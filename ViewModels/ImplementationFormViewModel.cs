using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class ImplementationFormViewModel
{
    public string Id { get; set; } = string.Empty;
    public string InquiryId { get; set; } = string.Empty;
    [Required(ErrorMessage = "Company Name is required.")] public string FirmName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Person Name is required.")] public string PersonName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Mobile Number is required.")] public string Mobile { get; set; } = string.Empty;
    // FIX (2026-08-17): Customer Email must be compulsory - completion/feedback
    // mail relies on it, so a record with no email can never actually be reached
    // for feedback once training is done. Was previously optional (no [Required]).
    [Required(ErrorMessage = "Customer Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "City is required.")] public string City { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    [Range(1, 100000)] public int LicenseCount { get; set; } = 1;
    [Required(ErrorMessage = "License Number is required.")] public string LicenseNumber { get; set; } = string.Empty;
    public string PinNumber { get; set; } = string.Empty;
    public string BillNumber { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; } = DateTime.Today;
    public string Priority { get; set; } = "Normal";
    public string SupportHeadId { get; set; } = string.Empty;
    public string AssignedMemberId { get; set; } = string.Empty;
    public bool CustomerContacted { get; set; }
    public DateTime? PreferredStartDate { get; set; }
    public string PreferredTime { get; set; } = string.Empty;
    [Range(0, 365)] public int PlannedDays { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "New Implementation";
    public bool IsPaidTraining { get; set; }
    [Range(0, 999999999)] public decimal PaidTrainingAmount { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public bool TrainingRequired { get; set; } = true;

    // ADDED (2026-08-17): "user manual select karse... paid che k free" - whether a
    // training manual was given to the customer, and if so whether it was paid or
    // free. Paid amount/reference are only ever asked (and only ever shown in the
    // UI) once Manual Provided AND Paid are both selected - see Edit.cshtml JS.
    public bool ManualProvided { get; set; }
    public bool IsManualPaid { get; set; }
    [Range(0, 999999999)] public decimal ManualAmount { get; set; }
    public string ManualPaymentReference { get; set; } = string.Empty;
}
