using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IStockService
{
    Task<List<StockItem>> GetAllAsync();
    Task<StockItem?> GetByIdAsync(string id);
    Task SaveAsync(StockItem stockItem);
    Task DeleteAsync(string id);
    Task RegisterSoldAsync(string productName, string version, string partnerId, string partnerName, int quantity, string? licenseNumber = null, string? billNo = null, DateTime? billDate = null);
}
