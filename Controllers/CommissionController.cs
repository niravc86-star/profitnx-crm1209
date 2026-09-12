using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class CommissionController : Controller
{
    private readonly ICommissionService _commissionService;
    private readonly IUserService _userService;
    private readonly IPermissionService _permissionService;

    public CommissionController(ICommissionService commissionService, IUserService userService, IPermissionService permissionService)
    {
        _commissionService = commissionService;
        _userService = userService;
        _permissionService = permissionService;
    }

    public async Task<IActionResult> Index(CommissionFilterViewModel filter)
    {
        if (!await _permissionService.HasPermissionAsync(User, "commission.view")) return RedirectToAction("AccessDenied", "Account");
        ViewBag.Partners = await _userService.GetPartnersAsync();
        ViewBag.Users = (await _userService.GetActiveUsersAsync()).Where(x => x.Role == "User").ToList();
        ViewBag.Filter = filter;
        return View(await _commissionService.GetReportAsync(filter));
    }
}
