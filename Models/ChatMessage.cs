using System;
using System.Collections.Generic;
using System.Linq;

namespace ProfitNx.CRM.Models;

// One row = one chat message stored in the "ChatMessages" Google Sheet tab.
// The Chat screen (Views/Chat/Index.cshtml) reads/writes through IChatService only.
public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime SentDate { get; set; } = DateTime.Now;
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AttachmentPath { get; set; } = string.Empty;
    public string AttachmentName { get; set; } = string.Empty;
    public long AttachmentSize { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadDate { get; set; }

    // ADDED (2026-08-18): WhatsApp-style message actions (Reply / Forward /
    // React / Star / Pin). All stored on the same ChatMessages sheet row so
    // no new sheet/tab is needed.
    public string ReplyToMessageId { get; set; } = string.Empty;
    public string ReplyToSenderName { get; set; } = string.Empty;
    public string ReplyToPreview { get; set; } = string.Empty;
    public bool IsForwarded { get; set; }
    // Reactions: "userId1:emoji1|userId2:emoji2" - one reaction per user, latest wins.
    public string ReactionsRaw { get; set; } = string.Empty;
    // Star: comma-separated user ids who starred this message (per-person, WhatsApp-style).
    public string StarredByRaw { get; set; } = string.Empty;
    public bool IsPinned { get; set; }

    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentPath);
    public bool IsReply => !string.IsNullOrWhiteSpace(ReplyToMessageId);

    public Dictionary<string, string> Reactions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(ReactionsRaw)) return result;
        foreach (var pair in ReactionsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(':', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                result[parts[0]] = parts[1];
        }
        return result;
    }

    public static string ReactionsToRaw(Dictionary<string, string> reactions)
        => string.Join('|', reactions.Select(kv => $"{kv.Key}:{kv.Value}"));

    public List<string> StarredBy()
        => string.IsNullOrWhiteSpace(StarredByRaw)
            ? new List<string>()
            : StarredByRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    public static string StarredByToRaw(List<string> userIds) => string.Join(',', userIds);
}
