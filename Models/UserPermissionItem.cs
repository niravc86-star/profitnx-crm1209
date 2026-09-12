namespace ProfitNx.CRM.Models;

public class UserWisePermissionItem : PermissionItem
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
