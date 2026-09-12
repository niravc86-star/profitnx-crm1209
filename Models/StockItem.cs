namespace ProfitNx.CRM.Models;

public class StockItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public int TotalPurchased { get; set; }
    public int TotalSold { get; set; }
    public int AvailableStock => TotalPurchased - TotalSold;
    public int ReorderLevel { get; set; }
    public string WarehouseLocation { get; set; } = string.Empty;
    public DateTime? LastPurchaseDate { get; set; }
    public string BillNo { get; set; } = string.Empty;
    public string LicenseNumbers { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public decimal ApproxValueWithoutGst { get; set; }
    public decimal ApproxValueWithGst { get; set; }
}
