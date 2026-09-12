using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.ViewModels;

public class ImplementationDashboardViewModel
{
    public List<ImplementationCase> Cases { get; set; } = new();
    public List<ImplementationSchedule> UpcomingSchedules { get; set; } = new();
    public List<ImplementationExecutivePerformance> ExecutivePerformance { get; set; } = new();
    public int TotalCases { get; set; }
    public int AwaitingAssignment { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int AtRisk { get; set; }
    public decimal AverageRating { get; set; }
}

public class ImplementationExecutivePerformance
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int FeedbackCount { get; set; }
    public decimal AverageRating { get; set; }
    public string Level { get; set; } = "Pending";
}
