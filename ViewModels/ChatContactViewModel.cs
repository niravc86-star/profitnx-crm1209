namespace ProfitNx.CRM.ViewModels;

// One row in the Chat contact list (left panel of Views/Chat/Index.cshtml).
public class ChatContactViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public DateTime? LastMessageDate { get; set; }
    public int UnreadCount { get; set; }
}

// One row in the Admin "All Conversations" monitoring list. Admin (chat.viewall)
// can open any pair of users to review the thread even if not a participant.
public class ChatThreadSummaryViewModel
{
    public string UserAId { get; set; } = string.Empty;
    public string UserAName { get; set; } = string.Empty;
    public string UserBId { get; set; } = string.Empty;
    public string UserBName { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public DateTime? LastMessageDate { get; set; }
    public int MessageCount { get; set; }
}
