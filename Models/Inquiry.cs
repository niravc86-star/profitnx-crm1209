namespace ProfitNx.CRM.Models;

public class Inquiry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public string FirmName { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Mobile1 { get; set; } = string.Empty;
    public string Mobile2 { get; set; } = string.Empty;
    public string Email1 { get; set; } = string.Empty;
    public string Email2 { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string VersionType { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public string Status { get; set; } = "New Inquiry";
    public string StatusReason { get; set; } = string.Empty;
    public string ForwardedToPartnerId { get; set; } = string.Empty;
    public string ForwardedToPartnerName { get; set; } = string.Empty;
    public string ForwardedToUserId { get; set; } = string.Empty;
    public string ForwardedToUserName { get; set; } = string.Empty;
    public string AssignedUserId { get; set; } = string.Empty;
    public string AssignedUserName { get; set; } = string.Empty;
    public string AttendedByRole { get; set; } = string.Empty;
    public DateTime? NextFollowUpDate { get; set; }
    public DateTime? DemoScheduledDate { get; set; }
    public DateTime? DemoDoneDate { get; set; }
    public int LicensesPurchased { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string BillNo { get; set; } = string.Empty;
    public DateTime? BillDate { get; set; }
    public decimal AmountWithoutGst { get; set; }
    public decimal AmountWithGst { get; set; }
    public DateTime? SoldDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    public string CloseReason { get; set; } = string.Empty;
    public string CustomerInfo { get; set; } = string.Empty;
    public string DealerId { get; set; } = string.Empty;
    public string DealerName { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string SupportExecutiveName { get; set; } = string.Empty;
    public string InquirySource { get; set; } = string.Empty;
    public string SoldByName { get; set; } = string.Empty;
    public string SoldByRole { get; set; } = string.Empty;
    public string InquiryQuality { get; set; } = string.Empty;

    // Forwarding / attendance tracking. Old rows default to attended, so an
    // upgrade does not create false pending popups for historical inquiries.
    public string ForwardedToRole { get; set; } = string.Empty;
    public string ForwardedByUserId { get; set; } = string.Empty;
    public string ForwardedByName { get; set; } = string.Empty;
    public string ForwardedByRole { get; set; } = string.Empty;
    public DateTime? ForwardedDate { get; set; }
    public bool IsAttended { get; set; } = true;
    public DateTime? AttendedDate { get; set; }
    public string AttendedByUserId { get; set; } = string.Empty;
    public string AttendedByName { get; set; } = string.Empty;

    // Price audit fields used everywhere after sale calculation.
    public string PriceType { get; set; } = string.Empty; // Direct Customer / Partner
    public decimal UnitPriceWithoutGst { get; set; }
    public decimal UnitPriceWithGst { get; set; }
    public decimal AppliedMarginPercent { get; set; }
}
