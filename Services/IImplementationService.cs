using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public interface IImplementationService
{
    Task EnsureSchemaAsync();
    Task<List<ImplementationCase>> GetVisibleCasesAsync(string role, string userId);
    Task<ImplementationCase?> GetByIdAsync(string id);
    Task<ImplementationCase?> FindCustomerByLicenseAsync(string licenseNumber);
    Task<ImplementationCase?> GetByFeedbackTokenAsync(string token);
    Task<List<ImplementationSchedule>> GetSchedulesAsync(string caseId);
    Task<List<ImplementationActivity>> GetActivitiesAsync(string caseId);
    Task<ImplementationCase> CreateAsync(ImplementationFormViewModel model, string actorUserId, string actorName, string actorRole);
    Task<ImplementationCase?> EnsureFromSoldInquiryAsync(Inquiry inquiry, string actorUserId, string actorName, string actorRole);
    Task UpdateAsync(ImplementationFormViewModel model, string actorUserId, string actorName);
    Task AssignAsync(string caseId, string supportHeadId, string assignedMemberId, string note, string actorUserId, string actorName);
    Task<ImplementationSchedule> SaveScheduleAsync(ImplementationScheduleViewModel model, string actorUserId, string actorName);
    Task UpdateProgressAsync(ImplementationProgressViewModel model, string actorUserId, string actorName);
    Task RequestOtpAsync(string caseId, string purpose, string actorUserId, string actorName);
    Task TransferAsync(string caseId, string newMemberId, string reason, string otp, string actorUserId, string actorName);
    Task ReopenAsync(string caseId, string remarks, string otp, string actorUserId, string actorName);
    Task CloseWithoutTrainingAsync(string caseId, string remarks, string actorUserId, string actorName);
    Task PauseAsync(string caseId, string reason, string actorUserId, string actorName);
    Task ResumeAsync(string caseId, string remarks, string actorUserId, string actorName);
    Task CompleteAsync(string caseId, IEnumerable<string> trainingTopics, string otherPointsCovered, string actorUserId, string actorName, string feedbackBaseUrl);
    Task SubmitFeedbackAsync(string token, int rating, string remarks);
    Task DeleteAsync(string caseId);
    Task<List<ImplementationSchedule>> GetUpcomingForUserAsync(string role, string userId, DateTime from, DateTime to);
    Task ProcessDueRemindersAsync(CancellationToken cancellationToken = default);
}
