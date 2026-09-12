using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class StockFormViewModel
{
    public string? Id { get; set; }

    [Display(Name = "Product")]
    public string ProductId { get; set; } = string.Empty;

    [Display(Name = "Version")]
    public string Version { get; set; } = string.Empty;

    [Display(Name = "Partner")]
    public string PartnerId { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    [Display(Name = "Total Purchased")]
    public int TotalPurchased { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Total Sold")]
    public int TotalSold { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Reorder Level")]
    public int ReorderLevel { get; set; }

    [Display(Name = "Warehouse / Location")]
    public string WarehouseLocation { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Purchase Date")]
    public DateTime? LastPurchaseDate { get; set; }

    [Display(Name = "Bill No")]
    public string BillNo { get; set; } = string.Empty;

    [Display(Name = "License Numbers")]
    public string LicenseNumbers { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}
