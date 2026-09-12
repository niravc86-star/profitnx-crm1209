using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using System.Security.Claims;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboardService;
    private readonly IInquiryService _inquiryService;
    private readonly IPermissionService _permissionService;
    private readonly IYearlyTargetService _yearlyTargetService;

    public DashboardController(
        IDashboardService dashboardService,
        IInquiryService inquiryService,
        IPermissionService permissionService,
        IYearlyTargetService yearlyTargetService)
    {
        _dashboardService = dashboardService;
        _inquiryService = inquiryService;
        _permissionService = permissionService;
        _yearlyTargetService = yearlyTargetService;
    }

    public async Task<IActionResult> Index(DateTime? followUpFromDate, DateTime? followUpToDate)
    {
        if (!await _permissionService.HasPermissionAsync(User, "dashboard.view"))
            return RedirectToAction("AccessDenied", "Account");

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var summary = await _dashboardService.GetSummaryAsync(role, userId);

        var visibleInquiries = await GetVisibleInquiriesAsync(role, userId);
        ViewBag.RecentInquiries = visibleInquiries;
        ViewBag.FollowUpFromDate = followUpFromDate;
        ViewBag.FollowUpToDate = followUpToDate;
        ViewBag.TodayFollowUps = visibleInquiries.Where(x => x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date == DateTime.Today && x.Status != "Sold" && x.Status != "Close").ToList();
        ViewBag.RangeFollowUps = visibleInquiries.Where(x => x.NextFollowUpDate.HasValue && (!followUpFromDate.HasValue || x.NextFollowUpDate.Value.Date >= followUpFromDate.Value.Date) && (!followUpToDate.HasValue || x.NextFollowUpDate.Value.Date <= followUpToDate.Value.Date) && x.Status != "Sold" && x.Status != "Close").OrderBy(x => x.NextFollowUpDate).ToList();
        ViewBag.RoleName = role;
        return View(summary);
    }

    [HttpGet]
    public async Task<IActionResult> Live(DateTime? fromDate, DateTime? toDate, string? period)
    {
        // dashboard.live is an independent right from dashboard.view - do not fall
        // back to dashboard.view here, or Support/SupportHead (who have Dashboard
        // View but not Live Dashboard) can still open this page directly by URL.
        if (!await _permissionService.HasPermissionAsync(User, "dashboard.live"))
            return RedirectToAction("AccessDenied", "Account");

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var userName = User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name
            ?? "User";

        var today = DateTime.Today;
        // Prefer explicit period (dropdown) over sticky date inputs so "This Week" etc. is not overwritten by old from/to fields.
        var periodKey = (period ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(periodKey) && periodKey is "today" or "week" or "lastmonth" or "month")
        {
            if (periodKey == "today")
            {
                fromDate = today;
                toDate = today;
            }
            else if (periodKey == "week")
            {
                var diff = (int)today.DayOfWeek;
                fromDate = today.AddDays(-diff);
                toDate = today;
            }
            else if (periodKey == "lastmonth")
            {
                var firstThis = new DateTime(today.Year, today.Month, 1);
                fromDate = firstThis.AddMonths(-1);
                toDate = firstThis.AddDays(-1);
            }
            else // month
            {
                fromDate = new DateTime(today.Year, today.Month, 1);
                toDate = today;
            }
        }
        else if (!fromDate.HasValue || !toDate.HasValue)
        {
            fromDate ??= new DateTime(today.Year, today.Month, 1);
            toDate ??= today;
        }

        var model = await _dashboardService.GetLiveDashboardAsync(role, userId, userName, fromDate, toDate);
        ViewBag.RoleName = role;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> LiveData(DateTime? fromDate, DateTime? toDate)
    {
        // Same independent check as Live() above - see comment there.
        if (!await _permissionService.HasPermissionAsync(User, "dashboard.live"))
            return Unauthorized();

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "User";
        var model = await _dashboardService.GetLiveDashboardAsync(role, userId, userName, fromDate, toDate);
        return Json(model);
    }

    // Yearly Target Planner — the target is Admin's to set (company-wide);
    // "Achieved so far" and pace are always computed automatically from
    // CRM sold data, so there is nothing else to save here.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetYearlyTarget(decimal amount, int? year)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        // Financial year: 01/04 to 31/03 — same convention as the "Year Wise Partner
        // Target" grid and the Live Dashboard's Yearly Target Planner.
        var todayForFy = DateTime.Today;
        var fallbackFyYear = todayForFy.Month >= 4 ? todayForFy.Year : todayForFy.Year - 1;
        var targetYear = year ?? fallbackFyYear;
        await _yearlyTargetService.SetTargetAsync(targetYear, amount);
        return Json(new { success = true });
    }

    private async Task<List<Inquiry>> GetVisibleInquiriesAsync(string role, string userId)
        => await _inquiryService.GetVisibleForRoleAsync(role, userId);
}
