using System.Security.Claims;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionKey);
    Task<PermissionMatrixViewModel> GetMatrixAsync();
    Task SaveMatrixAsync(Dictionary<string, bool> values);
    Task SaveUserMatrixAsync(Dictionary<string, bool> values, HashSet<string> separateUserIds);
    Task SeedDefaultsAsync();
}
