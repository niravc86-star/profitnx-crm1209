using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class ImplementationScheduleViewModel
{
    public string Id { get; set; } = string.Empty;
    [Required] public string CaseId { get; set; } = string.Empty;
    [Range(1, 365)] public int DayNumber { get; set; } = 1;
    [Required] public DateTime ScheduleDate { get; set; } = DateTime.Today;
    [Required] public string StartTime { get; set; } = "10:00";
    public string EndTime { get; set; } = string.Empty;
    // Planned Topics / Points is intentionally optional (not compulsory) when adding a training day.
    public string? TopicPlan { get; set; } = string.Empty;
    public string Stage { get; set; } = "Installation";
    public string AssignedMemberId { get; set; } = string.Empty;
}
