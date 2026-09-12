using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IProductService
{
    Task<List<Product>> GetAllAsync();
    Task<List<Product>> GetActiveAsync();
    Task<Product?> GetByIdAsync(string id);
    Task SaveAsync(Product product);
    Task DeleteAsync(string id);
}
