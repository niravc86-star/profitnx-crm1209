namespace ProfitNx.CRM.ViewModels;

public class InquiryFormViewModel
{
    public string? Id { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Today;
    public string FirmName { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Mobile1 { get; set; } = string.Empty;
    public string? Mobile2 { get; set; }
    public string? Email1 { get; set; }
    public string? Email2 { get; set; }
    public string? ProductName { get; set; }
    public string VersionType { get; set; } = "New";
    // Remarks (Other Remark / Requirement) and StatusReason (Latest Follow-up Note) are intentionally
    // nullable so ASP.NET Core does NOT implicitly treat them as compulsory (required) fields.
    public string? Remarks { get; set; } = string.Empty;
    public string Status { get; set; } = "New Inquiry";
    public string? StatusReason { get; set; } = string.Empty;
    public string? ForwardedToPartnerId { get; set; }
    public string? ForwardedToUserId { get; set; }
    public string? DealerId { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public DateTime? DemoScheduledDate { get; set; }
    public DateTime? DemoDoneDate { get; set; }
    public int LicensesPurchased { get; set; }
    public string? LicenseNumber { get; set; }
    public string? BillNo { get; set; }
    public DateTime? BillDate { get; set; }
    public decimal AmountWithoutGst { get; set; }
    public decimal AmountWithGst { get; set; }
    public string? CloseReason { get; set; }
    public string? CustomerInfo { get; set; }
    public string? SupportExecutiveName { get; set; }
    public string? InquirySource { get; set; } = "CRM";
    public string? InquiryQuality { get; set; }
}
