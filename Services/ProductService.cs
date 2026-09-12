using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class ProductService : IProductService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private const string ReadRange = "Products!A2:L";

    public ProductService(IGoogleSheetsService googleSheetsService)
    {
        _googleSheetsService = googleSheetsService;
    }

    public async Task<List<Product>> GetAllAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(ReadRange);
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0)))
            .Select(MapProduct)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Version)
            .ToList();
    }

    private static Product MapProduct(IList<object> r)
    {
        var directWithout = SheetValueHelper.GetDecimal(r, 5);
        var gst = SheetValueHelper.GetDecimal(r, 6);
        var directWith = SheetValueHelper.GetDecimal(r, 7);
        if (directWith <= 0 && directWithout > 0)
            directWith = Math.Round(directWithout + directWithout * gst / 100m, 2);

        // Old template: A:J. New template: A:L.
        var newLayout = r.Count >= 11;
        var partnerWithout = newLayout ? SheetValueHelper.GetDecimal(r, 8) : directWithout;
        if (partnerWithout <= 0) partnerWithout = directWithout;
        var partnerWith = newLayout ? SheetValueHelper.GetDecimal(r, 9) : 0m;
        if (partnerWith <= 0 && partnerWithout > 0)
            partnerWith = Math.Round(partnerWithout + partnerWithout * gst / 100m, 2);

        return new Product
        {
            Id = SheetValueHelper.GetString(r, 0),
            Code = SheetValueHelper.GetString(r, 1),
            Category = SheetValueHelper.GetString(r, 2),
            Name = SheetValueHelper.GetString(r, 3),
            Version = SheetValueHelper.GetString(r, 4),
            PriceWithoutGst = directWithout,
            GstPercent = gst,
            PriceWithGst = directWith,
            PartnerPriceWithoutGst = partnerWithout,
            PartnerPriceWithGst = partnerWith,
            IsActive = SheetValueHelper.GetBool(r, newLayout ? 10 : 8, true),
            Notes = SheetValueHelper.GetString(r, newLayout ? 11 : 9)
        };
    }

    public async Task<List<Product>> GetActiveAsync()
        => (await GetAllAsync()).Where(x => x.IsActive).ToList();

    public async Task<Product?> GetByIdAsync(string id)
        => (await GetAllAsync()).FirstOrDefault(x => x.Id == id);

    public async Task SaveAsync(Product product)
    {
        product.Id = string.IsNullOrWhiteSpace(product.Id) ? Guid.NewGuid().ToString() : product.Id;
        product.PriceWithGst = Math.Round(product.PriceWithoutGst + product.PriceWithoutGst * product.GstPercent / 100m, 2);
        if (product.PartnerPriceWithoutGst <= 0) product.PartnerPriceWithoutGst = product.PriceWithoutGst;
        product.PartnerPriceWithGst = Math.Round(product.PartnerPriceWithoutGst + product.PartnerPriceWithoutGst * product.GstPercent / 100m, 2);
        await _googleSheetsService.UpsertRowByIdAsync("Products", product.Id, ToRow(product));
    }

    public Task DeleteAsync(string id)
        => _googleSheetsService.DeleteRowsByIdAsync("Products", new[] { id });

    private static IList<object> ToRow(Product x) => new List<object>
    {
        x.Id, x.Code, x.Category, x.Name, x.Version,
        x.PriceWithoutGst, x.GstPercent, x.PriceWithGst,
        x.PartnerPriceWithoutGst, x.PartnerPriceWithGst,
        x.IsActive, x.Notes
    };
}
