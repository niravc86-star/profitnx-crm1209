namespace ProfitNx.CRM.Models;

public class LoginLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime LoginAt { get; set; } = DateTime.Now;
    public DateTime? LogoutAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string DeviceSummary { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class LiveUserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime LoginAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string DeviceSummary { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

public class InquiryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InquiryId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty; // Quotation, PriceList, BankDetails, UpiImage, Brochure, Other
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public string Notes { get; set; } = string.Empty;
}

public class InquirySendLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InquiryId { get; set; } = string.Empty;
    public string SendType { get; set; } = string.Empty; // Quotation, PaymentPack, Brochure, Resend
    public string Channel { get; set; } = string.Empty; // Email, WhatsApp, Both
    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientMobile { get; set; } = string.Empty;
    public string DocumentIds { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string WhatsAppUrl { get; set; } = string.Empty;
    public string SentByUserId { get; set; } = string.Empty;
    public string SentByName { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.Now;
}
