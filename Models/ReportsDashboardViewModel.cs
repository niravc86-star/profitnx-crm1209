namespace ProfitNx.CRM.Models;

public class ReportsDashboardViewModel
{
    public int TotalInquiries { get; set; }
    public int SelfAttended { get; set; }
    public int PartnerForwarded { get; set; }
    public int FollowUps { get; set; }
    public int DemoScheduled { get; set; }
    public int DemoDone { get; set; }
    public int SoldCount { get; set; }
    public int ClosedCount { get; set; }
    public int TotalLicensesSold { get; set; }
    public decimal TotalSalesAmount { get; set; }
    public List<ProductReportRow> ProductWise { get; set; } = new();
    public List<PerformanceReportRow> PartnerWise { get; set; } = new();
    public List<PerformanceReportRow> UserWise { get; set; } = new();
    public List<PerformanceReportRow> SupportWise { get; set; } = new();
    public List<PerformanceReportRow> AdminWise { get; set; } = new();
    public List<StatusFlowReportRow> StatusFlow { get; set; } = new();
    public List<AiInsightItem> AiInsights { get; set; } = new();
    public List<AiPriorityLeadRow> AiPriorityLeads { get; set; } = new();
    public List<AiOpportunityRow> AiOpportunities { get; set; } = new();
    public List<AiFocusRow> AiProductFocus { get; set; } = new();
    public List<AiFocusRow> AiPartnerFocus { get; set; } = new();
    public List<SourceReportRow> SourceWise { get; set; } = new();
    public ImplementationReportSummary ImplementationSummary { get; set; } = new();
    public List<ImplementationCase> ImplementationCases { get; set; } = new();
}

public class SourceReportRow
{
    public string Source { get; set; } = string.Empty;
    public int TotalGiven { get; set; }
    public int Forwarded { get; set; }
    public string ForwardedToSummary { get; set; } = string.Empty;
    public int Sold { get; set; }
    public int Pending { get; set; }
    public int FollowUpPending { get; set; }
}

public class ImplementationReportSummary
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Paused { get; set; }
    public int Reopened { get; set; }
    public int PaidTraining { get; set; }
    public int Pending { get; set; }
}

public class ProductReportRow
{
    public string ProductName { get; set; } = string.Empty;
    public string VersionType { get; set; } = string.Empty;
    public int SoldCount { get; set; }
    public int TotalLicenses { get; set; }
    public decimal TotalAmount { get; set; }
}

public class PerformanceReportRow
{
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Sold { get; set; }
    public int FollowUps { get; set; }
    public int DemoScheduled { get; set; }
    public int DemoDone { get; set; }
    public int Closed { get; set; }
    public decimal SalesAmount { get; set; }
    public int ConversionPercent => Total == 0 ? 0 : (int)Math.Round((decimal)Sold * 100 / Total);
}

public class StatusFlowReportRow
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Percent { get; set; }
}

public class ProductPartyInfoViewModel
{
    public string ProductName { get; set; } = string.Empty;
    public string VersionType { get; set; } = string.Empty;
    public List<Inquiry> Inquiries { get; set; } = new();
}

public class AiInsightItem
{
    public string Icon { get; set; } = "bi-lightbulb";
    public string Title { get; set; } = string.Empty;
    public string Insight { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public int Score { get; set; }
}


public class AiPriorityLeadRow
{
    public string InquiryId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime? NextActionDate { get; set; }
    public int DaysOpen { get; set; }
    public int PriorityScore { get; set; }
    public string Priority { get; set; } = "Low";
    public string Reason { get; set; } = string.Empty;
}

public class AiOpportunityRow
{
    public string InquiryId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int ConversionScore { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class AiFocusRow
{
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Sold { get; set; }
    public int Active { get; set; }
    public int Overdue { get; set; }
    public int ConversionPercent { get; set; }
    public int OpportunityScore { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
}
