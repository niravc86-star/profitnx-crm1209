using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class UserFormViewModel
{
    public string? Id { get; set; }

        [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

        public string Role { get; set; } = "User";

    [Display(Name = "Partner Code")]
    public string PartnerCode { get; set; } = string.Empty;

    [Display(Name = "Mobile")]
    public string Mobile { get; set; } = string.Empty;

    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Display(Name = "SMTP Host")]
    public string? SmtpHost { get; set; }

    [Display(Name = "SMTP Port")]
    public int SmtpPort { get; set; } = 587;

    [Display(Name = "SMTP Username")]
    public string? SmtpUsername { get; set; }

    [Display(Name = "SMTP Password")]
    public string? SmtpPassword { get; set; }

    [Display(Name = "SMTP SSL")]
    public bool SmtpEnableSsl { get; set; } = true;

    public string City { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Target Amount")]
    public decimal TargetAmount { get; set; }

    [Display(Name = "Margin %")]
    public decimal MarginPercent { get; set; }

    [Display(Name = "Target From Date")]
    public DateTime? TargetFromDate { get; set; }

    [Display(Name = "Target To Date")]
    public DateTime? TargetToDate { get; set; }

    public string? Notes { get; set; }
}
