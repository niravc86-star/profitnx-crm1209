namespace ProfitNx.CRM.Models;

public class NotificationLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime SentDate { get; set; } = DateTime.Now;
    public string Channel { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public string RelatedEntityId { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public string ActionUrl { get; set; } = string.Empty;
    public string FlowType { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime? ReadDate { get; set; }
}
