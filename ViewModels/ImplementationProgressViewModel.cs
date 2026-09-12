using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class ImplementationProgressViewModel
{
    [Required] public string ScheduleId { get; set; } = string.Empty;
    [Required] public string CaseId { get; set; } = string.Empty;
    [Required] public string TopicsCovered { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed";
    public string Stage { get; set; } = "Training";
    [Range(0, 100)] public int ProgressPercent { get; set; }
}
