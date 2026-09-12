using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class SchemeParticipationFormViewModel
{
    [Required]
    public string SchemeId { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Joined Date")]
    public DateTime JoinedDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    [Display(Name = "Active Until")]
    public DateTime? ActiveUntil { get; set; }
}
