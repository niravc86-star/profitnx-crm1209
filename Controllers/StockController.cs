using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;
using System.Security.Claims;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class StockController : Controller
{
    private readonly IStockService _stockService;
    private readonly IProductService _productService;
    private readonly IUserService _userService;
    private readonly IPermissionService _permissionService;
    private readonly IDealerService _dealerService;

    public StockController(IStockService stockService, IProductService productService, IUserService userService, IPermissionService permissionService, IDealerService dealerService)
    {
        _stockService = stockService;
        _productService = productService;
        _userService = userService;
        _permissionService = permissionService;
        _dealerService = dealerService;
    }

    public async Task<IActionResult> Index(string? partnerId, string? productId, string? version, string? status, string? billNo, string? licenseNo, DateTime? billDate, DateTime? fromDate, DateTime? toDate, int? purchaseYear)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        if (!await _permissionService.HasPermissionAsync(User, "stock.view")) return RedirectToAction("AccessDenied", "Account");
        var stocks = await _stockService.GetAllAsync();
        var partners = await _userService.GetPartnersAsync();
        var products = await _productService.GetAllAsync();
        if (role.Equals("Partner", StringComparison.OrdinalIgnoreCase))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var current = partners.FirstOrDefault(x => x.Id == userId);
            stocks = stocks.Where(x => x.PartnerId == userId || (!string.IsNullOrWhiteSpace(current?.FullName) && x.PartnerName.Equals(current.FullName, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(current?.PartnerCode) && x.PartnerId.Equals(current.PartnerCode, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(partnerId))
        {
            var selectedDealer = (await _dealerService.GetActiveAsync()).FirstOrDefault(d => d.Id == partnerId);
            stocks = stocks.Where(x => x.PartnerId == partnerId || (selectedDealer != null && x.PartnerName.Equals(selectedDealer.DealerName, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        if (!string.IsNullOrWhiteSpace(productId)) stocks = stocks.Where(x => x.ProductId == productId || x.ProductName.Equals(products.FirstOrDefault(p=>p.Id==productId)?.Name ?? productId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(version)) stocks = stocks.Where(x => x.Version.Equals(version, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(billNo)) stocks = stocks.Where(x => (x.BillNo ?? string.Empty).Contains(billNo, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(licenseNo)) stocks = stocks.Where(x => (x.LicenseNumbers ?? string.Empty).Contains(licenseNo, StringComparison.OrdinalIgnoreCase)).ToList();
        if (billDate.HasValue) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Date == billDate.Value.Date).ToList();
        if (fromDate.HasValue) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Date >= fromDate.Value.Date).ToList();
        if (toDate.HasValue) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Date <= toDate.Value.Date).ToList();
        if (purchaseYear.HasValue && purchaseYear.Value > 0) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Year == purchaseYear.Value).ToList();
        if (status == "available") stocks = stocks.Where(x => x.AvailableStock > 0).ToList();
        if (status == "soldout") stocks = stocks.Where(x => x.AvailableStock <= 0).ToList();
        ViewBag.Products = products;
        ViewBag.Partners = partners;
        ViewBag.PartnerMasters = await _dealerService.GetActiveAsync();
        ViewBag.SelectedPartnerId = partnerId; ViewBag.SelectedProductId = productId; ViewBag.SelectedVersion = version; ViewBag.SelectedStatus = status; ViewBag.SelectedBillNo = billNo; ViewBag.SelectedLicenseNo = licenseNo; ViewBag.SelectedBillDate = billDate; ViewBag.SelectedFromDate = fromDate; ViewBag.SelectedToDate = toDate; ViewBag.SelectedPurchaseYear = purchaseYear; ViewBag.RoleName = role;
        return View(stocks);
    }



    public async Task<IActionResult> AmountReport(string? partnerId, string? billNo, string? licenseNo, DateTime? fromDate, DateTime? toDate, int? purchaseYear)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        if (!await _permissionService.HasPermissionAsync(User, "stock.amountreport.view")) return RedirectToAction("AccessDenied", "Account");
        var stocks = await _stockService.GetAllAsync();
        var partners = await _userService.GetPartnersAsync();
        if (role.Equals("Partner", StringComparison.OrdinalIgnoreCase))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var current = partners.FirstOrDefault(x => x.Id == userId);
            stocks = stocks.Where(x => x.PartnerId == userId || (!string.IsNullOrWhiteSpace(current?.FullName) && x.PartnerName.Equals(current.FullName, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(current?.PartnerCode) && x.PartnerId.Equals(current.PartnerCode, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(partnerId))
        {
            var selectedDealer = (await _dealerService.GetActiveAsync()).FirstOrDefault(d => d.Id == partnerId);
            stocks = stocks.Where(x => x.PartnerId == partnerId || (selectedDealer != null && x.PartnerName.Equals(selectedDealer.DealerName, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        if (!string.IsNullOrWhiteSpace(billNo)) stocks = stocks.Where(x => (x.BillNo ?? string.Empty).Contains(billNo, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(licenseNo)) stocks = stocks.Where(x => (x.LicenseNumbers ?? string.Empty).Contains(licenseNo, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fromDate.HasValue) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Date >= fromDate.Value.Date).ToList();
        if (toDate.HasValue) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Date <= toDate.Value.Date).ToList();
        if (purchaseYear.HasValue && purchaseYear.Value > 0) stocks = stocks.Where(x => x.LastPurchaseDate.HasValue && x.LastPurchaseDate.Value.Year == purchaseYear.Value).ToList();
        ViewBag.Partners = partners;
        ViewBag.RoleName = role;
        ViewBag.SelectedPartnerId = partnerId; ViewBag.SelectedBillNo = billNo; ViewBag.SelectedLicenseNo = licenseNo; ViewBag.SelectedFromDate = fromDate; ViewBag.SelectedToDate = toDate; ViewBag.SelectedPurchaseYear = purchaseYear;
        ViewData["Title"] = "Partner Stock Amount Report";
        ViewData["Subtitle"] = "Partner wise total stock qty/amount with product wise zoom and license number drill-down.";
        return View(stocks);
    }

    [HttpGet]
    public async Task<IActionResult> CreateBill()
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.bill.create") && !await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        await LoadBagsAsync();
        ViewBag.BillDate = DateTime.Today.ToString("yyyy-MM-dd");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CreateBill(IFormCollection form)
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.bill.create") && !await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        var partnerId = form["PartnerId"].ToString();
        var billNo = form["BillNo"].ToString();
        DateTime.TryParse(form["BillDate"].ToString(), out var billDate);
        var productIds = form["ProductId"].ToList();
        var versions = form["Version"].ToList();
        var qtys = form["TotalPurchased"].ToList();
        var reorderLevels = form["ReorderLevel"].ToList();
        var licenses = form["LicenseNumbers"].ToList();
        if (string.IsNullOrWhiteSpace(partnerId)) ModelState.AddModelError("PartnerId", "Please select partner.");
        if (string.IsNullOrWhiteSpace(billNo)) ModelState.AddModelError("BillNo", "Bill number is required.");
        if (billDate == default) ModelState.AddModelError("BillDate", "Bill date is required.");
        var saved = 0;
        for (var i = 0; i < productIds.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(productIds[i])) continue;
            var qty = 0; int.TryParse(i < qtys.Count ? qtys[i] : "0", out qty);
            if (qty <= 0) { ModelState.AddModelError("TotalPurchased", "Purchased qty must be greater than zero."); continue; }
            var licenseText = i < licenses.Count ? licenses[i] : string.Empty;
            var licenseCount = (licenseText ?? string.Empty).Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Count(x => !string.IsNullOrWhiteSpace(x));
            if (licenseCount != qty) ModelState.AddModelError("LicenseNumbers", $"Product row {i + 1}: license count must match qty.");
        }
        if (!ModelState.IsValid) { await LoadBagsAsync(); ViewBag.BillDate = billDate == default ? DateTime.Today.ToString("yyyy-MM-dd") : billDate.ToString("yyyy-MM-dd"); return View(); }
        for (var i = 0; i < productIds.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(productIds[i])) continue;
            var qty = 0; int.TryParse(i < qtys.Count ? qtys[i] : "0", out qty);
            if (qty <= 0) continue;
            // Reorder level is optional per bill line. When left blank, StockService
            // auto-carries-forward the last reorder level already set for this same
            // partner+product(+version) so the user does not have to re-enter it on
            // every purchase bill.
            var reorderLevel = 0; int.TryParse(i < reorderLevels.Count ? reorderLevels[i] : "0", out reorderLevel);
            var vm = new StockFormViewModel { PartnerId = partnerId, ProductId = productIds[i] ?? string.Empty, Version = i < versions.Count ? versions[i] ?? string.Empty : string.Empty, TotalPurchased = qty, TotalSold = 0, ReorderLevel = reorderLevel, BillNo = billNo, LastPurchaseDate = billDate, LicenseNumbers = i < licenses.Count ? licenses[i] ?? string.Empty : string.Empty };
            await _stockService.SaveAsync(await MapToModelAsync(vm));
            saved++;
        }
        TempData["Success"] = $"{saved} product stock row(s) saved under bill {billNo}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        await LoadBagsAsync();
        return View("Edit", new StockFormViewModel { LastPurchaseDate = DateTime.Today });
    }

    [HttpPost]
    public async Task<IActionResult> Create(StockFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        ValidateStock(model);
        if (!ModelState.IsValid) { await LoadBagsAsync(); return View("Edit", model); }
        await _stockService.SaveAsync(await MapToModelAsync(model));
        TempData["Success"] = "Partner stock saved successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        var stock = await _stockService.GetByIdAsync(id);
        if (stock == null) return NotFound();
        await LoadBagsAsync();
        return View(new StockFormViewModel
        {
            Id = stock.Id,
            ProductId = stock.ProductId,
            Version = stock.Version,
            PartnerId = stock.PartnerId,
            TotalPurchased = stock.TotalPurchased,
            TotalSold = stock.TotalSold,
            ReorderLevel = stock.ReorderLevel,
            WarehouseLocation = stock.WarehouseLocation,
            LastPurchaseDate = stock.LastPurchaseDate,
            Notes = stock.Notes,
            BillNo = stock.BillNo,
            LicenseNumbers = stock.LicenseNumbers
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(StockFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        ValidateStock(model);
        if (!ModelState.IsValid) { await LoadBagsAsync(); return View(model); }
        await _stockService.SaveAsync(await MapToModelAsync(model));
        TempData["Success"] = "Partner stock updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "stock.manage")) return RedirectToAction("AccessDenied", "Account");
        await _stockService.DeleteAsync(id);
        TempData["Success"] = "Stock item deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateStock(StockFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ProductId)) ModelState.AddModelError(nameof(model.ProductId), "Please select product.");
        if (string.IsNullOrWhiteSpace(model.PartnerId)) ModelState.AddModelError(nameof(model.PartnerId), "Please select partner.");
        if (model.TotalSold > model.TotalPurchased) ModelState.AddModelError(nameof(model.TotalSold), "Total sold cannot be greater than total purchased.");
        var licenseCount = (model.LicenseNumbers ?? string.Empty).Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Count(x => !string.IsNullOrWhiteSpace(x));
        if (model.TotalPurchased > 0 && licenseCount > 0 && licenseCount != model.TotalPurchased) ModelState.AddModelError(nameof(model.LicenseNumbers), "License number count must match purchased qty.");
        if (string.IsNullOrWhiteSpace(model.BillNo)) ModelState.AddModelError(nameof(model.BillNo), "Bill number is required.");
        if (!model.LastPurchaseDate.HasValue) ModelState.AddModelError(nameof(model.LastPurchaseDate), "Bill / purchase date is required.");
    }

    private async Task LoadBagsAsync()
    {
        ViewBag.Products = await _productService.GetActiveAsync();
        ViewBag.Partners = await _userService.GetPartnersAsync();
        ViewBag.PartnerMasters = await _dealerService.GetActiveAsync();
    }

    private async Task<StockItem> MapToModelAsync(StockFormViewModel model)
    {
        var product = await _productService.GetByIdAsync(model.ProductId);
        var partner = (await _userService.GetPartnersAsync()).FirstOrDefault(x => x.Id == model.PartnerId || x.PartnerCode == model.PartnerId);
        var dealers = await _dealerService.GetAllAsync();
        var dealer = dealers.FirstOrDefault(d => d.Id.Equals(model.PartnerId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.Id.Equals(partner?.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.DealerName.Equals(partner?.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.ContactPerson.Equals(partner?.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        return new StockItem
        {
            Id = model.Id ?? string.Empty,
            ProductId = model.ProductId,
            ProductName = product?.Name ?? string.Empty,
            Version = string.IsNullOrWhiteSpace(model.Version) ? product?.Version ?? string.Empty : model.Version,
            PartnerId = partner?.Id ?? model.PartnerId,
            PartnerName = dealer?.DealerName ?? partner?.FullName ?? string.Empty,
            TotalPurchased = model.TotalPurchased,
            TotalSold = model.TotalSold,
            ReorderLevel = model.ReorderLevel,
            WarehouseLocation = model.WarehouseLocation,
            LastPurchaseDate = model.LastPurchaseDate,
            Notes = model.Notes,
            BillNo = model.BillNo,
            LicenseNumbers = model.LicenseNumbers
        };
    }
}
