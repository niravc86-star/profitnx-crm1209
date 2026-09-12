namespace ProfitNx.CRM.Models;

public class ImplementationSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public int DayNumber { get; set; } = 1;
    public DateTime ScheduleDate { get; set; } = DateTime.Today;
    public string StartTime { get; set; } = "10:00";
    public string EndTime { get; set; } = string.Empty;
    public string TopicPlan { get; set; } = string.Empty;
    public string TopicsCovered { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Stage { get; set; } = "Installation";
    public string Status { get; set; } = "Scheduled";
    public string AssignedMemberId { get; set; } = string.Empty;
    public string AssignedMemberName { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
