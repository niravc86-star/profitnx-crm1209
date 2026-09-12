using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using System.Security.Claims;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class ChatController : Controller
{
    private readonly IChatService _chatService;
    private readonly IPermissionService _permissionService;
    private readonly IWebHostEnvironment _environment;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".doc", ".docx",
        ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar", ".txt", ".csv"
    };
    private const long MaxAttachmentBytes = 15 * 1024 * 1024; // 15 MB

    public ChatController(IChatService chatService, IPermissionService permissionService, IWebHostEnvironment environment)
    {
        _chatService = chatService;
        _permissionService = permissionService;
        _environment = environment;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentUserName => User.Identity?.Name ?? string.Empty;
    private string CurrentUserRole => User.FindFirstValue(ClaimTypes.Role) ?? "User";

    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return RedirectToAction("AccessDenied", "Account");

        var canViewAll = await _permissionService.HasPermissionAsync(User, "chat.viewall");
        _chatService.TouchPresence(CurrentUserId);
        ViewBag.CanViewAll = canViewAll;
        ViewBag.CurrentUserId = CurrentUserId;
        ViewBag.CurrentUserName = CurrentUserName;
        var contacts = await _chatService.GetContactsAsync(CurrentUserId, canViewAll);
        return View(contacts);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Contacts()
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();

        _chatService.TouchPresence(CurrentUserId);
        var canViewAll = await _permissionService.HasPermissionAsync(User, "chat.viewall");
        var contacts = await _chatService.GetContactsAsync(CurrentUserId, canViewAll);
        return Json(contacts);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Messages(string withUserId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(withUserId)) return BadRequest();

        _chatService.TouchPresence(CurrentUserId);
        var messages = await _chatService.GetConversationAsync(CurrentUserId, withUserId, CurrentUserId);
        await _chatService.MarkConversationReadAsync(CurrentUserId, withUserId);
        return Json(messages.Select(m => ToMessageJson(m)));
    }

    // Shared JSON shape for a message bubble - used by Messages() and returned
    // fresh (with reply/reactions/star/pin already resolved) by every action
    // endpoint below so chat.js never has to re-derive that state itself.
    private object ToMessageJson(ChatMessage m) => new
    {
        m.Id,
        sentDate = m.SentDate.ToString("dd/MM/yyyy hh:mm tt"),
        m.SenderId,
        m.SenderName,
        m.Message,
        attachmentUrl = m.HasAttachment ? Url.Content(m.AttachmentPath) : null,
        m.AttachmentName,
        isMine = m.SenderId.Equals(CurrentUserId, StringComparison.OrdinalIgnoreCase),
        replyToMessageId = m.ReplyToMessageId,
        replyToSenderName = m.ReplyToSenderName,
        replyToPreview = m.ReplyToPreview,
        isForwarded = m.IsForwarded,
        reactions = m.Reactions(),
        isStarredByMe = m.StarredBy().Contains(CurrentUserId, StringComparer.OrdinalIgnoreCase),
        isPinned = m.IsPinned
    };

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Send(string receiverId, string receiverName, string? message, IFormFile? attachment,
        string? replyToMessageId, bool isForwarded = false,
        string? forwardAttachmentPath = null, string? forwardAttachmentName = null, long forwardAttachmentSize = 0)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(receiverId))
            return BadRequest("Receiver is required.");
        if (string.IsNullOrWhiteSpace(message) && (attachment == null || attachment.Length == 0) && string.IsNullOrWhiteSpace(forwardAttachmentPath))
            return BadRequest("Type a message or attach a file.");

        string attachmentPath = string.Empty, attachmentName = string.Empty;
        long attachmentSize = 0;

        if (attachment != null && attachment.Length > 0)
        {
            var extension = Path.GetExtension(attachment.FileName);
            if (!AllowedExtensions.Contains(extension))
                return BadRequest("This file type is not allowed.");
            if (attachment.Length > MaxAttachmentBytes)
                return BadRequest("File is too large. Maximum size is 15 MB.");

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "chat");
            Directory.CreateDirectory(uploadsFolder);
            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadsFolder, storedFileName);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await attachment.CopyToAsync(stream);
            }
            attachmentPath = $"/uploads/chat/{storedFileName}";
            attachmentName = Path.GetFileName(attachment.FileName);
            attachmentSize = attachment.Length;
        }
        else if (isForwarded && !string.IsNullOrWhiteSpace(forwardAttachmentPath))
        {
            // Forwarding an existing attachment: point the new message at the same
            // stored file instead of re-uploading it. Only ever accept a path under
            // our own /uploads/chat/ folder for an existing file, so this can't be
            // used to leak or link to arbitrary files on the server.
            var safePath = forwardAttachmentPath.Replace('\\', '/');
            if (safePath.StartsWith("/uploads/chat/", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.Combine(_environment.WebRootPath, safePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fullPath))
                {
                    attachmentPath = safePath;
                    attachmentName = forwardAttachmentName ?? string.Empty;
                    attachmentSize = forwardAttachmentSize;
                }
            }
        }

        var saved = await _chatService.SendMessageAsync(
            CurrentUserId, CurrentUserName, CurrentUserRole,
            receiverId, receiverName ?? string.Empty, message ?? string.Empty,
            attachmentPath, attachmentName, attachmentSize,
            isForwarded ? null : replyToMessageId, isForwarded);

        return Json(ToMessageJson(saved));
    }

    // Toggle (add/change/remove) the current user's emoji reaction on a message.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> React(string messageId, string emoji)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(emoji))
            return BadRequest();

        var reactions = await _chatService.ToggleReactionAsync(messageId, CurrentUserId, emoji);
        return Json(new { success = true, reactions });
    }

    // Star / unstar a message - per person, same as WhatsApp (starring only
    // affects your own view, not the other participant's).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStar(string messageId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(messageId))
            return BadRequest();

        var starred = await _chatService.ToggleStarAsync(messageId, CurrentUserId);
        return Json(new { success = true, starred });
    }

    // Pin / unpin a message to the top of the conversation. Visible to both
    // participants (pinning a message pins it for the thread, not just you).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePin(string messageId, string otherUserId, bool pin)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(otherUserId))
            return BadRequest();

        var ok = await _chatService.SetPinAsync(messageId, CurrentUserId, otherUserId, pin);
        if (!ok) return NotFound();
        return Json(new { success = true, pinned = pin });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UnreadCount()
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Json(new { count = 0 });

        _chatService.TouchPresence(CurrentUserId);
        var count = await _chatService.GetUnreadCountAsync(CurrentUserId);
        return Json(new { count, hasUnread = count > 0 });
    }

    // Front-end (chat.js) reads this once on load to decide whether to show the
    // Clear Chat / Delete Chat controls - permission-gated per Role/User via
    // Role Wise Rights / User Wise Rights ("chat.clear", "chat.delete").
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Rights()
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Json(new { canClear = false, canDelete = false, canViewAll = false });

        var canClear = await _permissionService.HasPermissionAsync(User, "chat.clear");
        var canDelete = await _permissionService.HasPermissionAsync(User, "chat.delete");
        var canViewAll = await _permissionService.HasPermissionAsync(User, "chat.viewall");
        return Json(new { canClear, canDelete, canViewAll });
    }

    // "Clear Chat" (user wise): hides the conversation from the current user's
    // own view only. The other participant's copy, and the underlying
    // messages, are untouched.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearChat(string otherUserId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (!await _permissionService.HasPermissionAsync(User, "chat.clear"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(otherUserId))
            return BadRequest();

        await _chatService.ClearConversationAsync(CurrentUserId, otherUserId);
        return Json(new { success = true });
    }

    // "Delete Chat" - particular message. Deletes one message permanently
    // for everyone. Regular users may only delete a message they sent or
    // received; chat.viewall admins may delete any message.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMessage(string messageId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (!await _permissionService.HasPermissionAsync(User, "chat.delete"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(messageId))
            return BadRequest();

        var allowAny = await _permissionService.HasPermissionAsync(User, "chat.viewall");
        var ok = await _chatService.DeleteMessageAsync(messageId, CurrentUserId, allowAny);
        if (!ok) return NotFound();
        return Json(new { success = true });
    }

    // "Delete Chat" - delete all. Permanently deletes every message in the
    // conversation between the current user and otherUserId, for both sides.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChat(string otherUserId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.access"))
            return Forbid();
        if (!await _permissionService.HasPermissionAsync(User, "chat.delete"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(otherUserId))
            return BadRequest();

        var removed = await _chatService.DeleteConversationAsync(CurrentUserId, otherUserId);
        return Json(new { success = true, removed });
    }

    // Admin-only monitoring screen: every conversation happening in the CRM.
    public async Task<IActionResult> AllConversations()
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.viewall"))
            return RedirectToAction("AccessDenied", "Account");

        ViewBag.CanDelete = await _permissionService.HasPermissionAsync(User, "chat.delete");
        var threads = await _chatService.GetAllConversationsAsync();
        return View(threads);
    }

    // Admin monitoring: delete an entire conversation between any two users
    // (requires both chat.viewall and chat.delete).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminDeleteThread(string userAId, string userBId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.viewall"))
            return Forbid();
        if (!await _permissionService.HasPermissionAsync(User, "chat.delete"))
            return Forbid();
        if (string.IsNullOrWhiteSpace(userAId) || string.IsNullOrWhiteSpace(userBId))
            return BadRequest();

        var removed = await _chatService.DeleteConversationAsync(userAId, userBId);
        return Json(new { success = true, removed });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Thread(string userAId, string userBId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "chat.viewall"))
            return Forbid();

        var messages = await _chatService.GetConversationAsync(userAId, userBId);
        return Json(messages.Select(m => new
        {
            m.Id,
            sentDate = m.SentDate.ToString("dd/MM/yyyy hh:mm tt"),
            m.SenderName,
            m.Message,
            attachmentUrl = m.HasAttachment ? Url.Content(m.AttachmentPath) : null,
            m.AttachmentName
        }));
    }
}
