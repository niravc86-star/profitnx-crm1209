using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public interface INotificationService
{
    Task<List<NotificationLog>> GetLogsAsync();
    Task<List<NotificationLog>> GetForUserAsync(string userId, string loginName, string role, bool includeAll = false);
    Task<int> GetUnreadCountForUserAsync(string userId, string loginName, string role);
    Task QueueAsync(NotificationFormViewModel model);
    Task NotifySupportToAdminAsync(Inquiry inquiry, string supportUserName, string adminEmail);
    Task NotifyAdminToPartnerAsync(Inquiry inquiry, string partnerName, string partnerMobile, string partnerEmail);
    Task NotifyAdminToUserAsync(Inquiry inquiry, string userName, string userMobile, string userEmail);
    Task NotifyInquiryForwardAsync(Inquiry inquiry, string recipientName, string mobile, string email);
    Task NotifyInquiryCreatedAsync(Inquiry inquiry, string recipientName, string mobile, string email, string submittedByName, string submittedByRole);
    Task NotifyInquiryUpdateAsync(Inquiry inquiry, string recipientName, string mobile, string email, string updateNote);
    Task NotifyInquiryUpdateCrmOnlyAsync(Inquiry inquiry, string recipientName, string updateNote);
    Task SendImplementationNotificationAsync(ImplementationCase implementation, IEnumerable<AppUser> recipients, string title, string note, string actionUrl, string flowType, bool includeWhatsApp = true);
    Task SendFeedbackRequestAsync(ImplementationCase implementation, string feedbackUrl);
    Task SendFeedbackReceivedEmailAsync(ImplementationCase implementation, IEnumerable<AppUser> recipients, AppUser? senderUser);
    Task MarkAsReadAsync(IEnumerable<string> ids, string readerUserId, string readerName, string readerRole);
    Task DeleteAsync(IEnumerable<string> ids);
    Task ClearAllAsync();
    Task NotifyUserLoginAsync(string userId, string fullName, string userName, string role, string deviceSummary, string ipAddress);
    Task NotifyUserLogoutAsync(string userId, string fullName, string userName, string role, string deviceSummary, string ipAddress);
    Task NotifyLowStockAsync(StockItem stock);
    Task<(bool EmailSent, string WhatsAppUrl, string Status)> SendInquiryDocumentsAsync(
        Inquiry inquiry,
        string sendType,
        string channel,
        IEnumerable<string> attachmentPaths,
        string? customMessage,
        AppUser? senderUser);
}

