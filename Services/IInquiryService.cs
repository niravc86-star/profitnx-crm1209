using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public interface IInquiryService
{
    Task<List<Inquiry>> GetAllAsync();
    Task<List<Inquiry>> SearchAsync(InquiryFilterViewModel filter, string? role = null, string? userId = null);
    Task<List<Inquiry>> GetVisibleForRoleAsync(string? role, string? userId);
    Task<List<Inquiry>> GetByPartnerAsync(string partnerId);
    Task<List<Inquiry>> GetBySupportUserAsync(string supportUserId);
    Task<List<Inquiry>> GetBySupportHeadAsync(string? supportHeadUserId = null);
    Task<Inquiry?> GetByIdAsync(string id);
    Task<List<InquiryUpdate>> GetUpdatesAsync(string inquiryId);
    Task CreateAsync(InquiryFormViewModel model, string currentUserId, string currentUserName, string currentUserRole);
    Task UpdateAsync(InquiryFormViewModel model, string currentUserId, string currentUserName, string currentUserRole);
    Task DeleteAsync(string id);
    Task UpdateStatusAsync(string inquiryId, string status, string note, string currentUserId, string currentUserName, string currentUserRole, DateTime? nextFollowUp, DateTime? demoScheduledDate, DateTime? demoDoneDate, string? licenseNumber, string? billNo, DateTime? billDate, string? closeReason, decimal? amountWithoutGst = null, decimal? amountWithGst = null, string? inquiryQuality = null, string? soldProductName = null, int? soldLicenses = null, string? forwardToPartnerId = null, string? forwardToUserId = null);
    Task<List<Inquiry>> GetPendingAttentionAsync(string role, string userId);
    Task MarkAttendedAsync(string inquiryId, string userId, string userName, string role);
    Task<int> GetMonthlySubmittedCountAsync(string userId, DateTime month);
}
