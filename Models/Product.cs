namespace ProfitNx.CRM.Models;

public class Product
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    // Backward compatible: existing PriceWithoutGst / PriceWithGst columns are
    // treated as the direct-customer price.
    public decimal PriceWithoutGst { get; set; }
    public decimal GstPercent { get; set; }
    public decimal PriceWithGst { get; set; }

    // Partner base price. Partner margin is deducted from this price only.
    public decimal PartnerPriceWithoutGst { get; set; }
    public decimal PartnerPriceWithGst { get; set; }

    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;

    public decimal GetPartnerNetPriceWithoutGst(decimal marginPercent)
        => Math.Round(PartnerPriceWithoutGst * (1m - Math.Clamp(marginPercent, 0m, 100m) / 100m), 2);

    public decimal GetPartnerNetPriceWithGst(decimal marginPercent)
        => Math.Round(PartnerPriceWithGst * (1m - Math.Clamp(marginPercent, 0m, 100m) / 100m), 2);
}
