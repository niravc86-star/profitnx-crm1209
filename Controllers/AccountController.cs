using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;
using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Controllers;

public class AccountController : Controller
{
    private readonly IUserService _userService;
    private readonly IDealerService _dealerService;
    private readonly IInquiryService _inquiryService;
    private readonly IPermissionService _permissionService;
    private readonly ILoginSessionService _loginSessions;
    private readonly INotificationService _notifications;
    private readonly IImplementationService _implementationService;

    public AccountController(IUserService userService, IRoleService roleService, IDealerService dealerService, IInquiryService inquiryService, IPermissionService permissionService, ILoginSessionService loginSessions, INotificationService notifications, IImplementationService implementationService)
    {
        _userService = userService;
        _dealerService = dealerService;
        _inquiryService = inquiryService;
        _permissionService = permissionService;
        _loginSessions = loginSessions;
        _notifications = notifications;
        _implementationService = implementationService;
        // roleService is kept in constructor so existing dependency injection remains compatible.
        // Landing page from Roles sheet is intentionally not used for redirect, because Notes/Description
        // like "Full access" must never become a controller URL.
    }

    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    // Used only by the CRM lock-screen overlay (Change 5) to verify the
    // *currently signed-in* user's own password before removing the overlay.
    // It never changes the authentication cookie/session — the user stays
    // logged in throughout; this only confirms it is really them at the keyboard.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyUnlockPassword(string password)
    {
        var username = User.FindFirst("Username")?.Value ?? User.Identity?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Json(new { ok = false });

        try
        {
            var user = await _userService.ValidateUserAsync(username, password);
            return Json(new { ok = user != null });
        }
        catch
        {
            return Json(new { ok = false });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        ProfitNx.CRM.Models.AppUser? user = null;
        try
        {
            user = await _userService.ValidateUserAsync(model.Username, model.Password);
        }
        catch (InvalidOperationException invEx)
        {
            // Known initialization/read errors from GoogleSheetsService surface as InvalidOperationException with guidance.
            ViewBag.Error = "Configuration error: " + invEx.Message;
            return View(model);
        }
        catch (Exception ex)
        {
            // Generic catch to avoid unhandled exceptions during login caused by Google API issues.
            ViewBag.Error = "Unable to contact Google Sheets service. Please check GoogleSheets:CredentialsFile, share the spreadsheet with the service account email, and ensure server time is correct.\nDetails: " + ex.Message;
            return View(model);
        }

        if (user == null)
        {
            ViewBag.Error = "Invalid username or password";
            return View(model);
        }

        var dealer = await ResolveDealerAsync(user);
        var marginPercent = dealer?.MarginPercent ?? 0m;
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
            new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.FullName) ? user.Username ?? string.Empty : user.FullName),
            new Claim(ClaimTypes.Role, user.Role ?? string.Empty),
            new Claim("Username", user.Username ?? string.Empty),
            new Claim("PartnerCode", user.PartnerCode ?? string.Empty),
            new Claim("PartnerName", dealer?.DealerName ?? user.FullName ?? string.Empty),
            new Claim("PartnerCity", dealer?.City ?? string.Empty),
            new Claim("MarginPercent", marginPercent.ToString()),
            new Claim("ThemePreference", string.IsNullOrWhiteSpace(user.ThemePreference) ? "classic" : user.ThemePreference)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();
            var sessionId = Guid.NewGuid().ToString("N");

            // Only page Admin when this is a genuine fresh login. If the user
            // already has a *currently live* session (a heartbeat pulse seen in
            // the last 15 minutes - e.g. an extra open tab, a silent token
            // refresh) we still record the login for the history report, but
            // skip the "User Login Alert" so Admin isn't paged repeatedly for
            // someone who never actually left. Directly closing the app/browser
            // WITHOUT clicking Logout still counts as "gone" once that activity
            // goes stale - so their next real login is treated as fresh and DOES
            // alert Admin, same as if they had logged out first.
            var alreadyActive = await _loginSessions.HasActiveSessionAsync(user.Id ?? "");
            await _loginSessions.RecordLoginAsync(user, ip, ua, sessionId);
            if (!alreadyActive)
            {
                var device = LoginSessionService.SummarizeDevice(ua);
                await _notifications.NotifyUserLoginAsync(user.Id ?? "", string.IsNullOrWhiteSpace(user.FullName) ? user.Username ?? "" : user.FullName, user.Username ?? "", user.Role ?? "", device, ip);
            }
        }
        catch { /* never block login */ }

        // Non-httponly cookie so the (unauthenticated) login screen can also apply this
        // person's last-known theme choice on this browser, before they sign in again.
        Response.Cookies.Append("pnx_theme", string.IsNullOrWhiteSpace(user.ThemePreference) ? "classic" : user.ThemePreference,
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = false, SameSite = SameSiteMode.Lax });

        if (await _permissionService.HasPermissionAsync(principal, "inquiry.view"))
            await SetFollowUpFlashAsync(user.Role, user.Id ?? string.Empty);

        return await RedirectToFirstAllowedAsync(principal);
    }

    // Allowed theme keys — kept in sync with app-theme-switcher.js THEMES array.
    private static readonly HashSet<string> AllowedThemes = new(StringComparer.OrdinalIgnoreCase)
        { "classic", "dompet", "midnight", "emerald", "rosegold", "premium", "saas" };

    // Called from the theme switcher (topbar) whenever the signed-in user picks a theme
    // with "Set as my default theme" checked. Saves it against their own account so it
    // is restored automatically next time they log in, on any device — and refreshes
    // the auth cookie + the pre-login cookie so it takes effect immediately everywhere.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTheme(string theme)
    {
        var key = AllowedThemes.Contains(theme ?? string.Empty) ? theme!.ToLowerInvariant() : "classic";
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return Json(new { ok = false });

        await _userService.SetThemePreferenceAsync(userId, key);

        var identity = new ClaimsIdentity(User.Claims.Where(c => c.Type != "ThemePreference"), CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim("ThemePreference", key));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        Response.Cookies.Append("pnx_theme", key,
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = false, SameSite = SameSiteMode.Lax });

        return Json(new { ok = true, theme = key });
    }

    private async Task SetFollowUpFlashAsync(string? role, string userId)
    {
        // Support Head: do NOT show inquiry follow-up reminders (even for Support Team inquiries).
        // Instead show Implementation / Training attention items only.
        if (role != null && role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase))
        {
            await SetImplementationFlashForSupportHeadAsync(userId);
            return;
        }

        var inquiries = await _inquiryService.SearchAsync(new InquiryFilterViewModel(), role, userId);
        var openFollowUps = inquiries.Where(x => x.NextFollowUpDate.HasValue && !x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)).ToList();
        var today = DateTime.Today;
        var todayCount = openFollowUps.Count(x => x.NextFollowUpDate!.Value.Date == today);
        var overdueCount = openFollowUps.Count(x => x.NextFollowUpDate!.Value.Date < today);
        var tomorrowCount = openFollowUps.Count(x => x.NextFollowUpDate!.Value.Date == today.AddDays(1));
        var weekCount = openFollowUps.Count(x => x.NextFollowUpDate!.Value.Date >= today && x.NextFollowUpDate!.Value.Date <= today.AddDays(7));
        if (todayCount > 0 || overdueCount > 0)
        {
            TempData["FollowUpFlash"] = $"Today follow-up: {todayCount} | Overdue: {overdueCount} | Tomorrow: {tomorrowCount} | Next 7 days: {weekCount}";
        }

        // Support users also get a compact Implementation/Training flash for their own sessions.
        if (role != null && role.Equals("Support", StringComparison.OrdinalIgnoreCase))
            await SetImplementationFlashForSupportUserAsync(userId);
    }

    /// <summary>
    /// Support Head reminders: pending assignment + upcoming/overdue training across the team.
    /// </summary>
    private async Task SetImplementationFlashForSupportHeadAsync(string userId)
    {
        try
        {
            var cases = await _implementationService.GetVisibleCasesAsync("SupportHead", userId);
            var awaiting = cases.Count(x => string.IsNullOrWhiteSpace(x.AssignedMemberId) && !x.IsTrainingCompleted);
            var now = DateTime.Now;
            var upcoming = await _implementationService.GetUpcomingForUserAsync("SupportHead", userId, now.AddDays(-1), now.AddDays(7));
            var overdueSched = 0;
            var todaySched = 0;
            var soonSched = 0;
            foreach (var s in upcoming)
            {
                if (!TryGetScheduleWhen(s, out var when)) continue;
                if (when < now && !s.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) overdueSched++;
                else if (when.Date == DateTime.Today) todaySched++;
                else if (when <= now.AddDays(7)) soonSched++;
            }
            if (awaiting > 0 || overdueSched > 0 || todaySched > 0 || soonSched > 0)
            {
                TempData["ImplementationFlash"] =
                    $"Training / Implementation: Awaiting assignment: {awaiting} | Overdue sessions: {overdueSched} | Today: {todaySched} | Next 7 days: {soonSched}";
            }
        }
        catch
        {
            // Never block login on reminder failure
        }
    }

    private async Task SetImplementationFlashForSupportUserAsync(string userId)
    {
        try
        {
            var now = DateTime.Now;
            var upcoming = await _implementationService.GetUpcomingForUserAsync("Support", userId, now.AddDays(-1), now.AddDays(7));
            var overdueSched = 0;
            var todaySched = 0;
            var soonSched = 0;
            foreach (var s in upcoming)
            {
                if (!TryGetScheduleWhen(s, out var when)) continue;
                if (when < now && !s.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) overdueSched++;
                else if (when.Date == DateTime.Today) todaySched++;
                else if (when <= now.AddDays(7)) soonSched++;
            }
            if (overdueSched > 0 || todaySched > 0 || soonSched > 0)
            {
                TempData["ImplementationFlash"] =
                    $"My Training: Overdue: {overdueSched} | Today: {todaySched} | Next 7 days: {soonSched}";
            }
        }
        catch { }
    }

    private static bool TryGetScheduleWhen(ImplementationSchedule s, out DateTime when)
    {
        when = default;
        if (s == null) return false;
        var time = string.IsNullOrWhiteSpace(s.StartTime) ? "10:00" : s.StartTime.Trim();
        if (DateTime.TryParse($"{s.ScheduleDate:yyyy-MM-dd} {time}", out var parsed))
        {
            when = parsed;
            return true;
        }
        when = s.ScheduleDate.Date.AddHours(10);
        return true;
    }

    private async Task<ProfitNx.CRM.Models.Dealer?> ResolveDealerAsync(ProfitNx.CRM.Models.AppUser user)
    {
        if (!string.Equals(user.Role, "Partner", StringComparison.OrdinalIgnoreCase)) return null;
        var dealers = await _dealerService.GetAllAsync();
        return dealers.FirstOrDefault(d => d.Id.Equals(user.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.DealerName.Equals(user.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.ContactPerson.Equals(user.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.Email.Equals(user.Email ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.Mobile.Equals(user.Mobile ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IActionResult> RedirectToFirstAllowedAsync(ClaimsPrincipal principal)
    {
        var routes = new (string Permission, string Controller, string Action)[]
        {
            ("dashboard.view", "Dashboard", "Index"),
            ("inquiry.view", "Inquiry", "Index"),
            ("reports.view", "Reports", "Index"),
            ("product.view", "Product", "Index"),
            ("stock.view", "Stock", "Index"),
            ("dealer.view", "Dealer", "Index"),
            ("commission.view", "Commission", "Index"),
            ("scheme.view", "Scheme", "Index"),
            ("notifications.view", "Notifications", "Index"),
            ("users.view", "Admin", "Users"),
            ("roles.view", "Role", "Index"),
            ("settings.view", "Settings", "Index")
        };

        foreach (var route in routes)
        {
            if (await _permissionService.HasPermissionAsync(principal, route.Permission))
                return RedirectToAction(route.Action, route.Controller);
        }

        return StatusCode(StatusCodes.Status403Forbidden,
            "No CRM menu rights are assigned to this login. Ask an administrator to assign Role Wise or Separate User Rights.");
    }

    public async Task<IActionResult> Logout()
    {
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
            await _loginSessions.RecordLogoutAsync(userId);

            // ADDED (2026-08-17): "koi pan user login/logout kare tyare aave" -
            // Admin used to only be paged on login, never on logout. Mirrors the
            // login-side alert exactly (same admin scoping/device summary), just
            // for the sign-out event.
            var fullName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "";
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            var userName = User.FindFirst("Username")?.Value ?? fullName;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrWhiteSpace(userId))
                await _notifications.NotifyUserLogoutAsync(userId, fullName, userName, role, LoginSessionService.SummarizeDevice(ua), ip);
        }
        catch { }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Heartbeat()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        var name = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "";
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var username = User.FindFirst("Username")?.Value ?? name;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();
        await _loginSessions.HeartbeatAsync(userId, name, username, role, ip, ua);
        var live = await _loginSessions.GetLiveCountAsync();
        return Json(new { ok = true, live });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> LiveUsers()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            && !await _permissionService.HasPermissionAsync(User, "loginreport.view"))
            return Unauthorized();
        var users = await _loginSessions.GetLiveUsersAsync();
        return Json(users.Select(x => new {
            x.UserId, x.FullName, x.UserName, x.Role,
            loginAt = x.LoginAt.ToString("dd/MM/yyyy hh:mm tt"),
            lastSeen = x.LastSeenAt.ToString("dd/MM/yyyy hh:mm tt"),
            x.DeviceSummary, x.IpAddress
        }));
    }

    public async Task<IActionResult> AccessDenied()
    {
        TempData["Error"] = "You do not have permission to access this page.";
        return await RedirectToFirstAllowedAsync(User);
    }
}
