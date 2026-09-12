using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IRoleService
{
    Task<List<RoleMaster>> GetAllAsync();
    Task<RoleMaster?> GetByIdAsync(string id);
    Task<RoleMaster?> GetByNameAsync(string name);
    Task SaveAsync(RoleMaster role);
    Task DeleteAsync(string id);
}
