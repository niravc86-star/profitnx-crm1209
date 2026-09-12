using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.ViewModels;

public class PermissionMatrixViewModel
{
    public List<RoleMaster> Roles { get; set; } = new();
    public List<PermissionItem> Items { get; set; } = new();
    public List<string> Groups { get; set; } = new();
    public List<AppUser> Users { get; set; } = new();
    public List<UserWisePermissionItem> UserItems { get; set; } = new();
    public HashSet<string> SeparateUserIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
