using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class SchemeFormViewModel
{
    public string? Id { get; set; }

        [Display(Name = "Scheme Name")]
    public string Name { get; set; } = string.Empty;

        [Display(Name = "Scheme Type")]
    public string SchemeType { get; set; } = "Margin";

    [Display(Name = "Description / Rules")]
    public string Description { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Start Date")]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    [Display(Name = "End Date")]
    public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(1);

    [Display(Name = "Extra Margin %")]
    public decimal ExtraMarginPercent { get; set; }

    [Display(Name = "Discount Amount")]
    public decimal DiscountAmount { get; set; }

    [Display(Name = "Target Amount")]
    public decimal TargetAmount { get; set; }

    [Display(Name = "Applicable Roles")]
    public string ApplicableRoles { get; set; } = "User,Partner";

    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}
