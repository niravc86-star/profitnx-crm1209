namespace ProfitNx.CRM.Models;

public class RoleMaster
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string LandingPage { get; set; } = "/Dashboard";
}
