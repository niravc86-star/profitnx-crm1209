using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class ProductFormViewModel
{
    public string? Id { get; set; }

    [Display(Name = "Product Code")]
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    [Display(Name = "Product Name")]
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    [Range(0, 999999999)]
    [Display(Name = "Direct Customer Price (Without GST)")]
    public decimal PriceWithoutGst { get; set; }

    [Range(0, 999999999)]
    [Display(Name = "Partner Product Price (Without GST)")]
    public decimal PartnerPriceWithoutGst { get; set; }

    [Range(0, 100)]
    [Display(Name = "GST %")]
    public decimal GstPercent { get; set; } = 18;

    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}
