namespace ProfitNx.CRM.Models;

public class InquiryUpdate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InquiryId { get; set; } = string.Empty;
    public DateTime UpdateDate { get; set; } = DateTime.Now;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime? NextFollowUpDate { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string BillNo { get; set; } = string.Empty;
    public DateTime? BillDate { get; set; }
    public string CloseReason { get; set; } = string.Empty;
}
