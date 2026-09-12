namespace ProfitNx.CRM.Services;

public class GoogleSheetsOptions
{
    public string SpreadsheetId { get; set; } = string.Empty;
    public string CredentialsFile { get; set; } = string.Empty;

    // Optional alternative for environments where mounting a JSON file is not
    // convenient. Prefer environment variable GOOGLE_SERVICE_ACCOUNT_JSON so
    // private credentials are not stored in source control.
    public string CredentialsJson { get; set; } = string.Empty;
}
