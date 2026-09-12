using Microsoft.Extensions.Caching.Memory;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;
using System.Security.Claims;

namespace ProfitNx.CRM.Services;

public class PermissionService : IPermissionService
{
    private readonly IGoogleSheetsService sheetsService;
    private readonly IRoleService roleService;
    private readonly IUserService userService;
    private readonly IMemoryCache memoryCache;

    private const string RoleReadRange = "RolePermissions!A2:F";
    private const string RoleClearRange = "RolePermissions!A2:F5000";
    private const string RoleWriteRange = "RolePermissions!A2";
    private const string UserReadRange = "UserPermissions!A2:G";
    private const string UserClearRange = "UserPermissions!A2:G200000";
    private const string UserWriteRange = "UserPermissions!A2";
    private const string RoleCacheKey = "role_permissions_cache_v4";
    private const string UserCacheKey = "user_permissions_cache_v4";

    private static readonly HashSet<string> CommonExperiencePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "shortcuts.view", "help.view", "notifications.view", "lock.screen.use",
        "inquiry.attention.alert.view", "inquiry.achievement.view",
        "field.reports.filter", "field.reports.export",
        "chat.access"
    };

    private static readonly HashSet<string> InquiryWorkingPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "inquiry.view", "inquiry.edit", "inquiry.status", "inquiry.report.view",
        "inquiry.followup.reminder.view", "inquiry.smartfilters.view",
        "inquiry.conversion.score.view", "inquiry.nextbestaction.view",
        "inquiry.followup.manage", "inquiry.quickdate.manage",
        "inquiry.quickstage.manage", "inquiry.timeline.view",
        "inquiry.contact.call", "inquiry.contact.whatsapp", "inquiry.contact.email",
        "inquiry.summary.copy",
        "inquiry.card.total", "inquiry.card.new", "inquiry.card.genuine", "inquiry.card.notgenuine",
        "inquiry.card.followup", "inquiry.card.demo.scheduled", "inquiry.card.demo.done", "inquiry.card.sold", "inquiry.card.closed"
    };

    public PermissionService(IGoogleSheetsService googleSheetsService, IRoleService roleService, IUserService userService, IMemoryCache cache)
    {
        sheetsService = googleSheetsService;
        this.roleService = roleService;
        this.userService = userService;
        memoryCache = cache;
    }

    // Expose a cache clear helper for admin/tools to refresh permission data immediately.
    public void ClearCache()
    {
        memoryCache.Remove(RoleCacheKey);
        memoryCache.Remove(UserCacheKey);
    }

    private async Task<IList<IList<object>>> ReadRoleRowsAsync()
    {
        var rows = await memoryCache.GetOrCreateAsync(RoleCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            entry.SlidingExpiration = TimeSpan.FromMinutes(2);
            return await sheetsService.ReadAsync(RoleReadRange);
        });
        return rows ?? new List<IList<object>>();
    }

    private async Task<IList<IList<object>>> ReadUserRowsAsync()
    {
        var rows = await memoryCache.GetOrCreateAsync(UserCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            entry.SlidingExpiration = TimeSpan.FromMinutes(2);
            return await sheetsService.ReadAsync(UserReadRange);
        });
        return rows ?? new List<IList<object>>();
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionKey)
    {
        var username = principal.FindFirst("Username")?.Value ?? principal.Identity?.Name ?? string.Empty;
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(permissionKey)) return false;
        // Saved user and role rights always take precedence. DefaultAllowed is used only
        // when no saved permission row exists, preserving backwards-compatible defaults.

        var userRows = await ReadUserRowsAsync();
        var userMatches = userRows.Where(row =>
            SheetValueHelper.GetString(row, 0).Equals(userId, StringComparison.OrdinalIgnoreCase) ||
            SheetValueHelper.GetString(row, 1).Equals(username, StringComparison.OrdinalIgnoreCase)).ToList();
        var isSeparate = userMatches.Any(row =>
            SheetValueHelper.GetString(row, 2).Equals("__mode__", StringComparison.OrdinalIgnoreCase) &&
            SheetValueHelper.GetBool(row, 4, false));

        if (isSeparate)
        {
            var explicitRow = userMatches.FirstOrDefault(row =>
                SheetValueHelper.GetString(row, 2).Equals(permissionKey, StringComparison.OrdinalIgnoreCase));
            // Only trust "Separate Rights" when a row was actually saved for this exact
            // permission key. A brand-new permission (e.g. chat.access added later) will
            // have no saved row yet for existing separate-rights users - fall through to
            // the normal role/default logic below instead of silently denying it.
            if (explicitRow != null)
                return SheetValueHelper.GetBool(explicitRow, 4, false);
        }

        var roleRows = await ReadRoleRowsAsync();
        foreach (var row in roleRows)
        {
            var rowRole = SheetValueHelper.GetString(row, 0);
            var rowPermission = SheetValueHelper.GetString(row, 2);
            if (!rowRole.Equals(role, StringComparison.OrdinalIgnoreCase) || !rowPermission.Equals(permissionKey, StringComparison.OrdinalIgnoreCase))
                continue;

            // Column F marks a role permission as an intentional selection made
            // from Role Wise Rights. Old A:E rows are legacy defaults and must
            // not keep Edit/Delete/Admin modules locked to Admin forever.
            var isExplicitRoleOverride = SheetValueHelper.GetBool(row, 5, false);
            return isExplicitRoleOverride
                ? SheetValueHelper.GetBool(row, 4, false)
                : DefaultAllowed(role, permissionKey);
        }

        return DefaultAllowed(role, permissionKey);
    }

    private async Task<bool> HasAnySavedRolePermissionsAsync(string role)
    {
        var rows = await ReadRoleRowsAsync();
        return rows.Any(r => SheetValueHelper.GetString(r, 0).Equals(role, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PermissionMatrixViewModel> GetMatrixAsync()
    {
        await SeedDefaultsAsync();
        var activeRoles = (await roleService.GetAllAsync()).Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
        var activeUsers = (await userService.GetAllUsersAsync()).Where(x => x.IsActive).OrderBy(x => x.Role).ThenBy(x => x.FullName).ToList();
        var definitions = PermissionCatalog.Definitions();
        var roleRows = await ReadRoleRowsAsync();
        var userRows = await ReadUserRowsAsync();
        var separateUserIds = userRows
            .Where(r => SheetValueHelper.GetString(r, 2).Equals("__mode__", StringComparison.OrdinalIgnoreCase) && SheetValueHelper.GetBool(r, 4, false))
            .Select(r => SheetValueHelper.GetString(r, 0))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roleItems = new List<PermissionItem>();
        foreach (var role in activeRoles)
        {
            foreach (var definition in definitions)
            {
                var savedRow = roleRows.FirstOrDefault(r =>
                    SheetValueHelper.GetString(r, 0).Equals(role.Name, StringComparison.OrdinalIgnoreCase) &&
                    SheetValueHelper.GetString(r, 2).Equals(definition.PermissionKey, StringComparison.OrdinalIgnoreCase));

                roleItems.Add(new PermissionItem
                {
                    RoleName = role.Name,
                    GroupName = definition.GroupName,
                    PermissionKey = definition.PermissionKey,
                    PermissionLabel = definition.PermissionLabel,
                    Allowed = savedRow == null || !SheetValueHelper.GetBool(savedRow, 5, false)
                        ? DefaultAllowed(role.Name, definition.PermissionKey)
                        : SheetValueHelper.GetBool(savedRow, 4, false)
                });
            }
        }

        var userItems = new List<UserWisePermissionItem>();
        foreach (var user in activeUsers)
        {
            foreach (var definition in definitions)
            {
                var savedRow = separateUserIds.Contains(user.Id)
                    ? userRows.FirstOrDefault(r =>
                        SheetValueHelper.GetString(r, 0).Equals(user.Id, StringComparison.OrdinalIgnoreCase) &&
                        SheetValueHelper.GetString(r, 2).Equals(definition.PermissionKey, StringComparison.OrdinalIgnoreCase))
                    : null;

                var roleAllowed = roleItems.Any(x => x.RoleName.Equals(user.Role, StringComparison.OrdinalIgnoreCase) && x.PermissionKey == definition.PermissionKey && x.Allowed);

                userItems.Add(new UserWisePermissionItem
                {
                    UserId = user.Id,
                    Username = user.Username,
                    RoleName = user.Role,
                    GroupName = definition.GroupName,
                    PermissionKey = definition.PermissionKey,
                    PermissionLabel = definition.PermissionLabel,
                    Allowed = savedRow == null ? roleAllowed : SheetValueHelper.GetBool(savedRow, 4, false)
                });
            }
        }

        return new PermissionMatrixViewModel
        {
            Roles = activeRoles,
            Users = activeUsers,
            Items = roleItems,
            UserItems = userItems,
            SeparateUserIds = separateUserIds,
            Groups = definitions.Select(x => x.GroupName).Distinct().ToList()
        };
    }

    public async Task SaveMatrixAsync(Dictionary<string, bool> values)
    {
        var activeRoles = (await roleService.GetAllAsync()).Where(x => x.IsActive).ToList();
        var definitions = PermissionCatalog.Definitions();
        var rows = new List<IList<object>>();

        foreach (var role in activeRoles)
        {
            foreach (var definition in definitions)
            {
                var allowed = values.TryGetValue($"{role.Name}|{definition.PermissionKey}", out var savedValue) && savedValue;
                rows.Add(new List<object> { role.Name, definition.GroupName, definition.PermissionKey, definition.PermissionLabel, allowed, true });
            }
        }

        await sheetsService.ClearAsync(RoleClearRange);
        await sheetsService.WriteRowsAsync(RoleWriteRange, rows);
        memoryCache.Remove(RoleCacheKey);
        memoryCache.Remove(UserCacheKey);
    }

    public async Task SaveUserMatrixAsync(Dictionary<string, bool> values, HashSet<string> separateUserIds)
    {
        var activeUsers = (await userService.GetAllUsersAsync()).Where(x => x.IsActive).ToList();
        var definitions = PermissionCatalog.Definitions();
        var rows = new List<IList<object>>();

        foreach (var user in activeUsers.Where(x => separateUserIds.Contains(x.Id)))
        {
            rows.Add(new List<object> { user.Id, user.Username, "__mode__", "Separate User Rights", true, true, "Separate" });
            foreach (var definition in definitions)
            {
                var allowed = values.TryGetValue($"{user.Id}|{definition.PermissionKey}", out var savedValue) && savedValue;
                rows.Add(new List<object>
                {
                    user.Id, user.Username, definition.PermissionKey, definition.PermissionLabel, allowed, true, "Separate"
                });
            }
        }

        await sheetsService.ClearAsync(UserClearRange);
        if (rows.Count > 0) await sheetsService.WriteRowsAsync(UserWriteRange, rows);
        memoryCache.Remove(UserCacheKey);
    }

    public async Task SeedDefaultsAsync()
    {
        var existingRows = await ReadRoleRowsAsync();
        var activeRoles = (await roleService.GetAllAsync()).Where(x => x.IsActive).ToList();
        var definitions = PermissionCatalog.Definitions();

        // Existing installations already contain RolePermissions rows. Whenever
        // new permission definitions are introduced, merge the missing role/key
        // combinations into Google Sheet without overwriting saved selections.
        var expectedCount = activeRoles.Count * definitions.Count;
        var existingKeys = existingRows
            .Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0))
                     && !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 2)))
            .Select(r => $"{SheetValueHelper.GetString(r, 0)}|{SheetValueHelper.GetString(r, 2)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasMissingRows = activeRoles.Any(role => definitions.Any(definition =>
            !existingKeys.Contains($"{role.Name}|{definition.PermissionKey}")));

        if (existingRows.Any() && !hasMissingRows && existingKeys.Count >= expectedCount) return;

        var rows = new List<IList<object>>();
        foreach (var role in activeRoles)
        {
            foreach (var definition in definitions)
            {
                var savedRow = existingRows.FirstOrDefault(r =>
                    SheetValueHelper.GetString(r, 0).Equals(role.Name, StringComparison.OrdinalIgnoreCase) &&
                    SheetValueHelper.GetString(r, 2).Equals(definition.PermissionKey, StringComparison.OrdinalIgnoreCase));

                if (savedRow != null)
                {
                    rows.Add(new List<object>
                    {
                        role.Name,
                        definition.GroupName,
                        definition.PermissionKey,
                        definition.PermissionLabel,
                        SheetValueHelper.GetBool(savedRow, 4, DefaultAllowed(role.Name, definition.PermissionKey)),
                        SheetValueHelper.GetBool(savedRow, 5, false)
                    });
                }
                else
                {
                    rows.Add(new List<object>
                    {
                        role.Name,
                        definition.GroupName,
                        definition.PermissionKey,
                        definition.PermissionLabel,
                        DefaultAllowed(role.Name, definition.PermissionKey),
                        false
                    });
                }
            }
        }

        await sheetsService.ClearAsync(RoleClearRange);
        await sheetsService.WriteRowsAsync(RoleWriteRange, rows);
        memoryCache.Remove(RoleCacheKey);
        memoryCache.Remove(UserCacheKey);
    }

    private static bool DefaultAllowed(string role, string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(permissionKey)) return false;

        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase)) return true;
        if (CommonExperiencePermissions.Contains(permissionKey)) return true;
        if (permissionKey.StartsWith("field.inquiry.", StringComparison.OrdinalIgnoreCase)) return true;
        if (InquiryWorkingPermissions.Contains(permissionKey)) return true;

        if (role.Equals("Support", StringComparison.OrdinalIgnoreCase))
            return permissionKey is "dashboard.view" or "dashboard.kpi" or "dashboard.focus"
                or "inquiry.create" or "inquiry.create.support" or "inquiry.assign.admin"
                or "reports.support" or "reports.detailed"
                or "implementation.view" or "implementation.schedule" or "implementation.progress" or "implementation.transfer" or "implementation.complete"
                or "implementation.reopen" or "implementation.close.notrequired" or "implementation.pause" or "implementation.paidtraining" or "implementation.feedback.view" or "implementation.reminder.view" or "implementation.ai.view"
                or "field.implementation.customer" or "field.implementation.license" or "field.implementation.pin" or "field.implementation.assignment"
                or "field.implementation.schedule" or "field.implementation.progress" or "field.implementation.feedback";

        if (role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase))
            return permissionKey is "dashboard.view" or "dashboard.kpi" or "dashboard.business" or "dashboard.focus"
                or "inquiry.create.support" or "inquiry.assign.admin"
                or "reports.view" or "reports.support" or "reports.detailed"
                or "implementation.view" or "implementation.view.all" or "implementation.edit" or "implementation.assign" or "implementation.schedule"
                or "implementation.progress" or "implementation.transfer" or "implementation.complete" or "implementation.reopen" or "implementation.close.notrequired"
                or "implementation.pause" or "implementation.paidtraining" or "implementation.feedback.view"
                or "implementation.reminder.view" or "implementation.ai.view"
                or "field.implementation.customer" or "field.implementation.license" or "field.implementation.pin" or "field.implementation.assignment"
                or "field.implementation.schedule" or "field.implementation.progress" or "field.implementation.feedback";

        if (role.Equals("Partner", StringComparison.OrdinalIgnoreCase))
            return permissionKey is "dashboard.view" or "dashboard.kpi" or "dashboard.focus" or "dashboard.live"
                or "inquiry.create" or "inquiry.create.support"
                or "reports.view" or "reports.partner" or "reports.detailed" or "field.reports.amount"
                or "implementation.view" or "implementation.sold.setup" or "implementation.paidtraining" or "implementation.feedback.view"
                or "field.implementation.customer" or "field.implementation.license" or "field.implementation.assignment"
                or "product.view" or "field.product.name" or "field.product.version" or "field.product.price"
                or "field.product.directprice" or "field.product.partnerprice"
                or "stock.view" or "stock.amountreport.view" or "field.stock.qty" or "field.stock.amount"
                or "commission.view";

        if (role.Equals("User", StringComparison.OrdinalIgnoreCase))
            return permissionKey is "dashboard.view" or "dashboard.kpi" or "dashboard.focus" or "dashboard.live"
                or "reports.view" or "reports.user" or "reports.detailed" or "field.reports.amount"
                or "implementation.view" or "implementation.create" or "implementation.sold.setup" or "implementation.edit" or "implementation.paidtraining"
                or "implementation.close.notrequired" or "implementation.pause" or "implementation.feedback.view" or "implementation.reminder.view" or "implementation.ai.view"
                or "field.implementation.customer" or "field.implementation.license" or "field.implementation.pin" or "field.implementation.assignment"
                or "field.implementation.schedule" or "field.implementation.progress" or "field.implementation.feedback";

        return false;
    }
}
