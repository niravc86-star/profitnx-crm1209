using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class RoleFormViewModel
{
    public string? Id { get; set; }
    [Display(Name = "Role Name")]
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
    [Display(Name = "Landing Page")]
    public string LandingPage { get; set; } = "/Dashboard";
}
