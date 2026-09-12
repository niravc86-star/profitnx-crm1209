namespace ProfitNx.CRM.Models;

public class ImplementationOtpApproval
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.Now;
    public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(10);
    public string ApproverNames { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public string UsedByUserId { get; set; } = string.Empty;
    public string UsedByName { get; set; } = string.Empty;
}
