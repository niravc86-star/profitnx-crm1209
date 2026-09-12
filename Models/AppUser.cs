namespace ProfitNx.CRM.Models;

public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User"; // Admin, Partner, User
    public string PartnerCode { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public bool SmtpEnableSsl { get; set; } = true;
    public string City { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal TargetAmount { get; set; }
    public decimal MarginPercent { get; set; }
    public DateTime? TargetFromDate { get; set; }
    public DateTime? TargetToDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string ThemePreference { get; set; } = "classic";
}
