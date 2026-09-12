namespace ProfitNx.CRM.Models;

public class LiveDashboardViewModel
{
    public string RoleName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string PeriodLabel { get; set; } = "This Month";

    // Top KPI cards
    public int OrdersCompleted { get; set; }
    public decimal OrdersCompletedChangePct { get; set; }
    public int LiveInquiries { get; set; }
    public decimal LiveInquiriesChangePct { get; set; }
    public int ExpectedOrders { get; set; }
    public decimal ExpectedOrdersValue { get; set; }
    public int PendingFollowUps { get; set; }
    public int DueTodayFollowUps { get; set; }
    public decimal ConversionRate { get; set; }
    public decimal ConversionRateChangePct { get; set; }

    // Sales performance
    public decimal TotalSalesThisMonth { get; set; }
    public decimal TotalSalesLastMonth { get; set; }
    public decimal TargetThisMonth { get; set; }
    public decimal TargetAchievedPct { get; set; }
    public decimal SalesVsLastMonthPct { get; set; }
    public List<decimal> WeeklySales { get; set; } = new();
    public List<decimal> WeeklyTargets { get; set; } = new();
    public List<string> WeekLabels { get; set; } = new();

    // Orders status breakdown
    public int StatusCompleted { get; set; }
    public int StatusProcessing { get; set; }
    public int StatusPending { get; set; }
    public int StatusCancelled { get; set; }
    public int StatusOnHold { get; set; }
    public int TotalOrders => StatusCompleted + StatusProcessing + StatusPending + StatusCancelled + StatusOnHold;

    // Inquiries by type
    public int InqNew { get; set; }
    public int InqRepeat { get; set; }
    public int InqUpgrade { get; set; }
    public int InqCloud { get; set; }
    public int InqOthers { get; set; }
    public int TotalInquiries => InqNew + InqRepeat + InqUpgrade + InqCloud + InqOthers;

    // Stock summary
    public int TotalStockItems { get; set; }
    public decimal TotalStockValue { get; set; }
    public List<StockProductRow> TopProductsByStock { get; set; } = new();

    // Top dealers
    public List<DealerSalesRow> TopDealers { get; set; } = new();

    // Follow-up summary
    public int OverdueFollowUps { get; set; }
    public int UpcomingFollowUps { get; set; }
    public int CompletedFollowUpsThisMonth { get; set; }

    // Inquiry & conversion trend (weekly)
    public List<int> WeeklyInquiries { get; set; } = new();
    public List<int> WeeklyConversions { get; set; } = new();
    public List<decimal> WeeklyConversionPct { get; set; } = new();

    // Pipeline funnel
    public int PipelineTotalInquiries { get; set; }
    public int PipelineQualified { get; set; }
    public int PipelineProposalSent { get; set; }
    public int PipelineNegotiation { get; set; }
    public int PipelineExpectedOrders { get; set; }
    public decimal PipelineEstValue { get; set; }
    public decimal PipelineWeightedConversion { get; set; }

    // Recent activities
    public List<ActivityRow> RecentActivities { get; set; } = new();

    // Quick summary
    public int TotalPartners { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalUsers { get; set; }
    public int TotalProducts { get; set; }
    public int SupportTickets { get; set; }

    // Role-wise filters applied
    public string ScopeNote { get; set; } = string.Empty;

    // True only for Admin / SupportHead: the Yearly Target "Achieved" figure and the
    // "Who's Contributing" breakdown below are company-wide (Admin + every Partner +
    // every User combined). Every other role only ever sees their own scoped Sold
    // records here, so the UI must not show company-wide wording/tables to them.
    public bool IsCompanyWideScope { get; set; }

    // Yearly Target Planner — fully automatic:
    // Target is set by Admin (persisted server-side); Achieved is the
    // actual Sold amount for the year joining Admin + Partner + User sales
    // straight from CRM data (no manual entry).
    public int TargetYear { get; set; }
    public decimal YearlyTarget { get; set; }
    public decimal YearlyAchieved { get; set; }
    public decimal YearlyRemaining { get; set; }
    public decimal YearlyAchievedPct { get; set; }
    public decimal YearlyPaceDay { get; set; }
    public decimal YearlyPaceWeek { get; set; }
    public decimal YearlyPaceMonth { get; set; }
    public bool CanEditYearlyTarget { get; set; }
    public List<ProductVelocityRow> TopProductsByVelocity { get; set; } = new();
    public ProductVelocityRow? BestProductForTarget { get; set; }

    // Who is contributing to the Yearly Target: Admin's own direct sales, plus every
    // Partner and User whose sold inquiries count towards the same company-wide total.
    public List<YearlyContributionRow> YearlyContribution { get; set; } = new();

    // A few short, plain-language "what's happening" callouts generated from the same
    // CRM data already on this page (no separate AI service) — pace, biggest contributor,
    // stalled follow-ups, etc.
    public List<string> SmartInsights { get; set; } = new();

    // Smart Marketing Advisor — triggers only when this period's numbers look like a
    // genuine slowdown (sales down vs last month, weak conversion, or behind yearly
    // pace). Everything here is computed straight from existing CRM data (no external
    // AI call), the same way SmartInsights above works.
    public MarketingAdvisorViewModel MarketingAdvisor { get; set; } = new();
}

/// <summary>Data-driven "what to do about slow sales" advisor shown on the Live dashboard.</summary>
public class MarketingAdvisorViewModel
{
    public bool IsSlowdownDetected { get; set; }
    public string SlowdownSummary { get; set; } = string.Empty;
    public List<MarketingSuggestion> Suggestions { get; set; } = new();
    public List<MarketingBudgetOption> BudgetOptions { get; set; } = new();
}

/// <summary>One recommended marketing action — what type, what to actually do, and why (based on real CRM numbers).</summary>
public class MarketingSuggestion
{
    public string MarketingType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
}

/// <summary>One budget tier option with an approximate spend range and what it should realistically achieve.</summary>
public class MarketingBudgetOption
{
    public string Tier { get; set; } = string.Empty;
    public string BudgetLabel { get; set; } = string.Empty;
    public string ExpectedOutcome { get; set; } = string.Empty;
}

/// <summary>One contributor (Admin / a Partner / a User) towards the company Yearly Target.</summary>
public class YearlyContributionRow
{
    public string Name { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty; // "Admin (Direct)", "Partner", "User"
    public decimal Amount { get; set; }
    public int SoldCount { get; set; }
    public decimal PctOfTotal { get; set; }
}

/// <summary>
/// Real (not estimated) sales pace for a product this year, used to work
/// out — from actual CRM sold data — which product would close the yearly
/// target gap the fastest at its current selling pace.
/// </summary>
public class ProductVelocityRow
{
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int UnitsSoldThisYear { get; set; }
    public decimal RevenueThisYear { get; set; }
    public decimal RevenuePerDay { get; set; }
    public int UnitsNeeded { get; set; }
    public int EstimatedDaysToTarget { get; set; }
    /// <summary>True when the estimated days needed is either unknown (no pace) or further out than reasonably actionable (&gt; 1 year of pace) — UI shows a softer message instead of a huge/meaningless day count.</summary>
    public bool IsFarOut => EstimatedDaysToTarget == int.MaxValue || EstimatedDaysToTarget > 365;
}

public class StockProductRow
{
    public string ProductName { get; set; } = string.Empty;
    public int AvailableQty { get; set; }
    public decimal StockValue { get; set; }
}

public class DealerSalesRow
{
    public int Rank { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public int Orders { get; set; }
    public decimal ConversionPct { get; set; }
}

public class ActivityRow
{
    public string Icon { get; set; } = "bi-circle";
    public string ColorClass { get; set; } = "text-primary";
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
}
