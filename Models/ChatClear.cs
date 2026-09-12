namespace ProfitNx.CRM.Models;

// One row = one "Clear Chat" marker stored in the "ChatClears" Google Sheet tab.
// Clearing a chat only hides history for the user who cleared it - the other
// person's copy of the conversation (and the ChatMessages rows themselves)
// is untouched. Messages sent on/before ClearedBeforeDate are simply filtered
// out of GetConversationAsync(...) when that user is the viewer.
public class ChatClear
{
    public string Id => $"{UserId}|{OtherUserId}";
    public string UserId { get; set; } = string.Empty;
    public string OtherUserId { get; set; } = string.Empty;
    public DateTime ClearedBeforeDate { get; set; } = DateTime.Now;
}
