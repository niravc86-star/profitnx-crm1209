using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Claims;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public class NotificationService : INotificationService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private readonly IConfiguration _configuration;
    private readonly IUserService _userService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWebPushService _webPushService;
    private const string ReadRange = "Notifications!A2:N";
    private const string ClearRange = "Notifications!A2:N200000";
    private static readonly ConcurrentDictionary<string, DateTime> RecentNotificationKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim NotificationMutationLock = new(1, 1);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    public NotificationService(
        IGoogleSheetsService googleSheetsService,
        IConfiguration configuration,
        IUserService userService,
        IHttpContextAccessor httpContextAccessor,
        IWebPushService webPushService)
    {
        _googleSheetsService = googleSheetsService;
        _configuration = configuration;
        _userService = userService;
        _httpContextAccessor = httpContextAccessor;
        _webPushService = webPushService;
    }

    private static string GetForwardedToDisplay(Inquiry inquiry)
    {
        if (!string.IsNullOrWhiteSpace(inquiry.ForwardedToUserName)) return inquiry.ForwardedToUserName;
        if (!string.IsNullOrWhiteSpace(inquiry.ForwardedToPartnerName)) return inquiry.ForwardedToPartnerName;
        return "Admin Team";
    }

    // Notification settings helpers. CRM defaults ON; Email and WhatsApp default OFF until explicitly selected in Settings.
    private bool IsCrmEnabled()
    {
        var v = _configuration["Notifications:Crm"];
        return string.IsNullOrWhiteSpace(v) ? true : v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsEmailEnabled()
    {
        var v = _configuration["Notifications:Email"];
        return !string.IsNullOrWhiteSpace(v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsWhatsAppEnabled()
    {
        var v = _configuration["Notifications:WhatsApp"];
        return !string.IsNullOrWhiteSpace(v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsWhatsAppUseApp()
    {
        var v = _configuration["Notifications:WhatsAppUseApp"] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private string GetWhatsAppDefaultNumberDigits()
    {
        var def = _configuration["Notifications:WhatsAppDefaultNumber"] ?? string.Empty;
        var digits = new string((def ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return string.Empty;
        if (digits.Length == 10) digits = "91" + digits;
        return digits;
    }

    private (string Channel, string Url, string Status) BuildWhatsAppAction(string? mobile, string message)
    {
        // Use only the provided mobile (inquiry/recipient). Do not fall back to a global default number.
        var digits = string.IsNullOrWhiteSpace(mobile) ? string.Empty : new string(mobile.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return ("WhatsApp", string.Empty, "No mobile");
        if (digits.Length == 10) digits = "91" + digits;
        // Always use WhatsApp Web flow (free) to avoid requiring paid Business API; App link kept for optional mobile deep link.
        var useApp = IsWhatsAppUseApp();
        var url = useApp ? $"whatsapp://send?phone={digits}&text={Uri.EscapeDataString(message)}" : $"https://wa.me/{digits}?text={Uri.EscapeDataString(message)}";
        var channel = useApp ? "WhatsApp App" : "WhatsApp Web";
        var status = useApp ? "Open WhatsApp App" : "Open WhatsApp Web";
        return (channel, url, status);
    }

    public async Task<List<NotificationLog>> GetLogsAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(ReadRange);
        return rows.Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0)))
            .Select(r => new NotificationLog
            {
                Id = SheetValueHelper.GetString(r, 0),
                // FIX (2026-08-17): a row whose SentDate fails to parse must NEVER
                // fall back to "now" - that silently made every unparsable old
                // "User Login Alert" row look brand new on every single page load,
                // which is why old alerts (13/14 Aug) kept resurfacing days later
                // (see CHANGELOG-2026-08-17-LOGIN-LOGOUT-ALERT-AND-DARK-THEME-FIX.md).
                // DateTime.MinValue instead makes an unparsable row look OLD, so it
                // safely drops out of every "recent" filter instead of reappearing.
                SentDate = SheetValueHelper.GetDateTime(r, 1) ?? DateTime.MinValue,
                Channel = SheetValueHelper.GetString(r, 2),
                Recipient = SheetValueHelper.GetString(r, 3),
                Message = SheetValueHelper.GetString(r, 4),
                RelatedEntityType = SheetValueHelper.GetString(r, 5),
                RelatedEntityId = SheetValueHelper.GetString(r, 6),
                Status = SheetValueHelper.GetString(r, 7),
                ActionUrl = SheetValueHelper.GetString(r, 8),
                FlowType = SheetValueHelper.GetString(r, 9),
                RecipientUserId = SheetValueHelper.GetString(r, 10),
                SenderUserId = SheetValueHelper.GetString(r, 11),
                SenderName = SheetValueHelper.GetString(r, 12),
                ReadDate = SheetValueHelper.GetDateTime(r, 13)
            })
            .OrderByDescending(x => x.SentDate)
            .ToList();
    }

    public async Task<List<NotificationLog>> GetForUserAsync(string userId, string loginName, string role, bool includeAll = false)
    {
        var logs = await GetLogsAsync();
        if (includeAll)
        {
            // "notifications.send" (view-all) lets a role review every notification
            // that was sent, but "User Login Alert" rows are Admin-only by nature
            // (they exist so Admin can see who signed in) and must stay hidden from
            // any other role even when that role can otherwise see everything else.
            if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                return logs.Where(x => !IsLoginAlertFlow(x.FlowType)).ToList();
            return logs;
        }
        var user = (await _userService.GetAllUsersAsync()).FirstOrDefault(x =>
            x.Id.Equals(userId, StringComparison.OrdinalIgnoreCase) ||
            x.Username.Equals(loginName, StringComparison.OrdinalIgnoreCase) ||
            x.FullName.Equals(loginName, StringComparison.OrdinalIgnoreCase));
        var legacyKeys = new[] { user?.FullName, user?.Username, user?.Email, user?.Mobile, loginName,
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : null,
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin Team" : null,
            role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase) ? "Support Head" : null }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeRecipient).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return logs.Where(x =>
            (!string.IsNullOrWhiteSpace(x.RecipientUserId) && x.RecipientUserId.Equals(userId, StringComparison.OrdinalIgnoreCase)) ||
            (string.IsNullOrWhiteSpace(x.RecipientUserId) && legacyKeys.Contains(NormalizeRecipient(x.Recipient))))
            .OrderByDescending(x => x.SentDate).ToList();
    }

    public async Task<int> GetUnreadCountForUserAsync(string userId, string loginName, string role)
        => (await GetForUserAsync(userId, loginName, role)).Count(x =>
            x.Channel.Equals("CRM", StringComparison.OrdinalIgnoreCase) &&
            !x.Status.Equals("Read", StringComparison.OrdinalIgnoreCase) &&
            !IsLoginAlertFlow(x.FlowType));

    // ADDED (2026-08-17): "UserLogin" and "UserLogout" are both live, Admin-only
    // heads-up alerts (not permanent task/notification queue items - the durable
    // record is the Login History Report), so every place that used to special-case
    // "UserLogin" alone now treats both the same way via this one helper.
    private static bool IsLoginAlertFlow(string flowType)
        => flowType.Equals("UserLogin", StringComparison.OrdinalIgnoreCase)
        || flowType.Equals("UserLogout", StringComparison.OrdinalIgnoreCase);

    public async Task QueueAsync(NotificationFormViewModel model)
    {
        var channel = (model.Channel ?? string.Empty).Trim();
        var recipient = (model.Recipient ?? string.Empty).Trim();
        var message = (model.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(message)) return;

        if (channel.Equals("CRM", StringComparison.OrdinalIgnoreCase))
        {
            if (IsCrmEnabled())
                await AppendLogAsync("CRM", recipient, message, model.RelatedEntityType, model.RelatedEntityId, "Unread", string.Empty, "ManualQueue");
            return;
        }

        if (channel.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsWhatsAppEnabled()) return;
            var (actualChannel, url, status) = BuildWhatsAppAction(recipient, message);
            await AppendLogAsync(actualChannel, recipient, message, model.RelatedEntityType, model.RelatedEntityId, status, url, "ManualQueue");
            return;
        }

        if (!IsEmailEnabled()) return;
        var sent = await TrySendEmailAsync(recipient, "ProfitNx CRM Notification", message, false);
        await AppendLogAsync("Email", recipient, message, model.RelatedEntityType, model.RelatedEntityId, sent ? "Sent" : "SMTP not configured", string.Empty, "ManualQueue");
    }

    // ── Support → Admin ────────────────────────────────────────────────────────
    // Support team mokle inquiry → Admin ne CRM notification aave + email aave
    // Email support user na own email thi sent thay (per-user SMTP)
    public async Task NotifySupportToAdminAsync(Inquiry inquiry, string supportUserName, string adminEmail)
    {
        supportUserName = CleanName(supportUserName);

        // Find admin users to notify via CRM
        var allUsers = await _userService.GetAllUsersAsync();
        var admins = allUsers.Where(x => x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) && x.IsActive).ToList();

        // Build a clear, detailed message for CRM / WhatsApp / Email that includes key inquiry fields
        var submittedOn = inquiry.CreatedDate == default ? DateTime.Now : inquiry.CreatedDate;
        var detailLines = new List<string>
        {
            $"Support User: {supportUserName}",
            $"Customer: {Value(inquiry.FirmName)}{(string.IsNullOrWhiteSpace(inquiry.PersonName) ? string.Empty : " (" + Value(inquiry.PersonName) + ")")}",
            $"City: {Value(inquiry.City)}",
            $"Mobile: {Value(inquiry.Mobile1)}",
            $"Email: {Value(inquiry.Email1)}",
            $"Product: {Value(inquiry.ProductName)}",
            $"Inquiry Date: {FormatDate(submittedOn)}",
            $"Requirement / Remarks: {Value(inquiry.Remarks)}",
            $"Submitted Via: {Value(inquiry.InquirySource)}"
        };

        var detailedPlain = string.Join("\n", detailLines);
        var subject = BuildSubject("Support Team - New Inquiry Submitted", inquiry);
        var crmMessage = BuildCrmMessage("New Inquiry from Support Team", inquiry, "Admin", detailedPlain, "Admin Team");

        // Email: sent FROM support user's configured email (per-user SMTP) when available.
        var supportUser = allUsers.FirstOrDefault(x =>
            (x.FullName.Equals(supportUserName, StringComparison.OrdinalIgnoreCase) || x.Username.Equals(supportUserName, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(x.Email));

        // Self notification removed for support user.

        // CRM + WhatsApp + Email notification to each admin (respect notification settings).
        foreach (var admin in admins)
        {
            var adminName = CleanName(string.IsNullOrWhiteSpace(admin.FullName) ? admin.Username : admin.FullName);

            // Personalized messages for each channel using the same detailed content
            var crmMsgForAdmin = BuildCrmMessage("New Inquiry Submitted by Support Team", inquiry, adminName, detailedPlain, adminName);
            var waForAdmin = BuildWhatsAppMessage("New Inquiry Submitted by Support Team", inquiry, adminName, detailedPlain, adminName);
            var html = BuildEmailHtml("New Inquiry Submitted by Support Team", adminName, inquiry, "Inquiry Details", detailedPlain, $"This inquiry was submitted by {supportUserName} from Support Team.", adminName);

            if (IsCrmEnabled()) await AppendLogAsync("CRM", adminName, crmMsgForAdmin, "Inquiry", inquiry.Id, "In App", string.Empty, "SupportToAdmin");

            if (!string.IsNullOrWhiteSpace(admin.Mobile) && IsWhatsAppEnabled())
            {
                var (channel, url, status) = BuildWhatsAppAction(admin.Mobile, waForAdmin);
                await AppendLogAsync(channel, admin.Mobile, waForAdmin, "Inquiry", inquiry.Id, string.IsNullOrWhiteSpace(url) ? "No mobile" : status, url, "SupportToAdmin");
            }

            if (!string.IsNullOrWhiteSpace(admin.Email) && IsEmailEnabled())
            {
                var sent = await TrySendEmailAsync(admin.Email, subject, html, true, supportUser);
                await AppendLogAsync("Email", admin.Email, ToPlainText(html), "Inquiry", inquiry.Id, sent ? "Sent" : "SMTP not configured", string.Empty, "SupportToAdmin");
            }
        }

        if (!admins.Any())
        {
            if (IsCrmEnabled()) await AppendLogAsync("CRM", "Admin", crmMessage, "Inquiry", inquiry.Id, "In App", string.Empty, "SupportToAdmin");
        }
    }

    // ── Admin → Partner ────────────────────────────────────────────────────────
    // Admin forward kare partner ne → partner ne CRM + email + WhatsApp notification
    public async Task NotifyAdminToPartnerAsync(Inquiry inquiry, string partnerName, string partnerMobile, string partnerEmail)
    {
        partnerName = CleanName(partnerName);
        var note = $"Admin forwarded this inquiry to {partnerName}. Requirement: {Value(inquiry.Remarks)}";
        var subject = BuildSubject("Inquiry Forwarded to You - Admin", inquiry);
        var whatsApp = BuildWhatsAppMessage("Inquiry Forwarded by Admin", inquiry, partnerName, note, partnerName);
        var crmMessage = BuildCrmMessage("Inquiry Forwarded by Admin", inquiry, partnerName, note, partnerName);
        var html = BuildEmailHtml("Inquiry Forwarded by Admin", partnerName, inquiry, "Latest Update", note, "Admin has assigned this inquiry to you. Please follow up.", partnerName);
        if (IsCrmEnabled()) await AppendLogAsync("CRM", partnerName, crmMessage, "Inquiry", inquiry.Id, "In App", string.Empty, "AdminToPartner");

        if (!string.IsNullOrWhiteSpace(partnerMobile) && IsWhatsAppEnabled())
        {
            var (channel, url, status) = BuildWhatsAppAction(partnerMobile, whatsApp);
            await AppendLogAsync(channel, partnerMobile, whatsApp, "Inquiry", inquiry.Id, string.IsNullOrWhiteSpace(url) ? "No mobile" : status, url, "AdminToPartner");
        }

        if (!string.IsNullOrWhiteSpace(partnerEmail) && IsEmailEnabled())
        {
            var sent = await TrySendEmailAsync(partnerEmail, subject, html, true);
            await AppendLogAsync("Email", partnerEmail, ToPlainText(html), "Inquiry", inquiry.Id, sent ? "Sent" : "SMTP not configured", string.Empty, "AdminToPartner");
        }
    }

    // ── Admin → User ───────────────────────────────────────────────────────────
    // Admin assign kare user ne → user ne CRM notification aave
    public async Task NotifyAdminToUserAsync(Inquiry inquiry, string userName, string userMobile, string userEmail)
    {
        userName = CleanName(userName);
        var note = $"Admin assigned this inquiry to {userName}. Requirement: {Value(inquiry.Remarks)}";
        var subject = BuildSubject("Inquiry Assigned to You - Admin", inquiry);
        var crmMessage = BuildCrmMessage("Inquiry Assigned by Admin", inquiry, userName, note, userName);
        var html = BuildEmailHtml("Inquiry Assigned by Admin", userName, inquiry, "Latest Update", note, "Admin assigned this inquiry to you. Please action it.", userName);
        if (IsCrmEnabled()) await AppendLogAsync("CRM", userName, crmMessage, "Inquiry", inquiry.Id, "In App", string.Empty, "AdminToUser");

        if (!string.IsNullOrWhiteSpace(userMobile) && IsWhatsAppEnabled())
        {
            var waMessage = BuildWhatsAppMessage("Inquiry Assigned by Admin", inquiry, userName, note, userName);
            var (channel, url, status) = BuildWhatsAppAction(userMobile, waMessage);
            await AppendLogAsync(channel, userMobile, waMessage, "Inquiry", inquiry.Id, string.IsNullOrWhiteSpace(url) ? "No mobile" : status, url, "AdminToUser");
        }

        if (!string.IsNullOrWhiteSpace(userEmail) && IsEmailEnabled())
        {
            var sent = await TrySendEmailAsync(userEmail, subject, html, true);
            await AppendLogAsync("Email", userEmail, ToPlainText(html), "Inquiry", inquiry.Id, sent ? "Sent" : "SMTP not configured", string.Empty, "AdminToUser");
        }
    }

    // ── Generic new inquiry notification ──────────────────────────────────────
    public async Task NotifyInquiryCreatedAsync(Inquiry inquiry, string recipientName, string mobile, string email, string submittedByName, string submittedByRole)
    {
        recipientName = CleanName(recipientName);
        submittedByName = CleanName(submittedByName);
        submittedByRole = string.IsNullOrWhiteSpace(submittedByRole) ? "User" : submittedByRole.Trim();
        var note = $"New inquiry submitted by {submittedByName} ({submittedByRole}). Requirement: {Value(inquiry.Remarks)}";
        var subject = BuildSubject("New Inquiry Submitted", inquiry);
        var whatsApp = BuildWhatsAppMessage("New Inquiry Submitted", inquiry, recipientName, note, recipientName);
        var crmMessage = BuildCrmMessage("New Inquiry Submitted", inquiry, recipientName, note, recipientName);
        var html = BuildEmailHtml("New Inquiry Submitted", recipientName, inquiry, "Submission Details", note, "Please review and assign the next action in Profit Nx CRM.", recipientName);
        await AppendMultiChannelAsync(recipientName, mobile, email, crmMessage, whatsApp, subject, html, inquiry.Id, "Inquiry", "InquiryCreated");
    }

    // ── Legacy: Generic forward (backward compat) ──────────────────────────────
    public async Task NotifyInquiryForwardAsync(Inquiry inquiry, string recipientName, string mobile, string email)
    {
        recipientName = CleanName(recipientName);
        var forwardTo = string.IsNullOrWhiteSpace(inquiry.ForwardedToPartnerName) ? string.Empty : inquiry.ForwardedToPartnerName;
        var note = string.IsNullOrWhiteSpace(forwardTo)
            ? $"Inquiry assigned/forwarded. Requirement: {Value(inquiry.Remarks)}"
            : $"{Value(string.IsNullOrWhiteSpace(inquiry.SupportExecutiveName) ? inquiry.AssignedUserName : inquiry.SupportExecutiveName)} forwarded inquiry to {forwardTo}. Requirement: {Value(inquiry.Remarks)}";
        var subject = BuildSubject("New Inquiry Assigned - Admin", inquiry);
        var whatsApp = BuildWhatsAppMessage("New Inquiry Assigned - Admin", inquiry, recipientName, note, recipientName);
        var crmMessage = BuildCrmMessage("New Inquiry Assigned - Admin", inquiry, recipientName, note, recipientName);
        var html = BuildEmailHtml("New Inquiry Assigned - Admin", recipientName, inquiry, "Latest Update", note, "Please review this inquiry and update the next status in Profit Nx CRM.", recipientName);
        await AppendMultiChannelAsync(recipientName, mobile, email, crmMessage, whatsApp, subject, html, inquiry.Id, "Inquiry", "InquiryForward");
    }

    public async Task NotifyInquiryUpdateAsync(Inquiry inquiry, string recipientName, string mobile, string email, string updateNote)
    {
        recipientName = CleanName(recipientName);
        var subject = BuildSubject("Inquiry Status Updated", inquiry);
        var whatsApp = BuildWhatsAppMessage("Inquiry Status Updated", inquiry, recipientName, updateNote, recipientName);
        var crmMessage = BuildCrmMessage("Inquiry Status Updated", inquiry, recipientName, updateNote, recipientName);
        var html = BuildEmailHtml("Inquiry Status Updated", recipientName, inquiry, "Latest Update", updateNote, "This is the latest status update for your inquiry.", recipientName);
        await AppendMultiChannelAsync(recipientName, mobile, email, crmMessage, whatsApp, subject, html, inquiry.Id, "Inquiry", "InquiryUpdate");
    }


    public async Task NotifyInquiryUpdateCrmOnlyAsync(Inquiry inquiry, string recipientName, string updateNote)
    {
        if (!IsCrmEnabled()) return;
        recipientName = CleanName(recipientName);
        var crmMessage = BuildCrmMessage("Inquiry Update", inquiry, recipientName, updateNote, recipientName);
        await AppendLogAsync("CRM", recipientName, crmMessage, "Inquiry", inquiry.Id, "Unread", string.Empty, "CrmOnly");
    }

    private async Task AppendMultiChannelAsync(string recipientName, string mobile, string email, string crmMessage, string whatsappMessage, string emailSubject, string emailHtml, string entityId, string entityType, string flowType)
    {
        // Every generic inquiry update must respect the settings selected on the
        // Notification Channels screen. Previously this method always created all
        // three channel rows, which looked like duplicate notifications.
        if (IsCrmEnabled())
            await AppendLogAsync("CRM", recipientName, crmMessage, entityType, entityId, "Unread", string.Empty, flowType);

        if (IsWhatsAppEnabled() && !string.IsNullOrWhiteSpace(mobile))
        {
            var (channel, url, status) = BuildWhatsAppAction(mobile, whatsappMessage);
            await AppendLogAsync(channel, mobile, whatsappMessage, entityType, entityId, string.IsNullOrWhiteSpace(url) ? "No mobile" : status, url, flowType);
        }

        if (IsEmailEnabled() && !string.IsNullOrWhiteSpace(email))
        {
            var sent = await TrySendEmailAsync(email, emailSubject, emailHtml, true);
            await AppendLogAsync("Email", email, ToPlainText(emailHtml), entityType, entityId, sent ? "Sent" : "SMTP not configured", string.Empty, flowType);
        }
    }

    // Try sending email; optionally override the From address (for per-user support email)
    private async Task<bool> TrySendEmailAsync(string to, string subject, string body, bool isHtml, AppUser? senderUser = null)
    {
        var host = !string.IsNullOrWhiteSpace(senderUser?.SmtpHost) ? senderUser!.SmtpHost : _configuration["Smtp:Host"];
        var from = !string.IsNullOrWhiteSpace(senderUser?.Email) ? senderUser!.Email : _configuration["Smtp:From"];
        var user = !string.IsNullOrWhiteSpace(senderUser?.SmtpUsername) ? senderUser!.SmtpUsername : _configuration["Smtp:Username"];
        var pass = !string.IsNullOrWhiteSpace(senderUser?.SmtpPassword) ? senderUser!.SmtpPassword : _configuration["Smtp:Password"];
        var enableSsl = senderUser == null ? true : senderUser.SmtpEnableSsl;
        var port = senderUser != null && senderUser.SmtpPort > 0 ? senderUser.SmtpPort : (int.TryParse(_configuration["Smtp:Port"], out var cfgPort) ? cfgPort : 587);
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return false;

        try
        {
            using var client = new SmtpClient(host)
            {
                Port = port,
                EnableSsl = enableSsl,
                UseDefaultCredentials = false,
                Credentials = string.IsNullOrWhiteSpace(user) ? CredentialCache.DefaultNetworkCredentials : new NetworkCredential(user, pass?.Replace(" ", ""))
            };
            using var mail = new MailMessage
            {
                From = new MailAddress(from, "Profit Nx Team"),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            mail.To.Add(to);
            await client.SendMailAsync(mail);
            return true;
        }
        catch { return false; }
    }

    public async Task SendImplementationNotificationAsync(ImplementationCase implementation, IEnumerable<AppUser> recipients, string title, string note, string actionUrl, string flowType, bool includeWhatsApp = true)
    {
        var uniqueRecipients = (recipients ?? Array.Empty<AppUser>())
            .Where(x => x != null && x.IsActive)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        if (uniqueRecipients.Count == 0) return;

        var subject = $"{title} - {Value(implementation.FirmName)} - {Value(implementation.LicenseNumber)}";
        foreach (var recipient in uniqueRecipients)
        {
            var recipientName = CleanName(recipient.FullName);
            var message = BuildImplementationPlainText(title, recipientName, implementation, note);
            if (IsCrmEnabled())
                await AppendLogAsync("CRM", string.IsNullOrWhiteSpace(recipient.Id) ? recipientName : recipient.Id, message, "Implementation", implementation.Id, "Unread", actionUrl, flowType);

            if (includeWhatsApp && IsWhatsAppEnabled() && !string.IsNullOrWhiteSpace(recipient.Mobile))
            {
                var (channel, url, status) = BuildWhatsAppAction(recipient.Mobile, message);
                await AppendLogAsync(channel, recipient.Mobile, message, "Implementation", implementation.Id, status, url, flowType);
            }

            if (IsEmailEnabled() && !string.IsNullOrWhiteSpace(recipient.Email))
            {
                var html = BuildImplementationEmailHtml(title, recipientName, implementation, note, actionUrl);
                var sent = await TrySendEmailAsync(recipient.Email, subject, html, true);
                await AppendLogAsync("Email", recipient.Email, ToPlainText(html), "Implementation", implementation.Id, sent ? "Sent" : "SMTP not configured", actionUrl, flowType);
            }
        }
    }

    public async Task SendFeedbackRequestAsync(ImplementationCase implementation, string feedbackUrl)
    {
        if (string.IsNullOrWhiteSpace(implementation.Email)) return;
        var customerName = CleanName(string.IsNullOrWhiteSpace(implementation.PersonName) ? implementation.FirmName : implementation.PersonName);
        var executiveName = CleanName(string.IsNullOrWhiteSpace(implementation.AssignedMemberName) ? implementation.LastUpdatedByName : implementation.AssignedMemberName);
        var subject = $"Congratulations! ProfitNx Software Training Completed - {Value(implementation.FirmName)}";
        var topics = string.IsNullOrWhiteSpace(implementation.TrainingTopicsSummary)
            ? "Covered: Implementation overview, software usage guidance and customer questions."
            : implementation.TrainingTopicsSummary.Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase);
        var topicsHtml = BuildTrainingTopicsHtml(topics);
        var html = $@"<!DOCTYPE html><html><body style='margin:0;background:#f3f6fb;font-family:Arial,Helvetica,sans-serif;color:#172033;'>
<table width='100%' cellpadding='0' cellspacing='0' role='presentation' style='padding:30px 12px;background:#f3f6fb;'><tr><td align='center'>
<table width='720' cellpadding='0' cellspacing='0' role='presentation' style='width:100%;max-width:720px;background:#ffffff;border:1px solid #dce5f0;border-radius:18px;overflow:hidden;box-shadow:0 18px 48px rgba(15,23,42,.08);'>
<tr><td style='padding:25px 32px;background:linear-gradient(135deg,#0b2b59,#1768d4);color:#fff;'><div style='font-size:27px;font-weight:800;'>ProfitNx</div><div style='margin-top:5px;font-size:13px;opacity:.86;'>Implementation & Training Success</div></td></tr>
<tr><td style='padding:32px;'>
<p style='margin:0 0 16px;font-size:16px;'>Dear <strong>{Html(customerName)}</strong>,</p>
<div style='padding:22px;border-radius:14px;background:#eef6ff;border:1px solid #d8eaff;text-align:center;margin-bottom:24px;'><div style='font-size:30px;'>🎉</div><h1 style='margin:8px 0 6px;font-size:26px;color:#113a70;'>Congratulations!</h1><p style='margin:0;color:#4d6380;line-height:1.65;'>We are delighted to inform you that your <strong>ProfitNx Software Training</strong> has been successfully completed.</p></div>
<p style='line-height:1.75;color:#4e6078;'>Thank you for choosing <strong>ProfitNx</strong> as your trusted business software partner. We sincerely appreciate your valuable time, active participation and trust throughout the training process.</p>
<p style='line-height:1.75;color:#4e6078;'>Our goal is not only to provide software, but also to ensure that you and your team can use it confidently and efficiently to simplify daily business operations.</p>

<h2 style='font-size:18px;color:#123b6d;border-bottom:1px solid #e4ebf4;padding-bottom:10px;margin:28px 0 14px;'>Training Details</h2>
<table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-bottom:20px;'>
{Row("Training For", Value(implementation.PersonName))}
{Row("License Number", Value(implementation.LicenseNumber))}
{Row("Company Name", Value(implementation.FirmName))}
{Row("City", Value(implementation.City))}
{Row("Training Date", implementation.CompletionDate?.ToString("dd/MM/yyyy") ?? "N/A")}
{Row("Training Conducted By", executiveName + " — Implementation Executive, ProfitNx")}
</table>

<h2 style='font-size:18px;color:#123b6d;border-bottom:1px solid #e4ebf4;padding-bottom:10px;margin:28px 0 14px;'>Topics Covered During Training</h2>
<div style='background:#f8fbff;border:1px solid #e2ebf5;border-radius:12px;padding:16px 18px;line-height:1.75;color:#40546f;'>{topicsHtml}</div>

<h2 style='font-size:18px;color:#123b6d;border-bottom:1px solid #e4ebf4;padding-bottom:10px;margin:28px 0 14px;'>We’d Love to Hear Your Experience</h2>
<p style='line-height:1.75;color:#4e6078;'>Your feedback is extremely valuable to us. It helps us improve training quality, enhance our services and deliver an even better experience to every customer. Please spare just one minute to rate your overall training, share comments, tell us what you liked and suggest any additional topics or improvements.</p>
<p style='text-align:center;margin:28px 0;'><a href='{Html(feedbackUrl)}' style='display:inline-block;background:#1768d4;color:#fff;text-decoration:none;padding:14px 28px;border-radius:10px;font-weight:800;font-size:15px;'>Submit Your Feedback</a></p>

<div style='background:#f7f9fc;border-radius:12px;padding:18px 20px;margin-top:24px;'><h3 style='margin:0 0 8px;color:#143b6d;font-size:17px;'>Our Commitment to Your Success</h3><p style='margin:0;color:#52657c;line-height:1.7;'>Even after training is completed, our dedicated support team is available whenever you need assistance, guidance or technical support.</p></div>
<table width='100%' cellpadding='0' cellspacing='0' style='margin:22px 0 8px;color:#40546f;line-height:1.8;'><tr><td>☎ <strong>Helpline:</strong> 0281-3500622<br/>📧 <strong>Email:</strong> <a href='mailto:support@profitnx.com' style='color:#1768d4;'>support@profitnx.com</a><br/>🌐 <strong>Website:</strong> <a href='http://www.profitnx.com' style='color:#1768d4;'>www.profitnx.com</a></td></tr></table>
<p style='line-height:1.75;color:#4e6078;'>Thank you once again for choosing <strong>ProfitNx</strong>. We truly value your trust and look forward to building a long-lasting business relationship with you. We wish you continued success with ProfitNx.</p>
<p style='margin-top:24px;line-height:1.65;'>Warm Regards,<br/><strong>{Html(executiveName)}</strong><br/>Implementation Executive<br/><strong>ProfitNx</strong></p>
</td></tr>
<tr><td style='padding:16px 32px;background:#0b203f;color:#d9e7fb;font-size:12px;text-align:center;'>ProfitNx Business Software · Helpline 0281-3500622 · support@profitnx.com</td></tr>
</table></td></tr></table></body></html>";
        var users = await _userService.GetActiveUsersAsync();
        var adminSender = users.FirstOrDefault(x => x.IsActive
            && x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.Email)
            && !string.IsNullOrWhiteSpace(x.SmtpHost));
        var sent = await TrySendEmailAsync(implementation.Email, subject, html, true, adminSender);
        await AppendLogAsync("Email", implementation.Email, ToPlainText(html), "Implementation", implementation.Id, sent ? "Sent" : "SMTP not configured", feedbackUrl, "ImplementationFeedbackRequest");
    }

    public async Task SendFeedbackReceivedEmailAsync(ImplementationCase implementation, IEnumerable<AppUser> recipients, AppUser? senderUser)
    {
        var uniqueRecipients = (recipients ?? Array.Empty<AppUser>())
            .Where(x => x != null && x.IsActive && !string.IsNullOrWhiteSpace(x.Email))
            .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        if (uniqueRecipients.Count == 0) return;

        var subject = $"Customer Feedback ({implementation.FeedbackRating}/5) - {Value(implementation.FirmName)}";
        foreach (var recipient in uniqueRecipients)
        {
            var recipientName = CleanName(recipient.FullName);
            var html = BuildFeedbackReceivedEmailHtml(implementation, recipientName);
            var sent = await TrySendEmailAsync(recipient.Email, subject, html, true, senderUser);
            await AppendLogAsync("Email", recipient.Email, ToPlainText(html), "Implementation", implementation.Id,
                sent ? "Sent" : "SMTP not configured", $"/Implementation/Details/{Uri.EscapeDataString(implementation.Id)}", "ImplementationFeedbackReceived");
        }
    }

    private static string BuildTrainingTopicsHtml(string topics)
    {
        var lines = (topics ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return "<p style='margin:0;color:#40546f;'>No topic details were recorded.</p>";

        var builder = new StringBuilder();
        builder.Append("<ul style='margin:0;padding:0;list-style:none;'>");
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var isOther = line.StartsWith("Other Points:", StringComparison.OrdinalIgnoreCase);
            var display = isOther ? line : line.StartsWith("Covered:", StringComparison.OrdinalIgnoreCase) ? line[8..].Trim() : line;
            builder.Append(isOther
                ? $"<li style='margin:9px 0;padding:11px 13px;border-radius:10px;background:#fff8e8;border:1px solid #ffe3ad;color:#72520d;'><strong>Other Points Covered:</strong> {Html(display["Other Points:".Length..].Trim())}</li>"
                : $"<li style='margin:7px 0;padding:10px 12px;border-radius:10px;background:#f2fff7;border:1px solid #d5f5e2;color:#245c3d;'><span style='color:#16a34a;font-weight:800;'>✓</span> {Html(display)}</li>");
        }
        builder.Append("</ul>");
        return builder.ToString();
    }

    private static string BuildFeedbackReceivedEmailHtml(ImplementationCase implementation, string recipientName)
    {
        var performance = implementation.FeedbackPerformanceLevel;
        var stars = string.Join(string.Empty, Enumerable.Range(1, 5).Select(i => i <= implementation.FeedbackRating ? "★" : "☆"));
        var topicsHtml = BuildTrainingTopicsHtml(implementation.TrainingTopicsSummary);
        return $@"<!DOCTYPE html><html><body style='margin:0;background:#f3f6fb;font-family:Arial,Helvetica,sans-serif;color:#172033;'>
<table width='100%' cellpadding='0' cellspacing='0' role='presentation' style='padding:30px 12px;background:#f3f6fb;'><tr><td align='center'>
<table width='720' cellpadding='0' cellspacing='0' role='presentation' style='width:100%;max-width:720px;background:#fff;border:1px solid #dce5f0;border-radius:18px;overflow:hidden;box-shadow:0 18px 48px rgba(15,23,42,.08);'>
<tr><td style='padding:24px 30px;background:linear-gradient(135deg,#0b2b59,#1768d4);color:#fff;'><div style='font-size:25px;font-weight:800;'>ProfitNx CRM</div><div style='margin-top:5px;font-size:13px;opacity:.86;'>Customer Training Feedback Response</div></td></tr>
<tr><td style='padding:30px;'>
<p style='margin:0 0 14px;'>Dear <strong>{Html(recipientName)}</strong>,</p>
<h1 style='font-size:22px;color:#123b6d;margin:0 0 10px;'>Customer feedback has been received</h1>
<p style='color:#53677f;line-height:1.7;margin:0 0 20px;'>The customer submitted feedback for the completed ProfitNx implementation and training record.</p>
<div style='display:block;padding:18px;border-radius:14px;background:#fff7e8;border:1px solid #ffdfaa;text-align:center;margin-bottom:22px;'>
<div style='font-size:30px;color:#f59e0b;letter-spacing:4px;'>{stars}</div><div style='margin-top:8px;font-size:26px;font-weight:800;color:#b86812;'>{implementation.FeedbackRating}/5</div><div style='margin-top:4px;font-weight:700;color:#72520d;'>Performance: {Html(performance)}</div></div>
<table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-bottom:22px;'>
{Row("Company Name", Value(implementation.FirmName))}
{Row("Customer / Person", Value(implementation.PersonName))}
{Row("License Number", Value(implementation.LicenseNumber))}
{Row("City", Value(implementation.City))}
{Row("Training Executive", Value(implementation.AssignedMemberName))}
{Row("Training Completed", implementation.CompletionDate?.ToString("dd/MM/yyyy") ?? "N/A")}
{Row("Feedback Date", implementation.FeedbackDate?.ToString("dd/MM/yyyy hh:mm tt") ?? "N/A")}
</table>
<h2 style='font-size:17px;color:#123b6d;margin:24px 0 10px;'>Customer Remarks</h2>
<div style='padding:15px 17px;background:#f8fbff;border:1px solid #e2ebf5;border-radius:12px;color:#40546f;line-height:1.7;'>{Html(Value(implementation.FeedbackRemarks))}</div>
<h2 style='font-size:17px;color:#123b6d;margin:24px 0 10px;'>Training Topics Sent for Reference</h2>
<div style='padding:15px 17px;background:#f8fbff;border:1px solid #e2ebf5;border-radius:12px;'>{topicsHtml}</div>
<p style='margin-top:24px;color:#53677f;line-height:1.7;'>Open the Implementation &amp; Training record in CRM to review the complete activity history and take any required follow-up action.</p>
<p style='margin-top:22px;'>Regards,<br/><strong>ProfitNx CRM</strong></p>
</td></tr>
<tr><td style='padding:15px 30px;background:#0b203f;color:#d9e7fb;font-size:12px;text-align:center;'>ProfitNx Business Software · Customer Success Notification</td></tr>
</table></td></tr></table></body></html>";
    }

    private static string BuildImplementationPlainText(string title, string recipientName, ImplementationCase item, string note)
        => $@"Profit Nx Implementation Notification

Dear {recipientName},

{title}

{note}

Customer: {Value(item.FirmName)}
Contact Person: {Value(item.PersonName)}
Mobile: {Value(item.Mobile)}
City: {Value(item.City)}
Product: {Value(item.ProductName)}
License: {Value(item.LicenseNumber)}
Assigned Member: {Value(item.AssignedMemberName)}
Stage: {Value(item.Stage)}
Status: {Value(item.Status)}
Progress: {item.ProgressPercent}%

Regards,
Profit Nx Team";

    private static string BuildImplementationEmailHtml(string title, string recipientName, ImplementationCase item, string note, string actionUrl)
    {
        var action = string.IsNullOrWhiteSpace(actionUrl) ? string.Empty : $"<p style='text-align:center;margin:24px 0;'><a href='{Html(actionUrl)}' style='display:inline-block;background:#2563eb;color:#fff;text-decoration:none;padding:12px 22px;border-radius:8px;font-weight:700;'>Open in CRM</a></p>";
        return $@"<!DOCTYPE html><html><body style='margin:0;background:#f4f7fb;font-family:Arial,Helvetica,sans-serif;color:#172033;'>
<table width='100%' cellpadding='0' cellspacing='0' style='padding:28px 12px;background:#f4f7fb;'><tr><td align='center'>
<table width='700' cellpadding='0' cellspacing='0' style='max-width:700px;background:#fff;border:1px solid #dce3ed;border-radius:14px;overflow:hidden;'>
<tr><td style='background:#102a43;color:#fff;padding:24px 30px;'><div style='font-size:25px;font-weight:800;'>Profit Nx</div><div style='font-size:13px;opacity:.8;margin-top:4px;'>Customer Onboarding Automation</div></td></tr>
<tr><td style='padding:30px;'><p>Dear {Html(recipientName)},</p><h2 style='color:#102a43;margin:0 0 12px;'>{Html(title)}</h2><div style='background:#eef4ff;border-left:4px solid #2563eb;padding:14px 16px;border-radius:8px;white-space:pre-line;'>{Html(note)}</div>
<table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin:20px 0;'>{Row("Customer", Value(item.FirmName))}{Row("Contact Person", Value(item.PersonName))}{Row("Mobile", Value(item.Mobile))}{Row("City", Value(item.City))}{Row("Product", Value(item.ProductName))}{Row("License", Value(item.LicenseNumber))}{Row("Assigned Member", Value(item.AssignedMemberName))}{Row("Stage", Value(item.Stage))}{Row("Status", Value(item.Status))}{Row("Progress", item.ProgressPercent + "%")}</table>{action}<p>Regards,<br><strong>Profit Nx Team</strong></p></td></tr></table>
</td></tr></table></body></html>";
    }

    public async Task MarkAsReadAsync(IEnumerable<string> ids, string readerUserId, string readerName, string readerRole)
    {
        var idSet = ids.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0) return;
        var ownLogs = await GetForUserAsync(readerUserId, readerName, readerRole);
        var rows = ownLogs.Where(x => idSet.Contains(x.Id) && x.Channel.Equals("CRM", StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                x.Status = "Read";
                x.ReadDate = DateTime.Now;
                return (x.Id, ToRow(x));
            }).ToList();
        if (rows.Count > 0) await _googleSheetsService.UpsertRowsByIdAsync("Notifications", rows);
    }

    public Task DeleteAsync(IEnumerable<string> ids)
        => _googleSheetsService.DeleteRowsByIdAsync("Notifications", ids);

    public Task ClearAllAsync()
        => _googleSheetsService.ClearAsync(ClearRange);

    // FIX (2026-08-17): SentDate/ReadDate are now apostrophe-guarded (see
    // SheetValueHelper.ToSheetText) so Google Sheets can never silently
    // re-interpret/reformat them into something GetDateTime fails to parse.
    private static IList<object> ToRow(NotificationLog x) => new List<object>
    {
        x.Id, SheetValueHelper.ToSheetText(x.SentDate.ToString("yyyy-MM-dd HH:mm:ss")), x.Channel, x.Recipient, x.Message,
        x.RelatedEntityType, x.RelatedEntityId, x.Status, x.ActionUrl, x.FlowType,
        x.RecipientUserId, x.SenderUserId, x.SenderName,
        x.ReadDate.HasValue ? SheetValueHelper.ToSheetText(x.ReadDate.Value.ToString("yyyy-MM-dd HH:mm:ss")) : string.Empty
    };

    private static string NormalizeRecipient(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task AppendLogAsync(string channel, string recipient, string message, string entityType, string entityId, string status, string actionUrl, string flowType)
    {
        channel = (channel ?? string.Empty).Trim();
        recipient = (recipient ?? string.Empty).Trim();
        message = (message ?? string.Empty).Trim();
        entityType = (entityType ?? string.Empty).Trim();
        entityId = (entityId ?? string.Empty).Trim();
        flowType = (flowType ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(message))
            return;

        if (channel.Equals("CRM", StringComparison.OrdinalIgnoreCase))
        {
            status = string.Equals(status, "Read", StringComparison.OrdinalIgnoreCase) ? "Read" : "Unread";
            if (string.IsNullOrWhiteSpace(actionUrl) && entityType.Equals("Inquiry", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entityId))
                actionUrl = $"/Inquiry/Details/{Uri.EscapeDataString(entityId)}";
        }

        // Stop accidental double-click, retry and overlapping recipient-flow calls
        // from writing the same notification more than once.
        var now = DateTime.UtcNow;
        foreach (var stale in RecentNotificationKeys.Where(x => now - x.Value > DuplicateWindow).Select(x => x.Key).ToList())
            RecentNotificationKeys.TryRemove(stale, out _);

        var messageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)));
        var duplicateKey = string.Join("|", channel, recipient, entityType, entityId, flowType, messageHash);
        if (RecentNotificationKeys.TryGetValue(duplicateKey, out var lastCreated) && now - lastCreated <= DuplicateWindow)
            return;

        RecentNotificationKeys[duplicateKey] = now;
        await NotificationMutationLock.WaitAsync();
        try
        {
            // Also check persisted rows. This protects against duplicate requests after
            // an application recycle or when more than one web worker is running.
            var duplicateSince = DateTime.Now.Subtract(DuplicateWindow);
            var persistedDuplicate = (await GetLogsAsync())
                .Take(250)
                .Any(x => x.SentDate >= duplicateSince
                    && x.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)
                    && x.Recipient.Equals(recipient, StringComparison.OrdinalIgnoreCase)
                    && x.RelatedEntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase)
                    && x.RelatedEntityId.Equals(entityId, StringComparison.OrdinalIgnoreCase)
                    && x.FlowType.Equals(flowType, StringComparison.OrdinalIgnoreCase)
                    && x.Message.Equals(message, StringComparison.Ordinal));
            if (persistedDuplicate) return;

            var allUsers = await _userService.GetAllUsersAsync();
            var normalizedRecipient = NormalizeRecipient(recipient);
            var recipientUser = allUsers.FirstOrDefault(x =>
                NormalizeRecipient(x.Id) == normalizedRecipient ||
                NormalizeRecipient(x.FullName) == normalizedRecipient ||
                NormalizeRecipient(x.Username) == normalizedRecipient ||
                NormalizeRecipient(x.Email) == normalizedRecipient ||
                NormalizeRecipient(x.Mobile) == normalizedRecipient);
            var principal = _httpContextAccessor.HttpContext?.User;
            var senderUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var senderName = principal?.Identity?.Name ?? "ProfitNx CRM";
            var log = new NotificationLog
            {
                Id = Guid.NewGuid().ToString(), SentDate = DateTime.Now, Channel = channel, Recipient = recipient,
                Message = message, RelatedEntityType = entityType, RelatedEntityId = entityId, Status = status,
                ActionUrl = actionUrl, FlowType = flowType, RecipientUserId = recipientUser?.Id ?? string.Empty,
                SenderUserId = senderUserId, SenderName = senderName
            };
            await _googleSheetsService.AppendAsync("Notifications", ToRow(log));

            // Web Push (closed-tab / background) — fire-and-forget after CRM log is stored.
            if (channel.Equals("CRM", StringComparison.OrdinalIgnoreCase) && _webPushService.IsConfigured)
            {
                var pushTitle = "ProfitNx CRM";
                var pushBody = message.Length > 180 ? message[..177] + "…" : message;
                var pushUrl = string.IsNullOrWhiteSpace(actionUrl) ? "/Notifications" : actionUrl;
                var pushTag = "profitnx-crm-" + (string.IsNullOrWhiteSpace(entityId) ? log.Id : entityId);
                var targetUserIds = new List<string>();
                if (!string.IsNullOrWhiteSpace(log.RecipientUserId))
                {
                    targetUserIds.Add(log.RecipientUserId);
                }
                else if (recipient.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                      || recipient.Equals("Admin Team", StringComparison.OrdinalIgnoreCase)
                      || recipient.StartsWith("Admin", StringComparison.OrdinalIgnoreCase))
                {
                    targetUserIds.AddRange(allUsers
                        .Where(x => x.IsActive && x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Id));
                }

                if (targetUserIds.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _webPushService.SendToUsersAsync(targetUserIds, pushTitle, pushBody, pushUrl, pushTag);
                        }
                        catch
                        {
                            // Never fail the CRM write path because push delivery failed.
                        }
                    });
                }
            }
        }
        catch
        {
            RecentNotificationKeys.TryRemove(duplicateKey, out _);
            throw;
        }
        finally
        {
            NotificationMutationLock.Release();
        }
    }

    private static string BuildCrmMessage(string title, Inquiry inquiry, string recipientName, string updateNote, string? forwardedToName = null)
    {
        var forwardedTo = string.IsNullOrWhiteSpace(forwardedToName) ? GetForwardedToDisplay(inquiry) : forwardedToName.Trim();
        return $@"Profit Nx Inquiry Notification

Dear {recipientName},

{title}

Latest Update:
{Value(updateNote)}

Inquiry Details:
Customer: {Value(inquiry.FirmName)}
Contact Person: {Value(inquiry.PersonName)}
City: {Value(inquiry.City)}
Mobile: {Value(inquiry.Mobile1)}
Inquiry Date: {FormatDate(inquiry.CreatedDate)}
Product: {Value(inquiry.ProductName)}
Latest Status: {Value(inquiry.Status)}
Genuine Status: {Value(inquiry.InquiryQuality)}
Submitted By: {Value(string.IsNullOrWhiteSpace(inquiry.SupportExecutiveName) ? inquiry.AssignedUserName : inquiry.SupportExecutiveName)}
Forwarded To: {Value(forwardedTo)}
Sold By: {Value(inquiry.SoldByName)} {(!string.IsNullOrWhiteSpace(inquiry.SoldByRole) ? "(" + inquiry.SoldByRole + ")" : string.Empty)}

Regards,
Profit Nx Team";
    }

    private static string BuildWhatsAppMessage(string title, Inquiry inquiry, string recipientName, string updateNote, string? forwardedToName = null)
    {
        var forwardedTo = string.IsNullOrWhiteSpace(forwardedToName) ? GetForwardedToDisplay(inquiry) : forwardedToName.Trim();
        return $@"Dear {recipientName},

{title}

Latest Update:
{Value(updateNote)}

Inquiry Details:
Customer: {Value(inquiry.FirmName)}
Contact Person: {Value(inquiry.PersonName)}
City: {Value(inquiry.City)}
Mobile: {Value(inquiry.Mobile1)}
Inquiry Date: {FormatDate(inquiry.CreatedDate)}
Product: {Value(inquiry.ProductName)}
Latest Status: {Value(inquiry.Status)}
Genuine Status: {Value(inquiry.InquiryQuality)}
Submitted By: {Value(string.IsNullOrWhiteSpace(inquiry.SupportExecutiveName) ? inquiry.AssignedUserName : inquiry.SupportExecutiveName)}
Forwarded To: {Value(forwardedTo)}
Sold By: {Value(inquiry.SoldByName)} {(!string.IsNullOrWhiteSpace(inquiry.SoldByRole) ? "(" + inquiry.SoldByRole + ")" : string.Empty)}

Regards,
Profit Nx Team";
    }

    private static string BuildEmailHtml(string title, string recipientName, Inquiry inquiry, string noteTitle, string note, string footerLine, string? forwardedToName = null)
    {
        var forwardedTo = string.IsNullOrWhiteSpace(forwardedToName) ? GetForwardedToDisplay(inquiry) : forwardedToName.Trim();
        var statusColor = GetStatusColor(inquiry.Status);
        var qualityColor = inquiry.InquiryQuality == "Genuine" ? "#16a34a" : inquiry.InquiryQuality == "Not Genuine" ? "#dc2626" : "#64748b";
        return $@"<!DOCTYPE html><html><body style='margin:0;padding:0;background:#f3f6fb;font-family:Arial,Helvetica,sans-serif;color:#102a43;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background:#f3f6fb;padding:24px 0;'><tr><td align='center'>
<table width='720' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #dbe6f5;'>
<tr><td style='background:linear-gradient(135deg,#0b2545,#1d4ed8);padding:24px;color:#ffffff;'><div style='font-size:26px;font-weight:800;'>Profit Nx</div><div style='font-size:14px;opacity:.9;margin-top:4px;'>Inquiry Notification</div></td></tr>
<tr><td style='padding:28px;'><p style='font-size:16px;margin:0 0 14px;'>Dear {Html(recipientName)},</p><h2 style='margin:0 0 10px;font-size:24px;color:#0b2545;'>{Html(title)}</h2><p style='font-size:15px;line-height:1.6;color:#52657a;margin:0 0 22px;'>{Html(footerLine)}</p>
<div style='background:#eef6ff;border-left:5px solid #2563eb;border-radius:12px;padding:16px;margin:18px 0;'><div style='font-weight:800;color:#0b2545;margin-bottom:6px;'>{Html(noteTitle)}</div><div style='font-size:16px;line-height:1.6;font-weight:700;color:#102a43;'>{Html(Value(note))}</div></div>
<table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-top:12px;'>
{Row("Customer Name", Value(inquiry.FirmName))}{Row("Contact Person", Value(inquiry.PersonName))}{Row("City", Value(inquiry.City))}{Row("Mobile", Value(inquiry.Mobile1))}{Row("Inquiry Date", FormatDate(inquiry.CreatedDate))}{Row("Submitted By", Value(string.IsNullOrWhiteSpace(inquiry.SupportExecutiveName) ? inquiry.AssignedUserName : inquiry.SupportExecutiveName))}{Row("Forwarded To", Value(forwardedTo))}{Row("Product", Value(inquiry.ProductName))}
<tr><td style='padding:12px;border:1px solid #e4edf7;background:#f8fbff;font-weight:bold;width:35%;'>Genuine Status</td><td style='padding:12px;border:1px solid #e4edf7;'><span style='display:inline-block;padding:7px 14px;border-radius:999px;background:{qualityColor};color:#ffffff;font-weight:bold;'>{Html(Value(inquiry.InquiryQuality))}</span></td></tr>
<tr><td style='padding:12px;border:1px solid #e4edf7;background:#f8fbff;font-weight:bold;'>Latest Status</td><td style='padding:12px;border:1px solid #e4edf7;'><span style='display:inline-block;padding:7px 14px;border-radius:999px;background:{statusColor};color:#ffffff;font-weight:bold;'>{Html(Value(inquiry.Status))}</span></td></tr>
{Row("Sold By", Value(string.Join(" ", new []{ inquiry.SoldByName, string.IsNullOrWhiteSpace(inquiry.SoldByRole) ? string.Empty : "(" + inquiry.SoldByRole + ")" }.Where(x => !string.IsNullOrWhiteSpace(x)))))}
</table><p style='font-size:15px;line-height:1.6;color:#52657a;margin-top:24px;'>This notification is related to the same inquiry. Please keep this email for reference.</p><p style='font-size:15px;margin-top:22px;'>Regards,<br><strong>Profit Nx Team</strong></p></td></tr>
<tr><td style='background:#f8fbff;padding:16px 28px;color:#6b7c93;font-size:12px;border-top:1px solid #e4edf7;'>This is an automated notification from Profit Nx.</td></tr>
</table></td></tr></table></body></html>";
    }

    private static string Row(string label, string value) => $"<tr><td style='padding:12px;border:1px solid #e4edf7;background:#f8fbff;font-weight:bold;width:35%;'>{Html(label)}</td><td style='padding:12px;border:1px solid #e4edf7;'>{Html(value)}</td></tr>";

    private static string BuildSubject(string title, Inquiry inquiry)
    {
        var customer = string.IsNullOrWhiteSpace(inquiry.FirmName) ? "Customer" : inquiry.FirmName.Trim();
        var shortId = string.IsNullOrWhiteSpace(inquiry.Id) ? string.Empty : $" #{inquiry.Id[..Math.Min(8, inquiry.Id.Length)]}";
        return $"Re: {title} - {customer}{shortId}";
    }

    private static string GetStatusColor(string? status)
    {
        status = status?.Trim().ToLowerInvariant();
        return status switch
        {
            "sold" => "#16a34a",
            "close" => "#dc2626",
            "closed" => "#dc2626",
            "follow up" => "#f97316",
            "demo scheduled" => "#2563eb",
            "demo done" => "#7c3aed",
            "negotiation" => "#0891b2",
            _ => "#475569"
        };
    }

    private static string CleanName(string? name) => string.IsNullOrWhiteSpace(name) ? "Sir/Madam" : name.Trim();
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim();
    private static string FormatDate(DateTime date) => date == default ? "N/A" : date.ToString("dd/MM/yyyy");
    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</p>|</div>|</tr>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<.*?>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"[ \t]+", " ").Replace("\n ", "\n").Trim();
    }

    public async Task NotifyUserLoginAsync(string userId, string fullName, string userName, string role, string deviceSummary, string ipAddress)
    {
        var users = await _userService.GetAllUsersAsync();
        var admins = users.Where(x => x.IsActive && x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)).ToList();
        var when = DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");
        var message = $"User Login Alert\n{fullName} ({userName}) — Role: {role}\nTime: {when}\nDevice: {deviceSummary}\nIP: {ipAddress}";
        foreach (var admin in admins)
        {
            if (admin.Id.Equals(userId, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsCrmEnabled())
                await AppendLogAsync("CRM", string.IsNullOrWhiteSpace(admin.Id) ? admin.FullName : admin.Id, message, "Login", userId, "Unread", "/Admin/LoginReport", "UserLogin");
        }
    }

    // ADDED (2026-08-17): "koi pan user login/logout kare tyare aave" - Admin was
    // only ever alerted on login, never on logout. Mirrors NotifyUserLoginAsync
    // exactly (same admin scoping, same CRM-only delivery, same de-dupe via
    // AppendLogAsync) with its own "UserLogout" flowType so it rides the exact
    // same 20-minute "live heads-up only" window as login alerts everywhere that
    // already special-cases "UserLogin" (see NotificationsController + layout JS).
    public async Task NotifyUserLogoutAsync(string userId, string fullName, string userName, string role, string deviceSummary, string ipAddress)
    {
        var users = await _userService.GetAllUsersAsync();
        var admins = users.Where(x => x.IsActive && x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)).ToList();
        var when = DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");
        var message = $"User Logout Alert\n{fullName} ({userName}) — Role: {role}\nTime: {when}\nDevice: {deviceSummary}\nIP: {ipAddress}";
        foreach (var admin in admins)
        {
            if (admin.Id.Equals(userId, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsCrmEnabled())
                await AppendLogAsync("CRM", string.IsNullOrWhiteSpace(admin.Id) ? admin.FullName : admin.Id, message, "Login", userId, "Unread", "/Admin/LoginReport", "UserLogout");
        }
    }

    // ── Low Stock Alert ─────────────────────────────────────────────────────────
    // Whenever a partner's available stock drops to/below the reorder qty that
    // Admin set for that stock row, notify BOTH that partner and every Admin —
    // so Admin knows exactly which partner has run low and needs a new order.
    public async Task NotifyLowStockAsync(StockItem stock)
    {
        if (stock == null) return;
        // Only alert when Admin has actually configured a reorder level and the
        // available balance has fallen to/below it.
        if (stock.ReorderLevel <= 0 || stock.AvailableStock > stock.ReorderLevel) return;

        var allUsers = await _userService.GetAllUsersAsync();
        var admins = allUsers.Where(x => x.IsActive && x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)).ToList();
        var partnerUser = allUsers.FirstOrDefault(x =>
            x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase) &&
            ((!string.IsNullOrWhiteSpace(stock.PartnerId) && (x.Id.Equals(stock.PartnerId, StringComparison.OrdinalIgnoreCase) || x.PartnerCode.Equals(stock.PartnerId, StringComparison.OrdinalIgnoreCase)))
             || (!string.IsNullOrWhiteSpace(stock.PartnerName) && x.FullName.Equals(stock.PartnerName, StringComparison.OrdinalIgnoreCase))));

        var productLabel = string.IsNullOrWhiteSpace(stock.Version) ? Value(stock.ProductName) : $"{Value(stock.ProductName)} ({stock.Version})";
        var partnerLabel = string.IsNullOrWhiteSpace(stock.PartnerName) ? "Unassigned Partner" : stock.PartnerName;
        var statusWord = stock.AvailableStock <= 0 ? "OUT OF STOCK" : "LOW STOCK";
        var bodyLine = $"{statusWord}: {productLabel} for partner {partnerLabel} is at {stock.AvailableStock} unit(s) available — at/below the reorder level of {stock.ReorderLevel}. Please place a new stock order.";
        var subject = $"Low Stock Alert - {productLabel} ({partnerLabel})";
        var adminActionUrl = $"/Stock/Edit/{Uri.EscapeDataString(stock.Id)}";
        // Partners can't open the Edit screen (Admin-only), so point them at their own Stock list instead.
        const string partnerActionUrl = "/Stock/Index";

        foreach (var admin in admins)
        {
            var adminName = CleanName(string.IsNullOrWhiteSpace(admin.FullName) ? admin.Username : admin.FullName);
            var crmMsg = $"Dear {adminName},\n\n{bodyLine}";
            if (IsCrmEnabled())
                await AppendLogAsync("CRM", adminName, crmMsg, "Stock", stock.Id, "Unread", adminActionUrl, "LowStockAlert");

            if (!string.IsNullOrWhiteSpace(admin.Mobile) && IsWhatsAppEnabled())
            {
                var (channel, url, status) = BuildWhatsAppAction(admin.Mobile, crmMsg);
                await AppendLogAsync(channel, admin.Mobile, crmMsg, "Stock", stock.Id, string.IsNullOrWhiteSpace(url) ? "No mobile" : status, url, "LowStockAlert");
            }

            if (!string.IsNullOrWhiteSpace(admin.Email) && IsEmailEnabled())
            {
                var html = $"<p>Dear {Html(adminName)},</p><p>{Html(bodyLine)}</p>";
                var sent = await TrySendEmailAsync(admin.Email, subject, html, true);
                await AppendLogAsync("Email", admin.Email, ToPlainText(html), "Stock", stock.Id, sent ? "Sent" : "SMTP not configured", string.Empty, "LowStockAlert");
            }
        }

        if (partnerUser != null)
        {
            var partnerDisplayName = CleanName(string.IsNullOrWhiteSpace(partnerUser.FullName) ? partnerUser.Username : partnerUser.FullName);
            var crmMsg = $"Dear {partnerDisplayName},\n\n{bodyLine}";
            if (IsCrmEnabled())
                await AppendLogAsync("CRM", partnerDisplayName, crmMsg, "Stock", stock.Id, "Unread", partnerActionUrl, "LowStockAlert");

            if (!string.IsNullOrWhiteSpace(partnerUser.Mobile) && IsWhatsAppEnabled())
            {
                var (channel, url, status) = BuildWhatsAppAction(partnerUser.Mobile, crmMsg);
                await AppendLogAsync(channel, partnerUser.Mobile, crmMsg, "Stock", stock.Id, string.IsNullOrWhiteSpace(url) ? "No mobile" : status, url, "LowStockAlert");
            }

            if (!string.IsNullOrWhiteSpace(partnerUser.Email) && IsEmailEnabled())
            {
                var html = $"<p>Dear {Html(partnerDisplayName)},</p><p>{Html(bodyLine)}</p>";
                var sent = await TrySendEmailAsync(partnerUser.Email, subject, html, true);
                await AppendLogAsync("Email", partnerUser.Email, ToPlainText(html), "Stock", stock.Id, sent ? "Sent" : "SMTP not configured", string.Empty, "LowStockAlert");
            }
        }
    }

    public async Task<(bool EmailSent, string WhatsAppUrl, string Status)> SendInquiryDocumentsAsync(
        Inquiry inquiry,
        string sendType,
        string channel,
        IEnumerable<string> attachmentPaths,
        string? customMessage,
        AppUser? senderUser)
    {
        var firm = string.IsNullOrWhiteSpace(inquiry.FirmName) ? "Customer" : inquiry.FirmName;
        var person = string.IsNullOrWhiteSpace(inquiry.PersonName) ? firm : inquiry.PersonName;
        var subject = sendType switch
        {
            "Quotation" => $"Quotation — {firm} | Profit Nx",
            "PaymentPack" => $"Payment Details — {firm} | Profit Nx",
            "Brochure" => $"Product Brochure — {firm} | Profit Nx",
            "Resend" => $"Documents (Resend) — {firm} | Profit Nx",
            _ => $"Profit Nx Documents — {firm}"
        };

        var defaultMsg = sendType switch
        {
            "Quotation" => $"Dear {person},\n\nPlease find the quotation for your inquiry regarding {inquiry.ProductName ?? "Profit Nx"}.\n\nFeel free to contact us for any clarification.\n\nRegards,\nProfit Nx Team",
            "PaymentPack" => $"Dear {person},\n\nAs requested, please find the price list / bank details / UPI information for payment.\n\nRegards,\nProfit Nx Team",
            "Brochure" => $"Dear {person},\n\nPlease find the product brochure attached for your reference.\n\nRegards,\nProfit Nx Team",
            _ => $"Dear {person},\n\nPlease find the attached documents related to your inquiry.\n\nRegards,\nProfit Nx Team"
        };
        var bodyText = string.IsNullOrWhiteSpace(customMessage) ? defaultMsg : customMessage.Trim();
        var encoded = System.Net.WebUtility.HtmlEncode(bodyText).Replace("\n", "<br/>");
        var html = $"<div style='font-family:Arial,sans-serif;color:#172033;line-height:1.5'><p>{encoded}</p><p style='color:#64748b;font-size:12px'>Inquiry: {System.Net.WebUtility.HtmlEncode(firm)} · Status: {System.Net.WebUtility.HtmlEncode(inquiry.Status)}</p></div>";

        var paths = (attachmentPaths ?? Array.Empty<string>()).Where(System.IO.File.Exists).Distinct().ToList();
        var channelNorm = (channel ?? "Both").Trim();
        var emailSent = false;
        var waUrl = string.Empty;
        var statusParts = new List<string>();

        if ((channelNorm.Equals("Email", StringComparison.OrdinalIgnoreCase) || channelNorm.Equals("Both", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(inquiry.Email1))
        {
            emailSent = await TrySendEmailWithAttachmentsAsync(inquiry.Email1, subject, html, true, paths, senderUser);
            statusParts.Add(emailSent ? "Email Sent" : "Email failed / SMTP not configured");
            await AppendLogAsync("Email", inquiry.Email1, bodyText, "Inquiry", inquiry.Id, emailSent ? "Sent" : "SMTP not configured", string.Empty, "InquiryDocuments");
        }
        else if (channelNorm.Equals("Email", StringComparison.OrdinalIgnoreCase))
        {
            statusParts.Add("No email on inquiry");
        }

        if (channelNorm.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase) || channelNorm.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            var mobile = !string.IsNullOrWhiteSpace(inquiry.Mobile1) ? inquiry.Mobile1 : inquiry.Mobile2;
            var waMsg = bodyText;
            if (paths.Count > 0)
                waMsg += "\n\n(Documents also sent on email if configured. Please check email for attachments.)";
            var (ch, url, st) = BuildWhatsAppAction(mobile, waMsg);
            waUrl = url ?? string.Empty;
            statusParts.Add(string.IsNullOrWhiteSpace(url) ? "No mobile" : st);
            if (!string.IsNullOrWhiteSpace(url))
                await AppendLogAsync(ch, mobile ?? string.Empty, waMsg, "Inquiry", inquiry.Id, st, url, "InquiryDocuments");
        }

        return (emailSent, waUrl, string.Join(" · ", statusParts));
    }

    private async Task<bool> TrySendEmailWithAttachmentsAsync(string to, string subject, string body, bool isHtml, List<string> attachmentPaths, AppUser? senderUser = null)
    {
        var host = !string.IsNullOrWhiteSpace(senderUser?.SmtpHost) ? senderUser!.SmtpHost : _configuration["Smtp:Host"];
        var from = !string.IsNullOrWhiteSpace(senderUser?.Email) ? senderUser!.Email : _configuration["Smtp:From"];
        var user = !string.IsNullOrWhiteSpace(senderUser?.SmtpUsername) ? senderUser!.SmtpUsername : _configuration["Smtp:Username"];
        var pass = !string.IsNullOrWhiteSpace(senderUser?.SmtpPassword) ? senderUser!.SmtpPassword : _configuration["Smtp:Password"];
        var enableSsl = senderUser == null ? true : senderUser.SmtpEnableSsl;
        var port = senderUser != null && senderUser.SmtpPort > 0 ? senderUser.SmtpPort : (int.TryParse(_configuration["Smtp:Port"], out var cfgPort) ? cfgPort : 587);
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return false;

        try
        {
            using var client = new SmtpClient(host)
            {
                Port = port,
                EnableSsl = enableSsl,
                UseDefaultCredentials = false,
                Credentials = string.IsNullOrWhiteSpace(user) ? CredentialCache.DefaultNetworkCredentials : new NetworkCredential(user, pass?.Replace(" ", ""))
            };
            using var mail = new MailMessage
            {
                From = new MailAddress(from, "Profit Nx Team"),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            mail.To.Add(to);
            foreach (var path in attachmentPaths)
            {
                try { mail.Attachments.Add(new Attachment(path)); } catch { /* skip bad file */ }
            }
            await client.SendMailAsync(mail);
            return true;
        }
        catch { return false; }
    }

}
