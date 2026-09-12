namespace ProfitNx.CRM.Models;

public class PermissionItem
{
    public string RoleName { get; set; } = string.Empty;
    public string PermissionKey { get; set; } = string.Empty;
    public string PermissionLabel { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public bool Allowed { get; set; }
}
