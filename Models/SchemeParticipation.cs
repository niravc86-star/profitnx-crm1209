namespace ProfitNx.CRM.Models;

public class SchemeParticipation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SchemeId { get; set; } = string.Empty;
    public string SchemeName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PartnerCode { get; set; } = string.Empty;
    public DateTime JoinedDate { get; set; } = DateTime.Today;
    public DateTime? ActiveUntil { get; set; }
}
