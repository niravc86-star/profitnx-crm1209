using System.ComponentModel.DataAnnotations;

namespace ProfitNx.CRM.ViewModels;

public class NotificationFormViewModel
{
        public string Channel { get; set; } = "Email";

        public string Recipient { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

    public string RelatedEntityType { get; set; } = string.Empty;
    public string RelatedEntityId { get; set; } = string.Empty;
}
