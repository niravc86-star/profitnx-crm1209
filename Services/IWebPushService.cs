namespace ProfitNx.CRM.Services;

public interface IWebPushService
{
    bool IsConfigured { get; }
    string GetPublicKey();
    Task SaveSubscriptionAsync(string userId, string userName, string endpoint, string p256dh, string auth, string userAgent);
    Task RemoveSubscriptionAsync(string userId, string endpoint);
    Task RemoveEndpointAsync(string endpoint);
    Task SendToUserAsync(string userId, string title, string body, string url, string tag);
    Task SendToUsersAsync(IEnumerable<string> userIds, string title, string body, string url, string tag);
}
