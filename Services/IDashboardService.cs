using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(string role, string userId);
    Task<LiveDashboardViewModel> GetLiveDashboardAsync(string role, string userId, string userName, DateTime? fromDate = null, DateTime? toDate = null);
}

public class DashboardSummary
{
    public int TotalInquiries { get; set; }
    public int NewInquiries { get; set; }
    public int FollowUps { get; set; }
    public int DemoScheduled { get; set; }
    public int DemoDone { get; set; }
    public int SoldInquiries { get; set; }
    public int ClosedInquiries { get; set; }
    public int Closed { get; set; }
    public int Pending { get; set; }
    public int Demo { get; set; }
    public int TotalLicensesSold { get; set; }
    public decimal TotalSalesAmount { get; set; }
    public int GenuineInquiries { get; set; }
    public int NotGenuineInquiries { get; set; }
    public List<PartnerSalesSummary> PartnerSales { get; set; } = new();
    public List<PartnerSalesSummary> ProductSales { get; set; } = new();
    public List<PartnerSalesSummary> AdminBusiness { get; set; } = new();
    public List<PartnerSalesSummary> UserBusiness { get; set; } = new();
    public int SupportToAdminPipeline { get; set; }
    public int AdminToUserPipeline { get; set; }
    public int AdminToPartnerPipeline { get; set; }
}

public class PartnerSalesSummary
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int Total { get; set; }
    public int Active { get; set; }
    public int Sold { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal MarginPercent { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal NetAmount => GrossAmount - MarginAmount;
}
