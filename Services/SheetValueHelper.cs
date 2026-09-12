namespace ProfitNx.CRM.Services;

public static class SheetValueHelper
{
    // FIX (2026-08-17): date/time cells written with a plain "yyyy-MM-dd HH:mm:ss"
    // string can get silently auto-converted by Google Sheets (USER_ENTERED input)
    // into its own date serial number / re-formatted display text. When that
    // formatted text doesn't match what DateTime.TryParse expects, the parse can
    // fail even though the cell "looks like" a valid date to a human. Writers now
    // prefix date/time cells with a leading apostrophe (see ToSheetText) to force
    // Sheets to store them as literal text, exactly as written. GetString strips
    // that leading apostrophe back off automatically (it is never part of the
    // real value; a manually-typed leading apostrophe in Sheets isn't part of the
    // read value either) so every existing caller keeps working unchanged, on
    // both old rows (no apostrophe) and new rows (apostrophe-guarded).
    public static string GetString(IList<object> row, int index)
    {
        var raw = row.Count > index ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;
        return raw.Length > 0 && raw[0] == '\'' ? raw[1..].Trim() : raw;
    }

    // Use when WRITING any date/time value into a sheet row (ToRow methods) to
    // stop Google Sheets from reinterpreting it. Always pair with GetDateTime
    // (or GetString) on the read side, which already strips this guard back off.
    public static string ToSheetText(string value) => "'" + value;

    public static bool GetBool(IList<object> row, int index, bool defaultValue = true)
    {
        var value = GetString(row, index);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1");
    }

    public static decimal GetDecimal(IList<object> row, int index)
        => decimal.TryParse(GetString(row, index), out var value) ? value : 0;

    public static int GetInt(IList<object> row, int index)
        => int.TryParse(GetString(row, index), out var value) ? value : 0;

    public static DateTime GetDate(IList<object> row, int index, DateTime defaultValue)
        => DateTime.TryParse(GetString(row, index), out var value) ? value : defaultValue;

    public static DateTime? GetNullableDate(IList<object> row, int index)
        => DateTime.TryParse(GetString(row, index), out var value) ? value : null;

    public static DateTime? GetDateTime(IList<object> row, int index)
        => DateTime.TryParse(GetString(row, index), out var value) ? value : null;
}
