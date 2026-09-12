using System.Collections.Concurrent;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public class ChatService : IChatService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly IUserService _users;

    private const string ChatSheet = "ChatMessages";
    // EXTENDED (2026-08-18): columns N-T added for Reply/Forward/React/Star/Pin
    // (see Headers() / Map() / ToRow() below). Old rows simply read blank/false
    // for the new columns - fully backward compatible.
    private const string ChatReadRange = ChatSheet + "!A2:T";

    // "Clear Chat" markers - one row per (UserId, OtherUserId) pair that a user
    // has cleared. See ChatClear.cs / ClearConversationAsync for details.
    private const string ChatClearSheet = "ChatClears";
    private const string ChatClearReadRange = ChatClearSheet + "!A2:D";

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaReady;

    // In-memory only "online" presence. Not persisted - resets on app restart,
    // which is fine since it only drives the green/red dot in the contact list.
    private static readonly ConcurrentDictionary<string, DateTime> LastSeen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(45);

    public ChatService(IGoogleSheetsService sheets, IUserService users)
    {
        _sheets = sheets;
        _users = users;
    }

    private static IList<object> Headers() => new List<object>
    {
        "Id", "SentDate", "SenderId", "SenderName", "SenderRole",
        "ReceiverId", "ReceiverName", "Message", "AttachmentPath",
        "AttachmentName", "AttachmentSize", "IsRead", "ReadDate",
        "ReplyToMessageId", "ReplyToSenderName", "ReplyToPreview",
        "IsForwarded", "Reactions", "StarredBy", "IsPinned"
    };

    private static IList<object> ClearHeaders() => new List<object>
    {
        "Id", "UserId", "OtherUserId", "ClearedBeforeDate"
    };

    public async Task EnsureSchemaAsync()
    {
        if (_schemaReady) return;
        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaReady) return;
            await _sheets.EnsureSheetAsync(ChatSheet, Headers());
            await _sheets.EnsureSheetAsync(ChatClearSheet, ClearHeaders());
            _schemaReady = true;
        }
        finally { SchemaLock.Release(); }
    }

    private static ChatMessage Map(IList<object> row) => new()
    {
        Id = SheetValueHelper.GetString(row, 0),
        SentDate = SheetValueHelper.GetDateTime(row, 1) ?? DateTime.Now,
        SenderId = SheetValueHelper.GetString(row, 2),
        SenderName = SheetValueHelper.GetString(row, 3),
        SenderRole = SheetValueHelper.GetString(row, 4),
        ReceiverId = SheetValueHelper.GetString(row, 5),
        ReceiverName = SheetValueHelper.GetString(row, 6),
        Message = SheetValueHelper.GetString(row, 7),
        AttachmentPath = SheetValueHelper.GetString(row, 8),
        AttachmentName = SheetValueHelper.GetString(row, 9),
        AttachmentSize = long.TryParse(SheetValueHelper.GetString(row, 10), out var size) ? size : 0,
        IsRead = SheetValueHelper.GetBool(row, 11, false),
        ReadDate = SheetValueHelper.GetDateTime(row, 12),
        ReplyToMessageId = SheetValueHelper.GetString(row, 13),
        ReplyToSenderName = SheetValueHelper.GetString(row, 14),
        ReplyToPreview = SheetValueHelper.GetString(row, 15),
        IsForwarded = SheetValueHelper.GetBool(row, 16, false),
        ReactionsRaw = SheetValueHelper.GetString(row, 17),
        StarredByRaw = SheetValueHelper.GetString(row, 18),
        IsPinned = SheetValueHelper.GetBool(row, 19, false)
    };

    private static IList<object> ToRow(ChatMessage m) => new List<object>
    {
        m.Id, m.SentDate.ToString("yyyy-MM-dd HH:mm:ss"), m.SenderId, m.SenderName, m.SenderRole,
        m.ReceiverId, m.ReceiverName, m.Message, m.AttachmentPath, m.AttachmentName,
        m.AttachmentSize, m.IsRead, m.ReadDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        m.ReplyToMessageId, m.ReplyToSenderName, m.ReplyToPreview,
        m.IsForwarded, m.ReactionsRaw, m.StarredByRaw, m.IsPinned
    };

    private async Task<List<ChatMessage>> GetAllAsync()
    {
        await EnsureSchemaAsync();
        var rows = await _sheets.ReadAsync(ChatReadRange);
        return rows.Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0)))
            .Select(Map)
            .OrderBy(x => x.SentDate)
            .ToList();
    }

    private static ChatClear MapClear(IList<object> row) => new()
    {
        UserId = SheetValueHelper.GetString(row, 1),
        OtherUserId = SheetValueHelper.GetString(row, 2),
        ClearedBeforeDate = SheetValueHelper.GetDateTime(row, 3) ?? DateTime.MinValue
    };

    private static IList<object> ToClearRow(ChatClear c) => new List<object>
    {
        c.Id, c.UserId, c.OtherUserId, c.ClearedBeforeDate.ToString("yyyy-MM-dd HH:mm:ss")
    };

    private async Task<DateTime?> GetClearedBeforeAsync(string userId, string otherUserId)
    {
        await EnsureSchemaAsync();
        var rows = await _sheets.ReadAsync(ChatClearReadRange);
        var id = $"{userId}|{otherUserId}";
        var row = rows.FirstOrDefault(r => SheetValueHelper.GetString(r, 0).Equals(id, StringComparison.OrdinalIgnoreCase));
        if (row == null) return null;
        var cleared = MapClear(row).ClearedBeforeDate;
        return cleared == DateTime.MinValue ? null : cleared;
    }

    public async Task<List<ChatMessage>> GetConversationAsync(string userIdA, string userIdB, string? viewerUserId = null)
    {
        var all = await GetAllAsync();
        var thread = all.Where(x =>
                (x.SenderId.Equals(userIdA, StringComparison.OrdinalIgnoreCase) && x.ReceiverId.Equals(userIdB, StringComparison.OrdinalIgnoreCase)) ||
                (x.SenderId.Equals(userIdB, StringComparison.OrdinalIgnoreCase) && x.ReceiverId.Equals(userIdA, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.SentDate)
            .ToList();

        if (!string.IsNullOrWhiteSpace(viewerUserId))
        {
            var otherId = viewerUserId.Equals(userIdA, StringComparison.OrdinalIgnoreCase) ? userIdB : userIdA;
            var clearedBefore = await GetClearedBeforeAsync(viewerUserId, otherId);
            if (clearedBefore.HasValue)
                thread = thread.Where(x => x.SentDate > clearedBefore.Value).ToList();
        }

        return thread;
    }

    public async Task ClearConversationAsync(string currentUserId, string otherUserId)
    {
        await EnsureSchemaAsync();
        var clear = new ChatClear
        {
            UserId = currentUserId,
            OtherUserId = otherUserId,
            ClearedBeforeDate = DateTime.Now
        };
        await _sheets.UpsertRowByIdAsync(ChatClearSheet, clear.Id, ToClearRow(clear));
    }

    public async Task<bool> DeleteMessageAsync(string messageId, string requesterId, bool allowAny)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return false;
        var all = await GetAllAsync();
        var message = all.FirstOrDefault(m => m.Id.Equals(messageId, StringComparison.OrdinalIgnoreCase));
        if (message == null) return false;

        if (!allowAny &&
            !message.SenderId.Equals(requesterId, StringComparison.OrdinalIgnoreCase) &&
            !message.ReceiverId.Equals(requesterId, StringComparison.OrdinalIgnoreCase))
            return false;

        await _sheets.DeleteRowsByIdAsync(ChatSheet, new[] { message.Id });
        return true;
    }

    public async Task<int> DeleteConversationAsync(string userIdA, string userIdB)
    {
        var thread = await GetConversationAsync(userIdA, userIdB);
        if (thread.Count == 0) return 0;
        await _sheets.DeleteRowsByIdAsync(ChatSheet, thread.Select(x => x.Id).ToList());
        return thread.Count;
    }

    public async Task<ChatMessage> SendMessageAsync(string senderId, string senderName, string senderRole,
        string receiverId, string receiverName, string message,
        string? attachmentPath, string? attachmentName, long attachmentSize,
        string? replyToMessageId = null, bool isForwarded = false)
    {
        await EnsureSchemaAsync();
        var chatMessage = new ChatMessage
        {
            SenderId = senderId,
            SenderName = senderName,
            SenderRole = senderRole,
            ReceiverId = receiverId,
            ReceiverName = receiverName,
            Message = (message ?? string.Empty).Trim(),
            AttachmentPath = attachmentPath ?? string.Empty,
            AttachmentName = attachmentName ?? string.Empty,
            AttachmentSize = attachmentSize,
            IsRead = false,
            IsForwarded = isForwarded
        };

        // Reply: snapshot the original sender/preview text at send time (WhatsApp-style) -
        // so the reply strip keeps showing something sensible even if the original
        // message is later deleted.
        if (!string.IsNullOrWhiteSpace(replyToMessageId))
        {
            var all = await GetAllAsync();
            var original = all.FirstOrDefault(m => m.Id.Equals(replyToMessageId, StringComparison.OrdinalIgnoreCase));
            if (original != null)
            {
                chatMessage.ReplyToMessageId = original.Id;
                chatMessage.ReplyToSenderName = original.SenderName;
                var preview = !string.IsNullOrWhiteSpace(original.Message)
                    ? original.Message
                    : (original.HasAttachment ? $"📎 {original.AttachmentName}" : string.Empty);
                chatMessage.ReplyToPreview = preview.Length > 160 ? preview[..160] + "…" : preview;
            }
        }

        await _sheets.AppendAsync(ChatSheet, ToRow(chatMessage));
        TouchPresence(senderId);
        return chatMessage;
    }

    public async Task<Dictionary<string, string>> ToggleReactionAsync(string messageId, string userId, string emoji)
    {
        var all = await GetAllAsync();
        var message = all.FirstOrDefault(m => m.Id.Equals(messageId, StringComparison.OrdinalIgnoreCase));
        if (message == null) return new Dictionary<string, string>();

        var reactions = message.Reactions();
        // Tapping the same emoji again removes it (toggle); a different emoji replaces
        // the user's previous reaction - one reaction per person, same as WhatsApp.
        if (reactions.TryGetValue(userId, out var existing) && existing == emoji)
            reactions.Remove(userId);
        else
            reactions[userId] = emoji;

        message.ReactionsRaw = ChatMessage.ReactionsToRaw(reactions);
        await _sheets.UpsertRowByIdAsync(ChatSheet, message.Id, ToRow(message));
        return reactions;
    }

    public async Task<bool> ToggleStarAsync(string messageId, string userId)
    {
        var all = await GetAllAsync();
        var message = all.FirstOrDefault(m => m.Id.Equals(messageId, StringComparison.OrdinalIgnoreCase));
        if (message == null) return false;

        var starredBy = message.StarredBy();
        bool nowStarred;
        if (starredBy.Contains(userId, StringComparer.OrdinalIgnoreCase))
        {
            starredBy.RemoveAll(x => x.Equals(userId, StringComparison.OrdinalIgnoreCase));
            nowStarred = false;
        }
        else
        {
            starredBy.Add(userId);
            nowStarred = true;
        }

        message.StarredByRaw = ChatMessage.StarredByToRaw(starredBy);
        await _sheets.UpsertRowByIdAsync(ChatSheet, message.Id, ToRow(message));
        return nowStarred;
    }

    public async Task<bool> SetPinAsync(string messageId, string userIdA, string userIdB, bool pin)
    {
        var thread = await GetConversationAsync(userIdA, userIdB);
        var message = thread.FirstOrDefault(m => m.Id.Equals(messageId, StringComparison.OrdinalIgnoreCase));
        if (message == null) return false;

        var updates = new List<(string Id, IList<object> Values)>();
        if (pin)
        {
            // Only one pinned message per conversation (WhatsApp allows more, but one
            // keeps the pinned banner simple) - unpin anything else in this thread first.
            foreach (var other in thread.Where(m => m.IsPinned && !m.Id.Equals(message.Id, StringComparison.OrdinalIgnoreCase)))
            {
                other.IsPinned = false;
                updates.Add((other.Id, ToRow(other)));
            }
        }
        message.IsPinned = pin;
        updates.Add((message.Id, ToRow(message)));
        await _sheets.UpsertRowsByIdAsync(ChatSheet, updates);
        return true;
    }

    public async Task MarkConversationReadAsync(string currentUserId, string otherUserId)
    {
        var all = await GetAllAsync();
        var unread = all.Where(x =>
                x.ReceiverId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase) &&
                x.SenderId.Equals(otherUserId, StringComparison.OrdinalIgnoreCase) &&
                !x.IsRead)
            .ToList();
        if (unread.Count == 0) return;

        var updates = unread.Select(m =>
        {
            m.IsRead = true;
            m.ReadDate = DateTime.Now;
            return (m.Id, ToRow(m));
        });
        await _sheets.UpsertRowsByIdAsync(ChatSheet, updates);
    }

    public async Task<int> GetUnreadCountAsync(string currentUserId)
    {
        var all = await GetAllAsync();
        return all.Count(x => x.ReceiverId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase) && !x.IsRead);
    }

    public async Task<List<ChatContactViewModel>> GetContactsAsync(string currentUserId, bool canViewAll)
    {
        var activeUsers = (await _users.GetActiveUsersAsync())
            .Where(x => !x.Id.Equals(currentUserId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.FullName)
            .ToList();

        var all = await GetAllAsync();
        var result = new List<ChatContactViewModel>();
        foreach (var user in activeUsers)
        {
            var thread = all.Where(x =>
                    (x.SenderId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase) && x.ReceiverId.Equals(user.Id, StringComparison.OrdinalIgnoreCase)) ||
                    (x.SenderId.Equals(user.Id, StringComparison.OrdinalIgnoreCase) && x.ReceiverId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var last = thread.OrderByDescending(x => x.SentDate).FirstOrDefault();
            var unread = thread.Count(x => x.ReceiverId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase) && !x.IsRead);

            result.Add(new ChatContactViewModel
            {
                UserId = user.Id,
                FullName = user.FullName,
                Role = user.Role,
                IsOnline = IsOnline(user.Id),
                LastMessage = last == null ? string.Empty : (last.HasAttachment && string.IsNullOrWhiteSpace(last.Message) ? $"📎 {last.AttachmentName}" : last.Message),
                LastMessageDate = last?.SentDate,
                UnreadCount = unread
            });
        }

        return result
            .OrderByDescending(x => x.UnreadCount > 0)
            .ThenByDescending(x => x.LastMessageDate)
            .ThenBy(x => x.FullName)
            .ToList();
    }

    public async Task<List<ChatThreadSummaryViewModel>> GetAllConversationsAsync()
    {
        var all = await GetAllAsync();
        var pairs = all.Select(x =>
            {
                var ordered = string.CompareOrdinal(x.SenderId, x.ReceiverId) <= 0
                    ? (AId: x.SenderId, AName: x.SenderName, BId: x.ReceiverId, BName: x.ReceiverName)
                    : (AId: x.ReceiverId, AName: x.ReceiverName, BId: x.SenderId, BName: x.SenderName);
                return new { ordered.AId, ordered.AName, ordered.BId, ordered.BName, Message = x };
            })
            .GroupBy(x => (x.AId, x.BId))
            .Select(g =>
            {
                var last = g.OrderByDescending(x => x.Message.SentDate).First();
                return new ChatThreadSummaryViewModel
                {
                    UserAId = last.AId,
                    UserAName = last.AName,
                    UserBId = last.BId,
                    UserBName = last.BName,
                    LastMessage = last.Message.HasAttachment && string.IsNullOrWhiteSpace(last.Message.Message) ? $"📎 {last.Message.AttachmentName}" : last.Message.Message,
                    LastMessageDate = last.Message.SentDate,
                    MessageCount = g.Count()
                };
            })
            .OrderByDescending(x => x.LastMessageDate)
            .ToList();
        return pairs;
    }

    public void TouchPresence(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        LastSeen[userId] = DateTime.UtcNow;
    }

    public bool IsOnline(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;
        return LastSeen.TryGetValue(userId, out var seen) && DateTime.UtcNow - seen < OnlineWindow;
    }
}
