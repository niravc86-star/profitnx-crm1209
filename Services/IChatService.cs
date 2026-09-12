using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;
using System.Collections.Generic;

namespace ProfitNx.CRM.Services;

public interface IChatService
{
    Task EnsureSchemaAsync();

    // Contacts a given user is allowed to open a conversation with.
    // canViewAll (Admin with chat.viewall) sees every active user; everyone else
    // sees every other active user by default (per "default badha allowed" requirement) -
    // an Admin can still turn chat off for a specific person from User Wise Rights.
    Task<List<ChatContactViewModel>> GetContactsAsync(string currentUserId, bool canViewAll);

    // viewerUserId (optional): when supplied, messages the viewer has "Cleared"
    // (see ClearConversationAsync) are filtered out for them only - the other
    // participant, and admin monitoring (Thread/AllConversations, which pass no
    // viewer), still see the full history.
    Task<List<ChatMessage>> GetConversationAsync(string userIdA, string userIdB, string? viewerUserId = null);

    Task<ChatMessage> SendMessageAsync(string senderId, string senderName, string senderRole,
        string receiverId, string receiverName, string message,
        string? attachmentPath, string? attachmentName, long attachmentSize,
        string? replyToMessageId = null, bool isForwarded = false);

    // WhatsApp-style message actions (2026-08-18). All three act on a single
    // message row and return the info the caller needs to update the UI.
    Task<Dictionary<string, string>> ToggleReactionAsync(string messageId, string userId, string emoji);
    Task<bool> ToggleStarAsync(string messageId, string userId);
    Task<bool> SetPinAsync(string messageId, string userIdA, string userIdB, bool pin);

    Task MarkConversationReadAsync(string currentUserId, string otherUserId);

    // "Clear Chat" (user wise): hides everything currently in the conversation
    // from currentUserId's own view only. Requires "chat.clear" permission -
    // enforced in ChatController, not here.
    Task ClearConversationAsync(string currentUserId, string otherUserId);

    // "Delete Chat" - particular message: permanently removes one message row.
    // allowAny=true (chat.viewall admins) can delete any message; otherwise the
    // requester must be the sender or receiver of that specific message.
    // Returns false when the message does not exist or the requester is not allowed to remove it.
    Task<bool> DeleteMessageAsync(string messageId, string requesterId, bool allowAny);

    // "Delete Chat" - delete all: permanently removes every message in the
    // conversation between the two users. Returns the number of messages removed.
    Task<int> DeleteConversationAsync(string userIdA, string userIdB);

    Task<int> GetUnreadCountAsync(string currentUserId);

    // Admin monitoring: every distinct conversation pair in the system.
    Task<List<ChatThreadSummaryViewModel>> GetAllConversationsAsync();

    // Lightweight in-memory presence (no extra sheet writes). Call on every chat AJAX hit.
    void TouchPresence(string userId);
    bool IsOnline(string userId);
}
