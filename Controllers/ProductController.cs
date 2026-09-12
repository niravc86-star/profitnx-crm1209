using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class ProductController : Controller
{
    private readonly IProductService _productService;
    private readonly IPermissionService _permissionService;

    public ProductController(IProductService productService, IPermissionService permissionService)
    {
        _productService = productService;
        _permissionService = permissionService;
    }

    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(User, "product.view")) return RedirectToAction("AccessDenied", "Account");
        await LoadPriceRightsAsync();
        return View(await _productService.GetAllAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(User, "product.manage")) return RedirectToAction("AccessDenied", "Account");
        await LoadPriceRightsAsync();
        return View("Edit", new ProductFormViewModel { GstPercent = 18, IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "product.manage")) return RedirectToAction("AccessDenied", "Account");
        var (canDirectPrice, canPartnerPrice) = await LoadPriceRightsAsync();
        if (!canDirectPrice) model.PriceWithoutGst = 0;
        if (!canPartnerPrice) model.PartnerPriceWithoutGst = 0;
        Validate(model, canPartnerPrice);
        if (!ModelState.IsValid) return View("Edit", model);
        await _productService.SaveAsync(MapToModel(model));
        TempData["Success"] = "Product created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "product.manage")) return RedirectToAction("AccessDenied", "Account");
        await LoadPriceRightsAsync();
        var product = await _productService.GetByIdAsync(id);
        if (product == null) return NotFound();
        return View(new ProductFormViewModel
        {
            Id = product.Id, Code = product.Code, Category = product.Category, Name = product.Name,
            Version = product.Version, PriceWithoutGst = product.PriceWithoutGst,
            PartnerPriceWithoutGst = product.PartnerPriceWithoutGst,
            GstPercent = product.GstPercent, IsActive = product.IsActive, Notes = product.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "product.manage")) return RedirectToAction("AccessDenied", "Account");
        var existing = await _productService.GetByIdAsync(model.Id ?? string.Empty);
        if (existing == null) return NotFound();
        var (canDirectPrice, canPartnerPrice) = await LoadPriceRightsAsync();
        if (!canDirectPrice) model.PriceWithoutGst = existing.PriceWithoutGst;
        if (!canPartnerPrice) model.PartnerPriceWithoutGst = existing.PartnerPriceWithoutGst;
        Validate(model, canPartnerPrice);
        if (!ModelState.IsValid) return View(model);
        await _productService.SaveAsync(MapToModel(model));
        TempData["Success"] = "Product updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "product.manage")) return RedirectToAction("AccessDenied", "Account");
        await _productService.DeleteAsync(id);
        TempData["Success"] = "Product deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private void Validate(ProductFormViewModel model, bool canPartnerPrice)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "Product name is required.");
        if (canPartnerPrice && model.PartnerPriceWithoutGst <= 0 && model.PriceWithoutGst > 0)
            model.PartnerPriceWithoutGst = model.PriceWithoutGst;
    }

    private async Task<(bool CanDirectPrice, bool CanPartnerPrice)> LoadPriceRightsAsync()
    {
        var legacyPriceRight = await _permissionService.HasPermissionAsync(User, "field.product.price");
        var canDirectPrice = legacyPriceRight || await _permissionService.HasPermissionAsync(User, "field.product.directprice");
        var canPartnerPrice = legacyPriceRight || await _permissionService.HasPermissionAsync(User, "field.product.partnerprice");
        ViewBag.CanDirectPrice = canDirectPrice;
        ViewBag.CanPartnerPrice = canPartnerPrice;
        return (canDirectPrice, canPartnerPrice);
    }

    private static Product MapToModel(ProductFormViewModel model) => new()
    {
        Id = model.Id ?? string.Empty, Code = model.Code, Category = model.Category, Name = model.Name,
        Version = model.Version, PriceWithoutGst = model.PriceWithoutGst,
        PartnerPriceWithoutGst = model.PartnerPriceWithoutGst,
        GstPercent = model.GstPercent, IsActive = model.IsActive, Notes = model.Notes
    };
}
