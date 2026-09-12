using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;
using System.Security.Claims;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly IUserService _userService;

    public NotificationsController(INotificationService notificationService, IPermissionService permissionService, IUserService userService)
    {
        _notificationService = notificationService;
        _permissionService = permissionService;
        _userService = userService;
    }

    public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate, string? fromTime, string? toTime, string? channel, string? recipient, string? status, string? search)
    {
        if (!await _permissionService.HasPermissionAsync(User, "notifications.view"))
            return RedirectToAction("AccessDenied", "Account");

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var loginName = User.Identity?.Name ?? string.Empty;
        var canManageAllNotifications = await _permissionService.HasPermissionAsync(User, "notifications.send");
        var canDeleteNotifications = await _permissionService.HasPermissionAsync(User, "notifications.delete");
        var logs = await _notificationService.GetForUserAsync(userId, loginName, role, canManageAllNotifications);

        // "User Login Alert" entries only exist to pop up a live heads-up for Admin
        // (see PendingList/UnreadCount) - the actual permanent record already lives
        // in the Login History Report, so they don't need to also sit in this
        // Notification Logs table/history.
        logs = logs.Where(x => !x.FlowType.Equals("UserLogin", StringComparison.OrdinalIgnoreCase)
                                && !x.FlowType.Equals("UserLogout", StringComparison.OrdinalIgnoreCase)).ToList();

        // Filter values must also respect the current user's notification scope.
        ViewBag.Channels = logs.Select(x => x.Channel).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        ViewBag.Recipients = logs.Select(x => x.Recipient).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Take(200).ToList();
        ViewBag.Statuses = logs.Select(x => x.Status).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        ViewBag.RoleName = role;
        ViewBag.CanManageAllNotifications = canManageAllNotifications;
        ViewBag.CanDeleteNotifications = canDeleteNotifications;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;
        ViewBag.FromTime = fromTime;
        ViewBag.ToTime = toTime;
        ViewBag.Channel = channel;
        ViewBag.Recipient = recipient;
        ViewBag.Status = status;
        ViewBag.Search = search;

        logs = ApplyFilters(logs, fromDate, toDate, fromTime, toTime, channel, recipient, status, search);

        // Opening My Notifications acknowledges only the CRM rows visible to this user.
        await _notificationService.MarkAsReadAsync(logs
            .Where(x => x.Channel.Equals("CRM", StringComparison.OrdinalIgnoreCase)
                        && !x.Status.Equals("Read", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id), userId, loginName, role);

        return View(logs);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UnreadCount()
    {
        if (!await _permissionService.HasPermissionAsync(User, "notifications.view"))
            return Forbid();

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var loginName = User.Identity?.Name ?? string.Empty;
        var count = await _notificationService.GetUnreadCountForUserAsync(userId, loginName, role);
        return Json(new { count, hasUnread = count > 0 });
    }

    // Change 1 (2026-08-05): pending-task flash message.
    // Returns the current user's still-unread CRM notifications (their
    // pending/assigned tasks), scoped exactly like UnreadCount/Index by the
    // same recipient matching + "notifications.view" permission check, so a
    // user only ever sees flash messages for what their rights already let
    // them see. The layout polls this to show one combined flash message for
    // every pending task instead of a popup per task, and to detect brand
    // new assignments (see wwwroot/js/crm-ui.js -> initPendingTaskFlash).
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PendingList()
    {
        if (!await _permissionService.HasPermissionAsync(User, "notifications.view"))
            return Forbid();

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var loginName = User.Identity?.Name ?? string.Empty;
        var logs = await _notificationService.GetForUserAsync(userId, loginName, role);

        // "User Login Alert" rows are never marked Read through the Notifications
        // page anymore (they were removed from that listing), so relying on
        // Status=="Read" here would make a login alert pop up forever. Instead,
        // treat it as a live, one-time heads-up: only surface it for a short
        // window after the login, then let it drop off on its own.
        var loginAlertWindow = DateTime.Now.AddMinutes(-20);

        var pending = logs
            .Where(x => x.Channel.Equals("CRM", StringComparison.OrdinalIgnoreCase)
                        && !x.Status.Equals("Read", StringComparison.OrdinalIgnoreCase)
                        && ((!x.FlowType.Equals("UserLogin", StringComparison.OrdinalIgnoreCase)
                             && !x.FlowType.Equals("UserLogout", StringComparison.OrdinalIgnoreCase))
                            || x.SentDate >= loginAlertWindow))
            .OrderByDescending(x => x.SentDate)
            .Take(20)
            .Select(x => new
            {
                id = x.Id,
                message = string.IsNullOrWhiteSpace(x.Message) ? "You have a pending task." : x.Message,
                senderName = string.IsNullOrWhiteSpace(x.SenderName) ? "CRM" : x.SenderName,
                flowType = x.FlowType,
                actionUrl = string.IsNullOrWhiteSpace(x.ActionUrl) ? Url.Action("Index", "Notifications") : x.ActionUrl,
                sentDate = x.SentDate.ToString("dd MMM, hh:mm tt")
            })
            .ToList();

        return Json(new { items = pending, count = pending.Count, viewAllUrl = Url.Action("Index", "Notifications") });
    }

    private static List<NotificationLog> ApplyFilters(List<NotificationLog> logs, DateTime? fromDate, DateTime? toDate, string? fromTime, string? toTime, string? channel, string? recipient, string? status, string? search)
    {
        IEnumerable<NotificationLog> query = logs;
        if (fromDate.HasValue) query = query.Where(x => x.SentDate.Date >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(x => x.SentDate.Date <= toDate.Value.Date);
        if (TimeSpan.TryParse(fromTime, out var fromTimeSpan)) query = query.Where(x => x.SentDate.TimeOfDay >= fromTimeSpan);
        if (TimeSpan.TryParse(toTime, out var toTimeSpan)) query = query.Where(x => x.SentDate.TimeOfDay <= toTimeSpan);
        if (!string.IsNullOrWhiteSpace(channel)) query = query.Where(x => x.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(recipient)) query = query.Where(x => x.Recipient.Contains(recipient, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchValue = search.Trim();
            query = query.Where(x => x.Message.Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                                     || x.Recipient.Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                                     || x.Channel.Contains(searchValue, StringComparison.OrdinalIgnoreCase));
        }
        return query.OrderByDescending(x => x.SentDate).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(User, "notifications.send"))
            return RedirectToAction("AccessDenied", "Account");
        return View(new NotificationFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NotificationFormViewModel model)
    {
        if (!await _permissionService.HasPermissionAsync(User, "notifications.send"))
            return RedirectToAction("AccessDenied", "Account");
        await _notificationService.QueueAsync(model);
        TempData["Success"] = "Notification queued successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(string[] ids)
    {
        if (!await _permissionService.HasPermissionAsync(User, "notifications.delete"))
            return RedirectToAction("AccessDenied", "Account");

        var requestedIds = (ids ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requestedIds.Any())
        {
            TempData["Error"] = "Please select at least one notification.";
            return RedirectToAction(nameof(Index));
        }

        var canManageAllNotifications = await _permissionService.HasPermissionAsync(User, "notifications.send");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var loginName = User.Identity?.Name ?? string.Empty;
        var logs = await _notificationService.GetForUserAsync(userId, loginName, role, canManageAllNotifications);

        var allowedIds = logs.Where(x => requestedIds.Contains(x.Id)).Select(x => x.Id).ToArray();
        if (!allowedIds.Any())
            return RedirectToAction("AccessDenied", "Account");

        await _notificationService.DeleteAsync(allowedIds);
        TempData["Success"] = $"{allowedIds.Length} selected notification record(s) deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearAll()
    {
        var canDelete = await _permissionService.HasPermissionAsync(User, "notifications.delete");
        var canManageAll = await _permissionService.HasPermissionAsync(User, "notifications.send");
        if (!canDelete || !canManageAll)
            return RedirectToAction("AccessDenied", "Account");

        await _notificationService.ClearAllAsync();
        TempData["Success"] = "All old notification history deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<NotificationLog>> FilterLogsForCurrentUserAsync(List<NotificationLog> logs, string role)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var loginName = User.Identity?.Name ?? string.Empty;
        var user = (await _userService.GetAllUsersAsync()).FirstOrDefault(x =>
            x.Id.Equals(userId, StringComparison.OrdinalIgnoreCase)
            || x.Username.Equals(loginName, StringComparison.OrdinalIgnoreCase)
            || x.FullName.Equals(loginName, StringComparison.OrdinalIgnoreCase));

        var recipientKeys = new List<string?>
        {
            user?.FullName,
            user?.Username,
            user?.Mobile,
            user?.Email,
            loginName,
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : null,
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin Team" : null,
            role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase) ? "Support Head" : null
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => NormalizeRecipient(x!))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return logs.Where(x => recipientKeys.Contains(NormalizeRecipient(x.Recipient))).ToList();
    }

    private static string NormalizeRecipient(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
