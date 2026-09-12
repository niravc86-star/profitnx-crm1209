using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Hosting;
using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

/// <summary>
/// Stores browser push subscriptions in App_Data and delivers Web Push payloads
/// using VAPID. Works when the CRM tab is closed (OS/browser permitting).
/// </summary>
public class WebPushService : IWebPushService
{
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebPushService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _recentPushKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(90);

    private readonly string _publicKey;
    private readonly string _privateKey;
    private readonly string _subject;
    private readonly string _storePath;
    private readonly PushServiceClient? _client;

    public WebPushService(IHostEnvironment env, IConfiguration configuration, ILogger<WebPushService> logger)
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;

        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "push-subscriptions.json");

        _subject = (configuration["WebPush:Subject"] ?? "mailto:support@profitnx.com").Trim();
        _publicKey = (configuration["WebPush:PublicKey"] ?? string.Empty).Trim();
        _privateKey = (configuration["WebPush:PrivateKey"] ?? string.Empty).Trim();

        // Auto-bootstrap keys into App_Data if missing so push works out of the box in new deploys.
        // Production should override via appsettings / environment variables.
        if (string.IsNullOrWhiteSpace(_publicKey) || string.IsNullOrWhiteSpace(_privateKey))
        {
            var keyPath = Path.Combine(dataDir, "webpush-vapid.json");
            if (File.Exists(keyPath))
            {
                try
                {
                    var json = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(keyPath));
                    if (json != null)
                    {
                        _publicKey = json.GetValueOrDefault("publicKey") ?? string.Empty;
                        _privateKey = json.GetValueOrDefault("privateKey") ?? string.Empty;
                        if (json.TryGetValue("subject", out var s) && !string.IsNullOrWhiteSpace(s))
                            _subject = s;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read VAPID key file.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_publicKey) && !string.IsNullOrWhiteSpace(_privateKey))
        {
            _client = new PushServiceClient
            {
                DefaultAuthentication = new VapidAuthentication(_publicKey, _privateKey)
                {
                    Subject = _subject
                }
            };
        }
        else
        {
            _logger.LogWarning("Web Push is not configured (missing VAPID keys). Browser closed-tab push will be disabled.");
        }
    }

    public bool IsConfigured => _client != null && !string.IsNullOrWhiteSpace(_publicKey);
    public string GetPublicKey() => _publicKey;

    public async Task SaveSubscriptionAsync(string userId, string userName, string endpoint, string p256dh, string auth, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
            return;

        await _lock.WaitAsync();
        try
        {
            var list = await LoadAsync();
            var existing = list.FirstOrDefault(x =>
                x.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.UserId = userId;
                existing.UserName = userName;
                existing.P256dh = p256dh;
                existing.Auth = auth;
                existing.UserAgent = userAgent ?? string.Empty;
                existing.LastSeenAt = DateTime.UtcNow;
                existing.IsActive = true;
            }
            else
            {
                list.Add(new PushSubscriptionRecord
                {
                    UserId = userId,
                    UserName = userName,
                    Endpoint = endpoint,
                    P256dh = p256dh,
                    Auth = auth,
                    UserAgent = userAgent ?? string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    IsActive = true
                });
            }
            await SaveAsync(list);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveSubscriptionAsync(string userId, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        await _lock.WaitAsync();
        try
        {
            var list = await LoadAsync();
            var changed = list.RemoveAll(x =>
                x.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(userId) || x.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase)));
            if (changed > 0) await SaveAsync(list);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveEndpointAsync(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        await _lock.WaitAsync();
        try
        {
            var list = await LoadAsync();
            var changed = list.RemoveAll(x => x.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase));
            if (changed > 0) await SaveAsync(list);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task SendToUserAsync(string userId, string title, string body, string url, string tag)
        => SendToUsersAsync(new[] { userId }, title, body, url, tag);

    public async Task SendToUsersAsync(IEnumerable<string> userIds, string title, string body, string url, string tag)
    {
        if (!IsConfigured || _client == null) return;

        var idSet = userIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0) return;

        // Dedupe identical pushes briefly (forward storms, double-saves).
        var dedupeKey = string.Join("|", string.Join(",", idSet.OrderBy(x => x)), title, body, tag);
        var now = DateTime.UtcNow;
        foreach (var stale in _recentPushKeys.Where(x => now - x.Value > DuplicateWindow).Select(x => x.Key).ToList())
            _recentPushKeys.TryRemove(stale, out _);
        if (!_recentPushKeys.TryAdd(dedupeKey, now))
            return;

        List<PushSubscriptionRecord> targets;
        await _lock.WaitAsync();
        try
        {
            targets = (await LoadAsync())
                .Where(x => x.IsActive && idSet.Contains(x.UserId))
                .ToList();
        }
        finally
        {
            _lock.Release();
        }

        if (targets.Count == 0) return;

        var payload = JsonSerializer.Serialize(new
        {
            title = string.IsNullOrWhiteSpace(title) ? "ProfitNx CRM" : title,
            body = body ?? string.Empty,
            url = string.IsNullOrWhiteSpace(url) ? "/" : url,
            tag = string.IsNullOrWhiteSpace(tag) ? "profitnx" : tag
        });

        foreach (var sub in targets)
        {
            try
            {
                var pushSub = new PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = sub.P256dh,
                        ["auth"] = sub.Auth
                    }
                };
                var message = new PushMessage(payload)
                {
                    Topic = tag,
                    Urgency = PushMessageUrgency.High
                };
                await _client.RequestPushMessageDeliveryAsync(pushSub, message);
            }
            catch (PushServiceClientException ex) when (
                ex.StatusCode == HttpStatusCode.Gone ||
                ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Removing expired push endpoint for user {UserId}", sub.UserId);
                await RemoveEndpointAsync(sub.Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web Push delivery failed for user {UserId}", sub.UserId);
            }
        }
    }

    private async Task<List<PushSubscriptionRecord>> LoadAsync()
    {
        if (!File.Exists(_storePath)) return new List<PushSubscriptionRecord>();
        try
        {
            await using var stream = File.OpenRead(_storePath);
            var list = await JsonSerializer.DeserializeAsync<List<PushSubscriptionRecord>>(stream);
            return list ?? new List<PushSubscriptionRecord>();
        }
        catch
        {
            return new List<PushSubscriptionRecord>();
        }
    }

    private async Task SaveAsync(List<PushSubscriptionRecord> list)
    {
        var tmp = _storePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, list, new JsonSerializerOptions { WriteIndented = true });
        }
        File.Copy(tmp, _storePath, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }
    }
}
