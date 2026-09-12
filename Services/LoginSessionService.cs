using System.Collections.Concurrent;
using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

// NOTE (2026-08-13): This used to write to a local App_Data/login-history.json
// file. That file lives on local disk, which does not survive an app restart
// or a new deployment on most hosts - so the "Login History Report" would
// only ever show logins since the last restart, no matter what date range was
// selected, and the "history" quietly reset every time the app was redeployed.
// Every other record in this app (Notifications, Permissions, Users, etc.)
// is durable because it lives in Google Sheets - login history now does too.
public class LoginSessionService : ILoginSessionService
{
    private readonly IGoogleSheetsService _sheetsService;
    private static readonly ConcurrentDictionary<string, LiveUserInfo> Live = new(StringComparer.OrdinalIgnoreCase);

    // "Currently live" is intentionally still in-memory only - it is a
    // real-time, short-lived (15 minute) signal, not history, so it is fine
    // (and correct) for it to reset when the app restarts.
    private const string SheetName = "LoginHistory";
    private const string ReadRange = "LoginHistory!A2:L";
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaReady;

    public LoginSessionService(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
    }

    private static IList<object> Headers() => new List<object>
    {
        "Id", "UserId", "UserName", "FullName", "Role", "LoginAt", "LogoutAt",
        "IpAddress", "UserAgent", "DeviceSummary", "SessionId", "IsActive"
    };

    private async Task EnsureSchemaAsync()
    {
        if (_schemaReady) return;
        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaReady) return;
            await _sheetsService.EnsureSheetAsync(SheetName, Headers());
            _schemaReady = true;
        }
        finally { SchemaLock.Release(); }
    }

    // NOTE (2026-08-14): Reworked for the "Login Alert" spam fix — v2.
    // FIRST VERSION (2026-08-13) checked the durable LoginHistory sheet's
    // IsActive flag, i.e. "does this user have a session that was never
    // closed by Logout". That was WRONG: most people close the browser tab
    // / kill the CRM app directly instead of clicking Logout, so that row
    // stays IsActive=true forever — which meant every future login for that
    // user got silently treated as "already active" and NEVER alerted
    // Admin again, even days later. That is not the intent: closing the
    // app without logging out should still mean a *real* next login gets a
    // *real* alert.
    //
    // Correct signal for "is this user genuinely still using the CRM right
    // now" is recency of the Heartbeat pulse (Views/Shared/_Layout.cshtml
    // pings /Account/Heartbeat every 60s while a tab is open), which is
    // exactly what the in-memory `Live` dictionary already tracks via
    // LastSeenAt — the same source GetLiveUsersAsync uses for the "who's
    // online now" dashboard. So: alert-suppress ONLY while a heartbeat has
    // been seen recently (still has an open tab); once heartbeats stop
    // (tab/app closed, with or without clicking Logout) and enough time has
    // passed, the next login is treated as a genuine fresh login and DOES
    // alert Admin, exactly as if they had clicked Logout first. Being
    // in-memory only, this also naturally resets to "not active" on every
    // app restart/redeploy, which is correct here too.
    // FIX (2026-08-17): v2 (above) relied ONLY on the in-memory `Live` heartbeat map.
    // That map is process-memory - it is correctly wiped on a real app restart, but it
    // is ALSO wiped on every deploy and every routine IIS/Kestrel app-pool recycle,
    // which happen while people are actively using the CRM, not just at planned
    // downtime. The moment that happens, every currently-logged-in user's heartbeat
    // history vanishes, so their very next login (even seconds/minutes later - a page
    // refresh, a re-post, a flaky connection retrying) looks like a brand-new session
    // and re-alerts Admin, even though nothing actually changed for that user. That
    // is exactly the "Login Alerts (6)" burst of near-duplicate alerts for the same
    // person minutes apart (screenshot: Ami Nathwani, 3 alerts within a 17-minute
    // span). The durable LoginHistory sheet survives restarts/recycles, so it is used
    // here as a second, independent signal: if THIS user's own last recorded login was
    // only moments ago, treat it as the same session continuing rather than a fresh
    // one, even if the in-memory heartbeat map has forgotten about them.
    public async Task<bool> HasActiveSessionAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;

        // Signal 1: a live heartbeat pulse (open tab) seen recently - cheap, in-memory.
        if (Live.TryGetValue(userId, out var info) && (DateTime.Now - info.LastSeenAt) <= TimeSpan.FromMinutes(15))
            return true;

        // Signal 2 (fallback): the durable history's own most recent login for this
        // user, in case the in-memory map was reset by a restart/recycle in between.
        var all = await ReadAllAsync();
        var lastLogin = all
            .Where(x => x.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LoginAt)
            .FirstOrDefault();
        return lastLogin != null && (DateTime.Now - lastLogin.LoginAt) <= TimeSpan.FromMinutes(15);
    }

    public async Task<LoginLog> RecordLoginAsync(AppUser user, string ipAddress, string userAgent, string sessionId)
    {
        await EnsureSchemaAsync();
        var log = new LoginLog
        {
            UserId = user.Id ?? string.Empty,
            UserName = user.Username ?? string.Empty,
            FullName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username ?? "" : user.FullName,
            Role = user.Role ?? string.Empty,
            LoginAt = DateTime.Now,
            IpAddress = ipAddress ?? string.Empty,
            UserAgent = userAgent ?? string.Empty,
            DeviceSummary = SummarizeDevice(userAgent),
            SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
            IsActive = true
        };

        Live[log.UserId] = new LiveUserInfo
        {
            UserId = log.UserId,
            FullName = log.FullName,
            UserName = log.UserName,
            Role = log.Role,
            LoginAt = log.LoginAt,
            LastSeenAt = log.LoginAt,
            DeviceSummary = log.DeviceSummary,
            IpAddress = log.IpAddress
        };

        // Close out any previous still-active session rows for this same user
        // before appending the new one, so a user is never shown "Active"
        // twice in the timeline.
        var all = await ReadAllAsync();
        var stillActive = all.Where(x => x.UserId.Equals(log.UserId, StringComparison.OrdinalIgnoreCase) && x.IsActive).ToList();
        if (stillActive.Count > 0)
        {
            var now = DateTime.Now;
            var closeRows = stillActive.Select(prev =>
            {
                prev.IsActive = false;
                prev.LogoutAt ??= now;
                return (prev.Id, (IList<object>)ToRow(prev));
            });
            await _sheetsService.UpsertRowsByIdAsync("LoginHistory", closeRows);
        }

        await _sheetsService.AppendAsync("LoginHistory", ToRow(log));
        return log;
    }

    public async Task RecordLogoutAsync(string userId, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        await EnsureSchemaAsync();
        Live.TryRemove(userId, out _);
        var all = await ReadAllAsync();
        var now = DateTime.Now;
        var toClose = all.Where(x => x.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase) && x.IsActive
                     && (string.IsNullOrWhiteSpace(sessionId) || x.SessionId == sessionId)).ToList();
        if (toClose.Count == 0) return;

        var closeRows = toClose.Select(row =>
        {
            row.IsActive = false;
            row.LogoutAt = now;
            return (row.Id, (IList<object>)ToRow(row));
        });
        await _sheetsService.UpsertRowsByIdAsync("LoginHistory", closeRows);
    }

    public Task HeartbeatAsync(string userId, string fullName, string userName, string role, string ipAddress, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.CompletedTask;
        var now = DateTime.Now;
        Live.AddOrUpdate(userId,
            _ => new LiveUserInfo
            {
                UserId = userId,
                FullName = fullName,
                UserName = userName,
                Role = role,
                LoginAt = now,
                LastSeenAt = now,
                DeviceSummary = SummarizeDevice(userAgent),
                IpAddress = ipAddress ?? string.Empty
            },
            (_, existing) =>
            {
                existing.LastSeenAt = now;
                existing.FullName = fullName;
                existing.UserName = userName;
                existing.Role = role;
                if (!string.IsNullOrWhiteSpace(ipAddress)) existing.IpAddress = ipAddress;
                if (!string.IsNullOrWhiteSpace(userAgent)) existing.DeviceSummary = SummarizeDevice(userAgent);
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task<List<LiveUserInfo>> GetLiveUsersAsync(TimeSpan? activeWithin = null)
    {
        var window = activeWithin ?? TimeSpan.FromMinutes(15);
        var cutoff = DateTime.Now - window;
        var list = Live.Values
            .Where(x => x.LastSeenAt >= cutoff)
            .OrderByDescending(x => x.LastSeenAt)
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<int> GetLiveCountAsync(TimeSpan? activeWithin = null)
        => (await GetLiveUsersAsync(activeWithin)).Count;

    public async Task<List<LoginLog>> GetLoginHistoryAsync(DateTime? from = null, DateTime? to = null, string? userId = null)
    {
        var all = await ReadAllAsync();
        IEnumerable<LoginLog> q = all;
        if (from.HasValue) q = q.Where(x => x.LoginAt.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(x => x.LoginAt.Date <= to.Value.Date);
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(x => x.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
        return q.OrderByDescending(x => x.LoginAt).ToList();
    }

    public async Task<bool> DeleteLoginHistoryAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await EnsureSchemaAsync();
        var exists = (await ReadAllAsync()).Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (!exists) return false;
        await _sheetsService.DeleteRowsByIdAsync(SheetName, new[] { id });
        return true;
    }

    public async Task<int> DeleteLoginHistoryManyAsync(IEnumerable<string> ids)
    {
        var list = ids?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        if (list.Count == 0) return 0;
        await EnsureSchemaAsync();
        var existing = (await ReadAllAsync()).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toDelete = list.Where(existing.Contains).ToList();
        if (toDelete.Count == 0) return 0;
        await _sheetsService.DeleteRowsByIdAsync(SheetName, toDelete);
        return toDelete.Count;
    }

    public async Task<int> DeleteAllLoginHistoryAsync()
    {
        await EnsureSchemaAsync();
        var all = await ReadAllAsync();
        if (all.Count == 0) return 0;
        await _sheetsService.DeleteRowsByIdAsync(SheetName, all.Select(x => x.Id));
        return all.Count;
    }

    private async Task<List<LoginLog>> ReadAllAsync()
    {
        await EnsureSchemaAsync();
        var rows = await _sheetsService.ReadAsync(ReadRange);
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0)))
            .Select(r => new LoginLog
            {
                Id = SheetValueHelper.GetString(r, 0),
                UserId = SheetValueHelper.GetString(r, 1),
                UserName = SheetValueHelper.GetString(r, 2),
                FullName = SheetValueHelper.GetString(r, 3),
                Role = SheetValueHelper.GetString(r, 4),
                // FIX (2026-08-17): never fall back to "now" - a row that fails to
                // parse must look OLD (MinValue), not freshly-logged-in, or it would
                // permanently satisfy HasActiveSessionAsync's "logged in moments ago"
                // fallback check and wrongly suppress that user's next real login
                // alert forever. See SheetValueHelper.ToSheetText / GetString.
                LoginAt = SheetValueHelper.GetDateTime(r, 5) ?? DateTime.MinValue,
                LogoutAt = SheetValueHelper.GetDateTime(r, 6),
                IpAddress = SheetValueHelper.GetString(r, 7),
                UserAgent = SheetValueHelper.GetString(r, 8),
                DeviceSummary = SheetValueHelper.GetString(r, 9),
                SessionId = SheetValueHelper.GetString(r, 10),
                IsActive = SheetValueHelper.GetBool(r, 11, false)
            })
            .ToList();
    }

    private static IList<object> ToRow(LoginLog x) => new List<object>
    {
        x.Id, x.UserId, x.UserName, x.FullName, x.Role,
        SheetValueHelper.ToSheetText(x.LoginAt.ToString("yyyy-MM-dd HH:mm:ss")),
        x.LogoutAt.HasValue ? SheetValueHelper.ToSheetText(x.LogoutAt.Value.ToString("yyyy-MM-dd HH:mm:ss")) : string.Empty,
        x.IpAddress, x.UserAgent, x.DeviceSummary, x.SessionId, x.IsActive
    };

    public static string SummarizeDevice(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ua)) return "Unknown device";
        var browser =
            ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
            ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase) ? "Opera" :
            "Browser";
        var os =
            ua.Contains("Windows NT 10", StringComparison.OrdinalIgnoreCase) ? "Windows 10/11" :
            ua.Contains("Windows NT", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
            "PC/Device";
        return $"{browser} on {os}";
    }
}
