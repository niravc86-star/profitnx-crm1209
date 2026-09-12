namespace ProfitNx.CRM.Models;

public class ImplementationCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InquiryId { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; } = DateTime.Today;
    public string FirmName { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int LicenseCount { get; set; } = 1;
    public string LicenseNumber { get; set; } = string.Empty;
    public string PinNumber { get; set; } = string.Empty;
    public string BillNumber { get; set; } = string.Empty;
    public string SoldByUserId { get; set; } = string.Empty;
    public string SoldByName { get; set; } = string.Empty;
    public string SoldByRole { get; set; } = string.Empty;
    public string SupportHeadId { get; set; } = string.Empty;
    public string SupportHeadName { get; set; } = string.Empty;
    public string AssignedMemberId { get; set; } = string.Empty;
    public string AssignedMemberName { get; set; } = string.Empty;
    public string Status { get; set; } = "New Sale";
    public string Stage { get; set; } = "Awaiting Assignment";
    public string Priority { get; set; } = "Normal";
    public int PlannedDays { get; set; }
    public int ProgressPercent { get; set; }
    public bool CustomerContacted { get; set; }
    public DateTime? PreferredStartDate { get; set; }
    public string PreferredTime { get; set; } = string.Empty;
    public DateTime? ActualStartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public bool IsTrainingCompleted { get; set; }
    public string LastProgressNote { get; set; } = string.Empty;
    public string TransferFromUserId { get; set; } = string.Empty;
    public string TransferFromName { get; set; } = string.Empty;
    public string TransferReason { get; set; } = string.Empty;
    public string FeedbackToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? FeedbackSentDate { get; set; }
    public int FeedbackRating { get; set; }
    public string FeedbackRemarks { get; set; } = string.Empty;
    public DateTime? FeedbackDate { get; set; }
    public string AiRiskLevel { get; set; } = "Low";
    public string AiRecommendation { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string LastUpdatedByUserId { get; set; } = string.Empty;
    public string LastUpdatedByName { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "New Implementation";
    public bool IsPaidTraining { get; set; }
    public decimal PaidTrainingAmount { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public bool TrainingRequired { get; set; } = true;
    public bool ClosedWithoutTraining { get; set; }
    public string NoTrainingRemarks { get; set; } = string.Empty;
    public int ReopenedCount { get; set; }
    public DateTime? LastReopenedDate { get; set; }
    public string LastReopenedByName { get; set; } = string.Empty;
    public DateTime? LastTransferDate { get; set; }
    public string TrainingTopicsSummary { get; set; } = string.Empty;
    public bool IsPaused { get; set; }
    public string PauseReason { get; set; } = string.Empty;
    public DateTime? PausedDate { get; set; }
    public string PausedByName { get; set; } = string.Empty;
    public DateTime? ResumedDate { get; set; }
    public string StatusBeforePause { get; set; } = string.Empty;

    // ADDED (2026-08-17): training manual - given to customer or not, paid or free.
    public bool ManualProvided { get; set; }
    public bool IsManualPaid { get; set; }
    public decimal ManualAmount { get; set; }
    public string ManualPaymentReference { get; set; } = string.Empty;

    // Runtime-only visibility information. These values are not written to Google Sheets.
    public bool IsTransferredHistoryView { get; set; }
    public string TransferredHistoryNote { get; set; } = string.Empty;

    public string FeedbackPerformanceLevel => FeedbackRating >= 4 ? "Pro" : FeedbackRating >= 3 ? "Intermediate" : FeedbackRating > 0 ? "Beginner" : "Pending";
}
