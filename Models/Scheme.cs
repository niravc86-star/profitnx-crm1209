namespace ProfitNx.CRM.Models;

public class Scheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string SchemeType { get; set; } = "Margin";
    public string Description { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(1);
    public decimal ExtraMarginPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public string ApplicableRoles { get; set; } = "User,Partner";
    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}
