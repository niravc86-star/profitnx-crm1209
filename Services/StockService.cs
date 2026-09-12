using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class StockService : IStockService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private readonly IProductService _productService;
    private readonly IUserService _userService;
    private readonly IDealerService _dealerService;
    private readonly INotificationService _notificationService;
    private const string ReadRange = "Stock!A2:Q";
    private const string ClearRange = "Stock!A2:Q5000";
    private const string WriteRange = "Stock!A2";

    public StockService(IGoogleSheetsService googleSheetsService, IProductService productService, IUserService userService, IDealerService dealerService, INotificationService notificationService)
    {
        _googleSheetsService = googleSheetsService;
        _productService = productService;
        _userService = userService;
        _dealerService = dealerService;
        _notificationService = notificationService;
    }

    // Fire the low-stock alert without ever letting a notification failure break the
    // actual stock save/sell operation that triggered it. Alerts are raised on the
    // TOTAL available stock for this partner+product(+version) across every stock
    // row (every bill / purchase entry) — not just the single row that was just
    // touched — since a customer/partner buying the same product multiple times
    // creates multiple stock rows over time.
    private async Task TryNotifyLowStockAsync(StockItem stock, List<StockItem>? allStocks = null)
    {
        try
        {
            var aggregate = await BuildAggregateSnapshotAsync(stock, allStocks);
            await _notificationService.NotifyLowStockAsync(aggregate);
        }
        catch { /* notification failures must never block stock updates */ }
    }

    // Groups every stock row that belongs to the same partner+product(+version) and
    // returns a synthetic snapshot carrying the TOTAL available stock and the
    // effective reorder level (the highest reorder level configured on any of those
    // rows), so the low-stock alert reflects the real combined stock position.
    private async Task<StockItem> BuildAggregateSnapshotAsync(StockItem reference, List<StockItem>? allStocks = null)
    {
        var stocks = allStocks ?? await GetAllAsync();
        var matches = FindMatchingRows(stocks, reference);
        var totalAvailable = matches.Sum(x => x.AvailableStock);
        var effectiveReorder = matches.Where(x => x.ReorderLevel > 0).Select(x => x.ReorderLevel).DefaultIfEmpty(0).Max();
        return new StockItem
        {
            Id = reference.Id,
            ProductId = reference.ProductId,
            ProductName = reference.ProductName,
            Version = reference.Version,
            PartnerId = reference.PartnerId,
            PartnerName = reference.PartnerName,
            TotalPurchased = totalAvailable,
            TotalSold = 0,
            ReorderLevel = effectiveReorder
        };
    }

    // Same partner (by Id or name) + same product (by Id or name) + same version
    // (when the reference row has a version specified). Mirrors the matching rules
    // already used by RegisterSoldAsync so "same stock" means the same thing
    // everywhere in this service.
    private static List<StockItem> FindMatchingRows(List<StockItem> stocks, StockItem reference) => stocks.Where(x =>
        (x.ProductName.Equals(reference.ProductName, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(reference.ProductId) && x.ProductId.Equals(reference.ProductId, StringComparison.OrdinalIgnoreCase))) &&
        ((!string.IsNullOrWhiteSpace(reference.PartnerId) && x.PartnerId.Equals(reference.PartnerId, StringComparison.OrdinalIgnoreCase)) ||
         (!string.IsNullOrWhiteSpace(reference.PartnerName) && x.PartnerName.Equals(reference.PartnerName, StringComparison.OrdinalIgnoreCase))) &&
        (string.IsNullOrWhiteSpace(reference.Version) || x.Version.Equals(reference.Version, StringComparison.OrdinalIgnoreCase))
    ).ToList();

    // Reorder Level is optional on the "Add Stock Bill (Multiple Product)" screen so
    // the user does not have to type it again for every repeat purchase of the same
    // partner+product(+version). When left blank (0), carry forward the last reorder
    // level that was already configured on any existing row for that same
    // partner+product(+version).
    private static int ResolveReorderLevel(List<StockItem> stocks, StockItem incoming)
    {
        if (incoming.ReorderLevel > 0) return incoming.ReorderLevel;
        var matches = FindMatchingRows(stocks, incoming).Where(x => x.Id != incoming.Id);
        var latest = matches.Where(x => x.ReorderLevel > 0).OrderByDescending(x => x.LastPurchaseDate ?? DateTime.MinValue).FirstOrDefault();
        return latest?.ReorderLevel ?? 0;
    }

    public async Task<List<StockItem>> GetAllAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(ReadRange);
        var list = rows
            .Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0)))
            .Select(r => new StockItem
            {
                Id = SheetValueHelper.GetString(r, 0),
                ProductId = SheetValueHelper.GetString(r, 1),
                ProductName = SheetValueHelper.GetString(r, 2),
                Version = SheetValueHelper.GetString(r, 3),
                PartnerId = SheetValueHelper.GetString(r, 4),
                PartnerName = SheetValueHelper.GetString(r, 5),
                TotalPurchased = SheetValueHelper.GetInt(r, 6),
                TotalSold = SheetValueHelper.GetInt(r, 7),
                ReorderLevel = SheetValueHelper.GetInt(r, 9),
                WarehouseLocation = SheetValueHelper.GetString(r, 10),
                LastPurchaseDate = SheetValueHelper.GetNullableDate(r, 11),
                Notes = SheetValueHelper.GetString(r, 12),
                ApproxValueWithoutGst = SheetValueHelper.GetDecimal(r, 13),
                ApproxValueWithGst = SheetValueHelper.GetDecimal(r, 14),
                BillNo = SheetValueHelper.GetString(r, 15),
                LicenseNumbers = SheetValueHelper.GetString(r, 16)
            })
            .OrderBy(x => x.PartnerName)
            .ThenBy(x => x.ProductName)
            .ThenBy(x => x.Version)
            .ToList();

        // Self-heal any legacy rows that were auto-created (or otherwise saved)
        // with Sold > Purchased. Negative balance / negative stock value should
        // never be shown anywhere in the CRM, so bring Purchased up to Sold and
        // clamp any already-stored negative value fields back to zero. The fix
        // is persisted once so the underlying data stays clean going forward.
        var corrected = new List<StockItem>();
        foreach (var item in list)
        {
            var needsFix = false;
            if (item.TotalSold > item.TotalPurchased) { item.TotalPurchased = item.TotalSold; needsFix = true; }
            if (item.ApproxValueWithoutGst < 0) { item.ApproxValueWithoutGst = 0; needsFix = true; }
            if (item.ApproxValueWithGst < 0) { item.ApproxValueWithGst = 0; needsFix = true; }
            if (needsFix) corrected.Add(item);
        }
        foreach (var item in corrected)
        {
            await CalculateValueAsync(item);
            await _googleSheetsService.UpsertRowByIdAsync("Stock", item.Id, ToRow(item));
        }

        return list;
    }

    public async Task<StockItem?> GetByIdAsync(string id)
        => (await GetAllAsync()).FirstOrDefault(x => x.Id == id);

    public async Task SaveAsync(StockItem stockItem)
    {
        var stocks = await GetAllAsync();
        var existing = stocks.FirstOrDefault(x => x.Id == stockItem.Id);

        // Auto carry-forward the reorder level from prior stock rows of the same
        // partner+product(+version) when this save did not specify one, so repeat
        // purchases (e.g. via Add Stock Bill) don't need it re-entered every time.
        stockItem.ReorderLevel = ResolveReorderLevel(stocks, stockItem);

        if (existing == null)
        {
            stockItem.Id = string.IsNullOrWhiteSpace(stockItem.Id) ? Guid.NewGuid().ToString() : stockItem.Id;
            stocks.Add(stockItem);
        }
        else
        {
            existing.ProductId = stockItem.ProductId;
            existing.ProductName = stockItem.ProductName;
            existing.Version = stockItem.Version;
            existing.PartnerId = stockItem.PartnerId;
            existing.PartnerName = stockItem.PartnerName;
            existing.TotalPurchased = stockItem.TotalPurchased;
            existing.TotalSold = stockItem.TotalSold;
            existing.ReorderLevel = stockItem.ReorderLevel;
            existing.WarehouseLocation = stockItem.WarehouseLocation;
            existing.LastPurchaseDate = stockItem.LastPurchaseDate;
            existing.BillNo = stockItem.BillNo;
            existing.LicenseNumbers = stockItem.LicenseNumbers;
            existing.Notes = stockItem.Notes;
            existing.ApproxValueWithoutGst = stockItem.ApproxValueWithoutGst;
            existing.ApproxValueWithGst = stockItem.ApproxValueWithGst;
        }

        var rowToSave = existing ?? stockItem;
        await CalculateValueAsync(rowToSave);
        await _googleSheetsService.UpsertRowByIdAsync("Stock", rowToSave.Id, ToRow(rowToSave));
        await TryNotifyLowStockAsync(rowToSave, stocks);
    }

    public async Task RegisterSoldAsync(string productName, string version, string partnerId, string partnerName, int quantity, string? licenseNumber = null, string? billNo = null, DateTime? billDate = null)
    {
        if (quantity <= 0 || string.IsNullOrWhiteSpace(productName)) return;

        var stocks = await GetAllAsync();
        var partnerStocks = stocks.Where(x =>
            x.ProductName.Equals(productName, StringComparison.OrdinalIgnoreCase) &&
            (
                (!string.IsNullOrWhiteSpace(partnerId) && x.PartnerId.Equals(partnerId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(partnerName) && x.PartnerName.Equals(partnerName, StringComparison.OrdinalIgnoreCase))
            )).ToList();

        var stock = partnerStocks.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(version) && x.Version.Equals(version, StringComparison.OrdinalIgnoreCase) && x.AvailableStock > 0)
            ?? partnerStocks.FirstOrDefault(x => x.AvailableStock > 0)
            ?? partnerStocks.FirstOrDefault(x => string.IsNullOrWhiteSpace(version) || x.Version.Equals(version, StringComparison.OrdinalIgnoreCase));

        if (stock == null)
        {
            // No partner stock record exists yet for this product/version. Auto-create
            // one, but set Purchased equal to Sold so the balance/value never shows as
            // negative. Admin can still correct the real Purchased qty afterwards; the
            // Notes call this out explicitly.
            stock = new StockItem
            {
                Id = Guid.NewGuid().ToString(),
                ProductName = productName,
                Version = version,
                PartnerId = partnerId,
                PartnerName = string.IsNullOrWhiteSpace(partnerName) ? "Unassigned Partner" : partnerName,
                TotalPurchased = quantity,
                TotalSold = quantity,
                ReorderLevel = 0,
                LastPurchaseDate = billDate ?? DateTime.Today,
                BillNo = billNo ?? string.Empty,
                LicenseNumbers = licenseNumber ?? string.Empty,
                Notes = "Auto-created from sold inquiry. Purchased qty was auto-set equal to sold qty to avoid negative stock — update it if partner stock was not already entered."
            };
            stocks.Add(stock);
        }
        else
        {
            stock.TotalSold += quantity;
            // Never let Sold exceed Purchased — negative balance / negative stock
            // value must never be shown, so top up Purchased to match.
            if (stock.TotalSold > stock.TotalPurchased)
            {
                stock.TotalPurchased = stock.TotalSold;
                stock.Notes = string.IsNullOrWhiteSpace(stock.Notes)
                    ? "Purchased qty was auto-adjusted to match sold qty to avoid negative stock — please verify."
                    : stock.Notes;
            }
            if (!string.IsNullOrWhiteSpace(billNo) && string.IsNullOrWhiteSpace(stock.BillNo)) stock.BillNo = billNo;
            if (billDate.HasValue && !stock.LastPurchaseDate.HasValue) stock.LastPurchaseDate = billDate;
            if (!string.IsNullOrWhiteSpace(licenseNumber) && !stock.LicenseNumbers.Contains(licenseNumber, StringComparison.OrdinalIgnoreCase))
                stock.LicenseNumbers = string.IsNullOrWhiteSpace(stock.LicenseNumbers) ? licenseNumber : stock.LicenseNumbers + "\n" + licenseNumber;
        }

        await CalculateValueAsync(stock);
        await _googleSheetsService.UpsertRowByIdAsync("Stock", stock.Id, ToRow(stock));
        await TryNotifyLowStockAsync(stock, stocks);
    }

    public Task DeleteAsync(string id) => _googleSheetsService.DeleteRowsByIdAsync("Stock", new[] { id });

    private async Task CalculateValueAsync(StockItem x)
    {
        var products = await _productService.GetAllAsync();
        var partners = await _userService.GetPartnersAsync();
        var dealers = await _dealerService.GetAllAsync();
        var product = products.FirstOrDefault(p => p.Id == x.ProductId || p.Name.Equals(x.ProductName, StringComparison.OrdinalIgnoreCase));
        if (product == null) return;
        var partner = partners.FirstOrDefault(u => u.Id == x.PartnerId || u.PartnerCode.Equals(x.PartnerId ?? string.Empty, StringComparison.OrdinalIgnoreCase) || u.FullName.Equals(x.PartnerName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var dealer = dealers.FirstOrDefault(d => d.Id.Equals(partner?.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase) || d.Id.Equals(x.PartnerId ?? string.Empty, StringComparison.OrdinalIgnoreCase) || d.DealerName.Equals(x.PartnerName ?? string.Empty, StringComparison.OrdinalIgnoreCase) || d.ContactPerson.Equals(x.PartnerName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var marginPercent = Math.Clamp(dealer?.MarginPercent ?? partner?.MarginPercent ?? 0m, 0m, 100m);
        if (dealer != null) x.PartnerName = dealer.DealerName;
        var grossWithout = x.AvailableStock * product.PartnerPriceWithoutGst;
        var grossWith = x.AvailableStock * product.PartnerPriceWithGst;
        x.ApproxValueWithoutGst = Math.Round(grossWithout * (1m - marginPercent / 100m), 2);
        x.ApproxValueWithGst = Math.Round(grossWith * (1m - marginPercent / 100m), 2);
    }

    private static IList<object> ToRow(StockItem x) => new List<object>
    {
        x.Id, x.ProductId, x.ProductName, x.Version, x.PartnerId, x.PartnerName,
        x.TotalPurchased, x.TotalSold, x.AvailableStock, x.ReorderLevel, x.WarehouseLocation,
        x.LastPurchaseDate?.ToString("yyyy-MM-dd") ?? string.Empty, x.Notes,
        x.ApproxValueWithoutGst, x.ApproxValueWithGst, x.BillNo, x.LicenseNumbers
    };
}
