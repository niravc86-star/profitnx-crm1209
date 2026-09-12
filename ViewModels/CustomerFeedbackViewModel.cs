using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class CustomerFeedbackViewModel
{
    [Required] public string Token { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    [Range(1, 5)] public int Rating { get; set; } = 5;
    public string Remarks { get; set; } = string.Empty;
}
