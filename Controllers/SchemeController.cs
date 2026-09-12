using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class SchemeController : Controller
{
    private readonly ISchemeService _schemeService;
    private readonly IUserService _userService;
    private readonly IPermissionService _permissionService;

    public SchemeController(ISchemeService schemeService, IUserService userService, IPermissionService permissionService)
    {
        _schemeService = schemeService;
        _userService = userService;
        _permissionService = permissionService;
    }

    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.view")) return RedirectToAction("AccessDenied", "Account");
        var schemes = await _schemeService.GetAllAsync();
        var participations = await _schemeService.GetParticipantsAsync();
        ViewBag.ParticipantCount = participations.GroupBy(x => x.SchemeId).ToDictionary(x => x.Key, x => x.Count());
        return View(schemes);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        return View("Edit", new SchemeFormViewModel { StartDate = DateTime.Today, EndDate = DateTime.Today.AddMonths(1), IsActive = true });
    }

    [HttpPost]
    public async Task<IActionResult> Create(SchemeFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        if (model.EndDate < model.StartDate) ModelState.AddModelError(nameof(model.EndDate), "End date must be greater than or equal to start date.");
        if (!ModelState.IsValid) return View("Edit", model);
        await _schemeService.SaveAsync(MapToModel(model));
        TempData["Success"] = "Scheme created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        var scheme = await _schemeService.GetByIdAsync(id);
        if (scheme == null) return NotFound();
        return View(new SchemeFormViewModel { Id = scheme.Id, Name = scheme.Name, SchemeType = scheme.SchemeType, Description = scheme.Description, StartDate = scheme.StartDate, EndDate = scheme.EndDate, ExtraMarginPercent = scheme.ExtraMarginPercent, DiscountAmount = scheme.DiscountAmount, TargetAmount = scheme.TargetAmount, ApplicableRoles = scheme.ApplicableRoles, IsActive = scheme.IsActive, Notes = scheme.Notes });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(SchemeFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        if (model.EndDate < model.StartDate) ModelState.AddModelError(nameof(model.EndDate), "End date must be greater than or equal to start date.");
        if (!ModelState.IsValid) return View(model);
        await _schemeService.SaveAsync(MapToModel(model));
        TempData["Success"] = "Scheme updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Participants(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.view")) return RedirectToAction("AccessDenied", "Account");
        var scheme = await _schemeService.GetByIdAsync(id);
        if (scheme == null) return NotFound();
        ViewBag.Scheme = scheme;
        ViewBag.AvailableUsers = await _userService.GetActiveUsersAsync();
        ViewBag.ParticipantVm = new SchemeParticipationFormViewModel { SchemeId = scheme.Id, JoinedDate = DateTime.Today };
        return View(await _schemeService.GetParticipantsBySchemeAsync(id));
    }

    [HttpPost]
    public async Task<IActionResult> AddParticipant(SchemeParticipationFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        var scheme = await _schemeService.GetByIdAsync(model.SchemeId);
        var user = await _userService.GetByIdAsync(model.UserId);
        if (scheme == null || user == null) { TempData["Error"] = "Scheme or user not found."; return RedirectToAction(nameof(Index)); }
        try
        {
            await _schemeService.AddParticipantAsync(new SchemeParticipation { SchemeId = scheme.Id, SchemeName = scheme.Name, UserId = user.Id, UserName = user.FullName, Role = user.Role, PartnerCode = user.PartnerCode, JoinedDate = model.JoinedDate, ActiveUntil = model.ActiveUntil });
            TempData["Success"] = "User/Partner joined successfully in scheme.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Participants), new { id = model.SchemeId });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveParticipant(string id, string schemeId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        await _schemeService.RemoveParticipantAsync(id);
        TempData["Success"] = "Scheme participant removed successfully.";
        return RedirectToAction(nameof(Participants), new { id = schemeId });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "scheme.manage")) return RedirectToAction("AccessDenied", "Account");
        await _schemeService.DeleteAsync(id);
        TempData["Success"] = "Scheme deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private static Scheme MapToModel(SchemeFormViewModel model) => new() { Id = model.Id ?? string.Empty, Name = model.Name, SchemeType = model.SchemeType, Description = model.Description, StartDate = model.StartDate, EndDate = model.EndDate, ExtraMarginPercent = model.ExtraMarginPercent, DiscountAmount = model.DiscountAmount, TargetAmount = model.TargetAmount, ApplicableRoles = model.ApplicableRoles, IsActive = model.IsActive, Notes = model.Notes };
}
