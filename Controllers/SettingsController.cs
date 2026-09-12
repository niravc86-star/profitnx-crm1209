using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Services;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IPermissionService _permissionService;

    public SettingsController(IConfiguration configuration, IWebHostEnvironment environment, IPermissionService permissionService)
    {
        _configuration = configuration;
        _environment = environment;
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(User, "settings.view"))
            return RedirectToAction("AccessDenied", "Account");

        ViewBag.Host = _configuration["Smtp:Host"] ?? string.Empty;
        ViewBag.Port = _configuration["Smtp:Port"] ?? "587";
        ViewBag.From = _configuration["Smtp:From"] ?? string.Empty;
        ViewBag.Username = _configuration["Smtp:Username"] ?? string.Empty;
        ViewBag.Password = _configuration["Smtp:Password"] ?? string.Empty;
        ViewBag.NotificationCrm = ReadBoolean("Notifications:Crm", true);
        ViewBag.NotificationEmail = ReadBoolean("Notifications:Email", false);
        ViewBag.NotificationWhatsApp = ReadBoolean("Notifications:WhatsApp", false);
        ViewBag.NotificationWhatsAppDefault = _configuration["Notifications:WhatsAppDefaultNumber"] ?? string.Empty;
        ViewBag.NotificationWhatsAppUseApp = ReadBoolean("Notifications:WhatsAppUseApp", false);
        ViewBag.ImplementationFeedbackEmail = _configuration["Implementation:FeedbackReceiverEmail"] ?? "support@profitnx.com";
        ViewBag.ImplementationReminderMinutes = _configuration["Implementation:ReminderMinutes"] ?? "15";
        ViewBag.ImplementationPublicBaseUrl = _configuration["Implementation:PublicBaseUrl"] ?? string.Empty;
        ViewBag.ImplementationNotifyAllAdmins = ReadBoolean("Implementation:NotifyAllAdmins", true);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        string? section,
        string? host,
        string? port,
        string? from,
        string? username,
        string? password,
        string[]? notificationCrm,
        string[]? notificationEmail,
        string[]? notificationWhatsApp,
        string? whatsAppDefaultNumber,
        string[]? whatsAppUseApp,
        string? feedbackReceiverEmail,
        string? reminderMinutes,
        string? publicBaseUrl,
        string[]? notifyAllAdmins)
    {
        if (!await _permissionService.HasPermissionAsync(User, "settings.manage"))
            return RedirectToAction("AccessDenied", "Account");

        var runtimePath = GetRuntimeSettingsPath();
        var root = await ReadRuntimeSettingsAsync(runtimePath);

        if (string.Equals(section, "smtp", StringComparison.OrdinalIgnoreCase))
        {
            root["Smtp"] = new JsonObject
            {
                ["Host"] = string.IsNullOrWhiteSpace(host) ? Current("Smtp:Host") : host.Trim(),
                ["Port"] = string.IsNullOrWhiteSpace(port) ? Current("Smtp:Port", "587") : port.Trim(),
                ["From"] = string.IsNullOrWhiteSpace(from) ? Current("Smtp:From") : from.Trim(),
                ["Username"] = string.IsNullOrWhiteSpace(username) ? Current("Smtp:Username") : username.Trim(),
                ["Password"] = string.IsNullOrWhiteSpace(password) ? Current("Smtp:Password") : password
            };
        }
        else if (string.Equals(section, "notifications", StringComparison.OrdinalIgnoreCase))
        {
            // Checkbox arrays contain "true" when checked and only "false" when unchecked.
            // This avoids the common HTML checkbox issue where an unchecked option is omitted.
            root["Notifications"] = new JsonObject
            {
                ["Crm"] = IsChecked(notificationCrm),
                ["Email"] = IsChecked(notificationEmail),
                ["WhatsApp"] = IsChecked(notificationWhatsApp),
                ["WhatsAppDefaultNumber"] = string.IsNullOrWhiteSpace(whatsAppDefaultNumber)
                    ? Current("Notifications:WhatsAppDefaultNumber")
                    : whatsAppDefaultNumber.Trim(),
                ["WhatsAppUseApp"] = whatsAppUseApp == null
                    ? ReadBoolean("Notifications:WhatsAppUseApp", false)
                    : IsChecked(whatsAppUseApp)
            };
        }
        else if (string.Equals(section, "implementation", StringComparison.OrdinalIgnoreCase))
        {
            var parsedMinutes = int.TryParse(reminderMinutes, out var minutes) ? Math.Clamp(minutes, 5, 120) : 15;
            root["Implementation"] = new JsonObject
            {
                ["FeedbackReceiverEmail"] = string.IsNullOrWhiteSpace(feedbackReceiverEmail) ? "support@profitnx.com" : feedbackReceiverEmail.Trim(),
                ["ReminderMinutes"] = parsedMinutes.ToString(),
                ["PublicBaseUrl"] = (publicBaseUrl ?? string.Empty).Trim().TrimEnd('/'),
                ["NotifyAllAdmins"] = IsChecked(notifyAllAdmins)
            };
        }
        else
        {
            TempData["Error"] = "Invalid settings section.";
            return RedirectToAction(nameof(Index));
        }

        await WriteRuntimeSettingsAsync(runtimePath, root);

        if (_configuration is IConfigurationRoot configurationRoot)
        {
            try { configurationRoot.Reload(); }
            catch { /* File watcher will reload the runtime JSON provider. */ }
        }

        TempData["Success"] = string.Equals(section, "smtp", StringComparison.OrdinalIgnoreCase)
            ? "SMTP settings saved successfully."
            : string.Equals(section, "implementation", StringComparison.OrdinalIgnoreCase)
                ? "Implementation automation settings saved successfully."
                : "Notification channels saved successfully. Only selected channels will be used.";
        return RedirectToAction(nameof(Index));
    }

    private bool ReadBoolean(string key, bool defaultValue)
    {
        var value = _configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private string Current(string key, string defaultValue = "")
        => _configuration[key] ?? defaultValue;

    private string GetRuntimeSettingsPath()
    {
        var directory = Path.Combine(_environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "runtime-settings.json");
    }

    private static bool IsChecked(IEnumerable<string>? values)
        => values?.Any(x => string.Equals(x, "true", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(x, "on", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(x, "1", StringComparison.OrdinalIgnoreCase)) == true;

    private static async Task<JsonObject> ReadRuntimeSettingsAsync(string path)
    {
        if (!System.IO.File.Exists(path)) return new JsonObject();

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(path);
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch
        {
            // Never lose the settings screen because a previous partial file exists.
            return new JsonObject();
        }
    }

    private static async Task WriteRuntimeSettingsAsync(string path, JsonObject root)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var temporaryPath = path + ".tmp";
        await System.IO.File.WriteAllTextAsync(temporaryPath, root.ToJsonString(options));
        System.IO.File.Move(temporaryPath, path, true);
    }
}
