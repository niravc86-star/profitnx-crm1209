using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class ImplementationCompletionViewModel
{
    [Required]
    public string CaseId { get; set; } = string.Empty;

    public List<string> TrainingTopics { get; set; } = new();

    [StringLength(4000)]
    public string OtherPointsCovered { get; set; } = string.Empty;
}
