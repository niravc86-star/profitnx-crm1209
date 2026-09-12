namespace ProfitNx.CRM.Models;

public class UserPermissionItem
{
    public string UserEmail { get; set; } = "";

    public string PermissionKey { get; set; } = "";

    public bool Allowed { get; set; }
}