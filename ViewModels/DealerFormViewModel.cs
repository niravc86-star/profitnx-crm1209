namespace ProfitNx.CRM.ViewModels;

public class DealerFormViewModel
{
    public string? Id { get; set; }
    public string DealerName { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal MarginPercent { get; set; }
    public decimal YearlyTargetAmount { get; set; }
    public int TargetYear { get; set; } = DateTime.Today.Year;
    public DateTime? TargetFromDate { get; set; }
    public DateTime? TargetToDate { get; set; }
    public string TargetRowsJson { get; set; } = string.Empty;
    public DateTime? JoiningDate { get; set; }
    public bool GivesPss { get; set; }
    public bool GivesApi { get; set; }
    public DateTime? PssApiChangeDate { get; set; }
    public string PssApiRemark { get; set; } = string.Empty;
    public DateTime? LeftDate { get; set; }
    public string LeftRemark { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
