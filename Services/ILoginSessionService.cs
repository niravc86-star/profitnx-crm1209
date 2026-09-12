using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface ILoginSessionService
{
    Task<LoginLog> RecordLoginAsync(AppUser user, string ipAddress, string userAgent, string sessionId);
    // True only while this user has a live, recently-active session (a Heartbeat
    // pulse seen in the last 15 minutes) — i.e. they are genuinely still using
    // the CRM right now (another open tab, a silent token refresh, etc.).
    // False once that activity goes stale, whether or not they clicked Logout —
    // closing the app/browser directly still counts as "gone" after the window
    // passes, so their next real login is treated as fresh and alerts Admin.
    // Used by AccountController.Login BEFORE RecordLoginAsync to decide whether
    // a login is alert-worthy.
    Task<bool> HasActiveSessionAsync(string userId);
    Task RecordLogoutAsync(string userId, string? sessionId = null);
    Task HeartbeatAsync(string userId, string fullName, string userName, string role, string ipAddress, string userAgent);
    Task<List<LiveUserInfo>> GetLiveUsersAsync(TimeSpan? activeWithin = null);
    Task<int> GetLiveCountAsync(TimeSpan? activeWithin = null);
    Task<List<LoginLog>> GetLoginHistoryAsync(DateTime? from = null, DateTime? to = null, string? userId = null);
    Task<bool> DeleteLoginHistoryAsync(string id);
    Task<int> DeleteAllLoginHistoryAsync();
    Task<int> DeleteLoginHistoryManyAsync(IEnumerable<string> ids);
}

