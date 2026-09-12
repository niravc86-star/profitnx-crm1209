using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class AdminController : Controller
{
    private readonly IUserService _userService;
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;
    private readonly IDealerService _dealerService;
    private readonly ILoginSessionService _loginSessions;

    public AdminController(IUserService userService, IRoleService roleService, IPermissionService permissionService, IDealerService dealerService, ILoginSessionService loginSessions)
    {
        _userService = userService;
        _roleService = roleService;
        _permissionService = permissionService;
        _dealerService = dealerService;
        _loginSessions = loginSessions;
    }

    [HttpGet]
    public async Task<IActionResult> LoginReport(DateTime? fromDate, DateTime? toDate, string? userId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "loginreport.view")
            && !(User.IsInRole("Admin")))
            return RedirectToAction("AccessDenied", "Account");

        var today = DateTime.Today;
        fromDate ??= today.AddDays(-30);
        toDate ??= today;
        var logs = await _loginSessions.GetLoginHistoryAsync(fromDate, toDate, userId);
        var live = await _loginSessions.GetLiveUsersAsync();
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;
        ViewBag.UserId = userId ?? "";
        ViewBag.LiveUsers = live;
        ViewBag.LiveCount = live.Count;
        ViewBag.AllUsers = await _userService.GetAllUsersAsync();
        ViewBag.CanDeleteLoginHistory = await CanDeleteLoginHistoryAsync();
        return View(logs);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLoginHistory(string id, DateTime? fromDate, DateTime? toDate, string? userId)
    {
        if (!await CanDeleteLoginHistoryAsync())
            return RedirectToAction("AccessDenied", "Account");
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Error"] = "Login record id is required.";
            return RedirectToAction(nameof(LoginReport), new { fromDate, toDate, userId });
        }
        var ok = await _loginSessions.DeleteLoginHistoryAsync(id);
        TempData[ok ? "Success" : "Error"] = ok ? "Login history record deleted." : "Login record not found or already deleted.";
        return RedirectToAction(nameof(LoginReport), new { fromDate, toDate, userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAllLoginHistory(DateTime? fromDate, DateTime? toDate, string? userId)
    {
        if (!await CanDeleteLoginHistoryAsync())
            return RedirectToAction("AccessDenied", "Account");
        var count = await _loginSessions.DeleteAllLoginHistoryAsync();
        TempData["Success"] = count == 0 ? "No login history records to delete." : $"Deleted {count} login history record(s).";
        return RedirectToAction(nameof(LoginReport));
    }

    private async Task<bool> CanDeleteLoginHistoryAsync()
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase)) return true;
        return await _permissionService.HasPermissionAsync(User, "loginreport.delete")
            || await _permissionService.HasPermissionAsync(User, "loginreport.view");
    }



    public async Task<IActionResult> Users()
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.view")) return RedirectToAction("AccessDenied", "Account");
        return View(await _userService.GetAllUsersAsync());
    }


    [HttpPost]
    public async Task<IActionResult> ToggleActive(string id, bool isActive)
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.manage")) return RedirectToAction("AccessDenied", "Account");
        await _userService.SetActiveAsync(id, isActive);
        TempData["Success"] = isActive ? "User activated successfully." : "User deactivated successfully.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.manage")) return RedirectToAction("AccessDenied", "Account");
        await _userService.DeleteAsync(id);
        TempData["Success"] = "User deleted successfully.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> Create(string role = "User")
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.manage")) return RedirectToAction("AccessDenied", "Account");
        await LoadRolesAsync();
        await LoadPartnerMasterAsync();
        var model = new UserFormViewModel { Role = NormalizeRole(role), IsActive = true };
        if (Request.Query.ContainsKey("partnerId"))
        {
            model.PartnerCode = Request.Query["partnerId"].ToString();
        }
        return View("Edit", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(UserFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.manage")) return RedirectToAction("AccessDenied", "Account");
        if (string.IsNullOrWhiteSpace(model.Username)) ModelState.AddModelError(nameof(model.Username), "Username is required.");
        if (string.IsNullOrWhiteSpace(model.Password)) ModelState.AddModelError(nameof(model.Password), "Password is required for new user.");
        else if (!IsStrongPassword(model.Password)) ModelState.AddModelError(nameof(model.Password), "Password must be minimum 6 characters and contain at least one letter and one number.");
        if (string.IsNullOrWhiteSpace(model.Role)) ModelState.AddModelError(nameof(model.Role), "Role is required.");
        // Email, SMTP and Notes are optional for all roles. Do not add required validation for these fields.
        NormalizeLoginModel(model);
        // Non-partner login fields are intentionally managed in Partner Master only.
        // Clear their ModelState entries so a hidden/empty field never blocks Support/User/Admin save.
        ClearNonLoginModelState();
        if (!ModelState.IsValid)
        {
            await LoadRolesAsync();
            await LoadPartnerMasterAsync();
            return View("Edit", model);
        }
        try
        {
            await _userService.SaveAsync(MapToModel(model), model.Password);
            TempData["Success"] = $"{model.Role} created successfully.";
            return RedirectToAction(nameof(Users));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadRolesAsync();
            await LoadPartnerMasterAsync();
            return View("Edit", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.manage")) return RedirectToAction("AccessDenied", "Account");
        var user = await _userService.GetByIdAsync(id);
        if (user == null) return NotFound();
        await LoadRolesAsync();
        await LoadPartnerMasterAsync();
        // Older accounts saved before plain-text passwords were shown may still hold a bcrypt
        // hash (e.g. "$2a$11$..."); never show that in the field — leave it blank instead so
        // Admin can set a fresh password, which will then display normally from now on.
        var displayPassword = IsBcryptHash(user.PasswordHash) ? string.Empty : user.PasswordHash;
        return View(new UserFormViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Username = user.Username,
            Password = displayPassword,
            Role = user.Role,
            PartnerCode = user.PartnerCode,
            Mobile = user.Mobile,
            Email = user.Email,
            SmtpHost = user.SmtpHost,
            SmtpPort = user.SmtpPort <= 0 ? 587 : user.SmtpPort,
            SmtpUsername = user.SmtpUsername,
            SmtpPassword = string.Empty,
            SmtpEnableSsl = user.SmtpEnableSsl,
            City = user.City,
            IsActive = user.IsActive,
            TargetAmount = user.TargetAmount,
            MarginPercent = user.MarginPercent,
            TargetFromDate = user.TargetFromDate,
            TargetToDate = user.TargetToDate,
            Notes = user.Notes
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "users.manage")) return RedirectToAction("AccessDenied", "Account");
        if (string.IsNullOrWhiteSpace(model.Username)) ModelState.AddModelError(nameof(model.Username), "Username is required.");
        if (string.IsNullOrWhiteSpace(model.Role)) ModelState.AddModelError(nameof(model.Role), "Role is required.");
        NormalizeLoginModel(model);
        // Clear Partner-only hidden field validation for non-partner login updates.
        ClearNonLoginModelState();
        if (!string.IsNullOrWhiteSpace(model.Password) && !IsStrongPassword(model.Password)) ModelState.AddModelError(nameof(model.Password), "Password must be minimum 6 characters and contain at least one letter and one number.");
        // Email optional for all roles. Do not add required validation here.
        if (!ModelState.IsValid)
        {
            await LoadRolesAsync();
            await LoadPartnerMasterAsync();
            return View(model);
        }
        try
        {
            await _userService.SaveAsync(MapToModel(model), model.Password);
            TempData["Success"] = $"{model.Role} updated successfully.";
            return RedirectToAction(nameof(Users));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadRolesAsync();
            await LoadPartnerMasterAsync();
            return View(model);
        }
    }

    private void ClearNonLoginModelState()
    {
        foreach (var key in new[] { "PartnerCode", "City", "TargetAmount", "MarginPercent", "TargetFromDate", "TargetToDate" })
        {
            ModelState.Remove(key);
        }
    }

    private static string NormalizeRole(string? role)
    {
        var value = (role ?? "User").Trim();
        if (value.Equals("Support Head", StringComparison.OrdinalIgnoreCase) || value.Equals("SupportHead", StringComparison.OrdinalIgnoreCase)) return "SupportHead";
        if (value.Equals("Support Team", StringComparison.OrdinalIgnoreCase) || value.Equals("SupportUser", StringComparison.OrdinalIgnoreCase)) return "Support";
        if (value.Equals("Partner", StringComparison.OrdinalIgnoreCase)) return "Partner";
        if (value.Equals("Admin", StringComparison.OrdinalIgnoreCase)) return "Admin";
        return value.Equals("User", StringComparison.OrdinalIgnoreCase) ? "User" : value;
    }

    private static void NormalizeLoginModel(UserFormViewModel model)
    {
        model.Role = NormalizeRole(model.Role);
        model.FullName = (model.FullName ?? string.Empty).Trim();
        model.Username = (model.Username ?? string.Empty).Trim();
        model.Notes = model.Notes ?? string.Empty ?? string.Empty;
        if (!model.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase))
        {
            model.PartnerCode = string.Empty;
            model.TargetAmount = 0;
            model.MarginPercent = 0;
            model.TargetFromDate = null;
            model.TargetToDate = null;
        }
    }

    private static bool IsStrongPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6) return false;
        return password.Any(char.IsLetter) && password.Any(char.IsDigit);
    }

    private static bool IsBcryptHash(string? value) =>
        !string.IsNullOrEmpty(value) && (value.StartsWith("$2a$") || value.StartsWith("$2b$") || value.StartsWith("$2y$"));

    private async Task LoadRolesAsync()
    {
        var roles = (await _roleService.GetAllAsync())
            .Where(x => x.IsActive)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        foreach (var requiredRole in new[] { "Admin", "User", "Support", "SupportHead", "Partner" })
        {
            if (!roles.Any(x => x.Equals(requiredRole, StringComparison.OrdinalIgnoreCase)))
            {
                roles.Add(requiredRole);
            }
        }

        ViewBag.RoleOptions = roles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }

    private async Task LoadPartnerMasterAsync()
    {
        ViewBag.PartnerMasters = await _dealerService.GetActiveAsync();
    }

    private static AppUser MapToModel(UserFormViewModel model) => new()
    {
        Id = model.Id ?? string.Empty,
        FullName = model.FullName,
        Username = model.Username,
        Role = model.Role,
        PartnerCode = model.PartnerCode,
        Mobile = model.Mobile,
        Email = model.Email ?? string.Empty,
        SmtpHost = model.SmtpHost ?? string.Empty,
        SmtpPort = model.SmtpPort <= 0 ? 587 : model.SmtpPort,
        SmtpUsername = model.SmtpUsername ?? string.Empty,
        SmtpPassword = model.SmtpPassword ?? string.Empty,
        SmtpEnableSsl = model.SmtpEnableSsl,
        City = model.City,
        IsActive = model.IsActive,
        TargetAmount = 0,
        MarginPercent = 0,
        TargetFromDate = model.TargetFromDate,
        TargetToDate = model.TargetToDate,
        Notes = model.Notes ?? string.Empty
    };
}
