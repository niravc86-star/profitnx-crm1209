using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IUserService
{
    Task<AppUser?> ValidateUserAsync(string username, string password);
    Task<List<AppUser>> GetAllUsersAsync();
    Task<List<AppUser>> GetActiveUsersAsync();
    Task<List<AppUser>> GetPartnersAsync();
    Task<AppUser?> GetByIdAsync(string id);
    Task SaveAsync(AppUser user, string? plainPassword = null);
    Task SetActiveAsync(string id, bool isActive);
    Task SetThemePreferenceAsync(string id, string theme);
    Task DeleteAsync(string id);
}
