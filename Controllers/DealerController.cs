using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class DealerController : Controller
{
    private readonly IDealerService _dealerService;
    private readonly IPermissionService _permission_service;
    private readonly IUserService _userService;

    public DealerController(IDealerService dealerService, IPermissionService permissionService, IUserService userService)
    {
        _dealerService = dealerService;
        _permission_service = permissionService;
        _userService = userService;
    }

    public async Task<IActionResult> Index()
    {
        if (!await _permission_service.HasPermissionAsync(User, "dealer.view")) return RedirectToAction("AccessDenied", "Account");
        var dealers = await _dealerService.GetAllAsync();
        // find which dealers don't have an associated Partner login yet
        try
        {
            var users = await _userService.GetAllUsersAsync();
            var partnerCodes = users.Where(u => u.Role == "Partner").Select(u => (u.PartnerCode ?? string.Empty).Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var needsLogin = dealers.Where(d => !partnerCodes.Contains(d.Id)).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ViewBag.PartnersNeedingLogin = needsLogin;
        }
        catch { ViewBag.PartnersNeedingLogin = new HashSet<string>(); }
        return View(dealers);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permission_service.HasPermissionAsync(User, "dealer.manage")) return RedirectToAction("AccessDenied", "Account");
        return View("Edit", new DealerFormViewModel { IsActive = true, JoiningDate = DateTime.Today });
    }

    [HttpPost]
    public async Task<IActionResult> Create(DealerFormViewModel model)
    {
        if (!await _permission_service.HasPermissionAsync(User, "dealer.manage")) return RedirectToAction("AccessDenied", "Account");
        await _dealerService.SaveAsync(Map(model));
        TempData["Success"] = "Partner created successfully.";
        TempData["PartnerNote"] = "Partner saved. To create the partner login, open <a href=\"/Admin/Create?role=Partner\">User Master (Create Partner Login)</a> and create the associated login.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await _permission_service.HasPermissionAsync(User, "dealer.manage")) return RedirectToAction("AccessDenied", "Account");
        var d = await _dealerService.GetByIdAsync(id);
        if (d == null) return NotFound();
        return View(new DealerFormViewModel
        {
            Id = d.Id, DealerName = d.DealerName, ContactPerson = d.ContactPerson, City = d.City, Mobile = d.Mobile, Email = d.Email,
            IsActive = d.IsActive, MarginPercent = d.MarginPercent, YearlyTargetAmount = d.YearlyTargetAmount, TargetYear = d.TargetYear, TargetFromDate = d.TargetFromDate, TargetToDate = d.TargetToDate, TargetRowsJson = d.TargetRowsJson, JoiningDate = d.JoiningDate, GivesPss = d.GivesPss, GivesApi = d.GivesApi,
            PssApiChangeDate = d.PssApiChangeDate, PssApiRemark = d.PssApiRemark, LeftDate = d.LeftDate, LeftRemark = d.LeftRemark, Notes = d.Notes
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(DealerFormViewModel model)
    {
        if (!await _permission_service.HasPermissionAsync(User, "dealer.manage")) return RedirectToAction("AccessDenied", "Account");
        await _dealerService.SaveAsync(Map(model));
        TempData["Success"] = "Partner updated successfully.";
        TempData["PartnerNote"] = "Partner saved. To create the partner login, open <a href=\"/Admin/Create?role=Partner\">User Master (Create Partner Login)</a> and create the associated login.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _permission_service.HasPermissionAsync(User, "dealer.manage")) return RedirectToAction("AccessDenied", "Account");
        await _dealerService.DeleteAsync(id);
        TempData["Success"] = "Partner deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private static Dealer Map(DealerFormViewModel model)
    {
        var dealer = new Dealer
        {
            Id = model.Id ?? string.Empty,
            DealerName = model.DealerName,
            ContactPerson = model.ContactPerson,
            City = model.City,
            Mobile = model.Mobile,
            Email = model.Email,
            IsActive = model.IsActive,
            MarginPercent = model.MarginPercent,
            YearlyTargetAmount = model.YearlyTargetAmount,
            TargetYear = model.TargetYear == 0 ? DateTime.Today.Year : model.TargetYear,
            TargetFromDate = model.TargetFromDate,
            TargetToDate = model.TargetToDate,
            TargetRowsJson = model.TargetRowsJson,
            JoiningDate = model.JoiningDate,
            GivesPss = model.GivesPss,
            GivesApi = model.GivesApi,
            PssApiChangeDate = model.PssApiChangeDate,
            PssApiRemark = model.PssApiRemark,
            LeftDate = model.LeftDate,
            LeftRemark = model.LeftRemark,
            Notes = model.Notes
        };

        // The "Year Wise Partner Target" grid (TargetRowsJson) is now the source of truth.
        // Keep the legacy single Year/Amount/From/To fields in sync with the current year's row
        // so older screens/reports that still read those fields (Partner Master grid, Reports)
        // show the right numbers instead of stale/zero data.
        var currentYearRow = dealer.GetTargetForYear(DateTime.Today.Year);
        if (currentYearRow != null)
        {
            dealer.TargetYear = currentYearRow.Year;
            dealer.YearlyTargetAmount = currentYearRow.Amount;
            dealer.TargetFromDate = currentYearRow.FromDate;
            dealer.TargetToDate = currentYearRow.ToDate;
        }

        return dealer;
    }
}
