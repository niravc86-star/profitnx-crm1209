namespace ProfitNx.CRM.Services;

/// <summary>
/// Periodically pushes overdue / unattended inquiry alerts to subscribed browsers
/// even when the CRM tab is closed.
/// </summary>
public class PendingAttentionPushWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingAttentionPushWorker> _logger;
    private readonly Dictionary<string, string> _lastSignatureByUser = new(StringComparer.OrdinalIgnoreCase);

    public PendingAttentionPushWorker(IServiceScopeFactory scopeFactory, ILogger<PendingAttentionPushWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<IUserService>();
                var inquiry = scope.ServiceProvider.GetRequiredService<IInquiryService>();
                var push = scope.ServiceProvider.GetRequiredService<IWebPushService>();

                if (!push.IsConfigured)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                var active = (await users.GetAllUsersAsync()).Where(x => x.IsActive).ToList();
                foreach (var user in active)
                {
                    try
                    {
                        var items = await inquiry.GetPendingAttentionAsync(user.Role, user.Id);
                        if (items == null || items.Count == 0)
                        {
                            _lastSignatureByUser.Remove(user.Id);
                            continue;
                        }

                        // Signature of pending ids — only push when the set changes or every ~30 min re-alert
                        var signature = string.Join("|", items.Select(x => x.Id).OrderBy(x => x));
                        var tag = "profitnx-attention-" + user.Id;
                        var shouldPush = true;
                        if (_lastSignatureByUser.TryGetValue(user.Id, out var prev) && prev == signature)
                        {
                            // same set — skip (avoid spam); re-alert is handled client-side while open
                            shouldPush = false;
                        }

                        if (shouldPush)
                        {
                            _lastSignatureByUser[user.Id] = signature;
                            var first = items[0];
                            var customer = string.IsNullOrWhiteSpace(first.FirmName) ? first.PersonName : first.FirmName;
                            var reason = !first.IsAttended
                                ? $"Forwarded by {first.ForwardedByName}"
                                : "Next action overdue";
                            var title = "ProfitNx CRM - Pending Inquiry";
                            var body = items.Count == 1
                                ? $"{customer ?? "Inquiry"} · {reason}"
                                : $"{items.Count} inquiries need attention";
                            var url = "/Inquiry";
                            await push.SendToUserAsync(user.Id, title, body, url, tag);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Attention push skipped for user {UserId}", user.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending attention push worker failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }
}
