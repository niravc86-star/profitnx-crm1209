using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IDealerService
{
    Task<List<Dealer>> GetAllAsync();
    Task<List<Dealer>> GetActiveAsync();
    Task<Dealer?> GetByIdAsync(string id);
    Task SaveAsync(Dealer dealer);
    Task DeleteAsync(string id);
}
