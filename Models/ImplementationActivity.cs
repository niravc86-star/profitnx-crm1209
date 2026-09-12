namespace ProfitNx.CRM.Models;

public class ImplementationActivity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public DateTime ActivityDate { get; set; } = DateTime.Now;
    public string ActivityType { get; set; } = string.Empty;
    public string FromUserId { get; set; } = string.Empty;
    public string FromUserName { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public string ToUserName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string PerformedByUserId { get; set; } = string.Empty;
    public string PerformedByName { get; set; } = string.Empty;
}
