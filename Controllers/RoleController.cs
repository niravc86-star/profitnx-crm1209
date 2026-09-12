using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class RoleController : Controller
{
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;
    private readonly IUserService _userService;

    public RoleController(IRoleService roleService, IPermissionService permissionService, IUserService userService)
    {
        _roleService = roleService;
        _permissionService = permissionService;
        _userService = userService;
    }

    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(User, "roles.view")) return RedirectToAction("AccessDenied", "Account");
        return View(await _roleService.GetAllAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(User, "roles.manage")) return RedirectToAction("AccessDenied", "Account");
        return View("Edit", new RoleFormViewModel { IsActive = true, LandingPage = "/Dashboard" });
    }

    [HttpPost]
    public async Task<IActionResult> Create(RoleFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "roles.manage")) return RedirectToAction("AccessDenied", "Account");
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "Role name is required.");
        if (!ModelState.IsValid) return View("Edit", model);
        await _roleService.SaveAsync(Map(model));
        TempData["Success"] = "Role saved successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "roles.manage")) return RedirectToAction("AccessDenied", "Account");
        var role = await _roleService.GetByIdAsync(id);
        if (role == null) return NotFound();
        return View(new RoleFormViewModel { Id = role.Id, Name = role.Name, Description = role.Description, IsActive = role.IsActive, LandingPage = role.LandingPage });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(RoleFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "roles.manage")) return RedirectToAction("AccessDenied", "Account");
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "Role name is required.");
        if (!ModelState.IsValid) return View(model);
        await _roleService.SaveAsync(Map(model));
        TempData["Success"] = "Role updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "roles.manage")) return RedirectToAction("AccessDenied", "Account");
        await _roleService.DeleteAsync(id);
        TempData["Success"] = "Role deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Permissions()
    {
        if (!await _permissionService.HasPermissionAsync(User, "permissions.manage")) return RedirectToAction("AccessDenied", "Account");
        return View(await _permissionService.GetMatrixAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Permissions(IFormCollection form)
    {
        if (!await _permissionService.HasPermissionAsync(User, "permissions.manage")) return RedirectToAction("AccessDenied", "Account");
        var matrix = await _permissionService.GetMatrixAsync();
        var dict = new Dictionary<string, bool>();
        foreach (var item in matrix.Items)
        {
            var key = $"perm_{item.RoleName}_{item.PermissionKey}".Replace('.', '_').Replace(' ', '_');
            dict[$"{item.RoleName}|{item.PermissionKey}"] = form.ContainsKey(key);
        }
        await _permissionService.SaveMatrixAsync(dict);
        // Clear in-memory cache so new role-wise rights apply immediately
        (_permissionService as ProfitNx.CRM.Services.PermissionService)?.ClearCache();
        TempData["Success"] = "Rights updated successfully.";
        return RedirectToAction(nameof(Permissions));
    }


    [HttpGet]
    public async Task<IActionResult> UserWiseRights()
    {
        if (!await _permissionService.HasPermissionAsync(User, "userpermissions.manage")) return RedirectToAction("AccessDenied", "Account");
        return View(await _permissionService.GetMatrixAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UserWiseRights(IFormCollection form)
    {
        if (!await _permissionService.HasPermissionAsync(User, "userpermissions.manage")) return RedirectToAction("AccessDenied", "Account");
        var matrix = await _permissionService.GetMatrixAsync();
        var dict = new Dictionary<string, bool>();
        var separateUserIds = matrix.Users
            .Where(user => form.ContainsKey($"separate_{user.Id}"))
            .Select(user => user.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var user in matrix.Users)
        {
            foreach (var def in PermissionCatalog.Definitions())
            {
                var key = $"userperm_{user.Id}_{def.PermissionKey}".Replace('.', '_').Replace(' ', '_');
                dict[$"{user.Id}|{def.PermissionKey}"] = form.ContainsKey(key);
            }
        }

        await _permissionService.SaveUserMatrixAsync(dict, separateUserIds);
        // Clear in-memory cache so new user-wise rights apply immediately
        (_permissionService as ProfitNx.CRM.Services.PermissionService)?.ClearCache();
        TempData["Success"] = "User wise rights updated successfully.";
        return RedirectToAction(nameof(UserWiseRights));
    }

    private static RoleMaster Map(RoleFormViewModel model) => new()
    {
        Id = model.Id ?? string.Empty,
        Name = model.Name,
        Description = model.Description,
        IsActive = model.IsActive,
        LandingPage = string.IsNullOrWhiteSpace(model.LandingPage) ? "/Dashboard" : model.LandingPage
    };

    [HttpPost]
    public async Task<IActionResult> ClearPermissionCache()
    {
        if (!await _permissionService.HasPermissionAsync(User, "permissions.manage")) return RedirectToAction("AccessDenied", "Account");
        (_permissionService as ProfitNx.CRM.Services.PermissionService)?.ClearCache();
        TempData["Success"] = "Permission cache cleared.";
        return RedirectToAction(nameof(Permissions));
    }
}
