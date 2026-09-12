namespace ProfitNx.CRM.Models;

public class CommissionRow
{
    public string InquiryId { get; set; } = string.Empty;
    public DateTime InquiryDate { get; set; }
    public string BillNo { get; set; } = string.Empty;
    public DateTime? BillDate { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string VersionType { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string AssignedUserName { get; set; } = string.Empty;
    public decimal AmountWithoutGst { get; set; }
    public decimal PartnerMarginPercent { get; set; }
    public decimal PartnerMarginAmount { get; set; }
    public decimal AfterPartnerMarginAmount { get; set; }
    public decimal MarginPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal AfterLessMarginAmount { get; set; }
    public decimal AmountWithGst { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
