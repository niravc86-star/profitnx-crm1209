namespace ProfitNx.CRM.ViewModels;

public class CommissionFilterViewModel
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? PartnerId { get; set; }
    public string? UserId { get; set; }
}
