namespace ProfitNx.CRM.ViewModels;

public class InquiryFilterViewModel
{
    public string? SearchText { get; set; }
    public string? Status { get; set; }
    public string? PartnerId { get; set; }
    public string? ProductName { get; set; }
    public string? AssignedUserId { get; set; }
    public string? SupportUser { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime? FollowUpFromDate { get; set; }
    public DateTime? FollowUpToDate { get; set; }
    public bool TodayFollowUps { get; set; }
    public string? InquiryQuality { get; set; }
    public string? Focus { get; set; }
}
