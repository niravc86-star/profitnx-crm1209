namespace ProfitNx.CRM.Services;

public static class ImplementationTrainingTopicCatalog
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "Software Desktop",
        "Master - Account/Product",
        "Transactions - Sales/Purchase/Cash/Bank",
        "Service Entry",
        "Report Overview (Sales/Purchase/Stock/Cash/Bank)",
        "Outstanding/Ledger Report",
        "Analytics Report",
        "GST Reports Overview",
        "Backup",
        "Mobile Application",
        "WhatsApp Setup",
        "Google Drive Backup"
    };

    public static List<string> Normalize(IEnumerable<string>? selectedTopics)
    {
        var allowed = All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (selectedTopics ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && allowed.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
