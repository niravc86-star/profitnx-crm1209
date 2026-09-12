namespace ProfitNx.CRM.Services;

public interface IGoogleSheetsService
{
    Task<IList<IList<object>>> ReadAsync(string range);
    Task EnsureSheetAsync(string sheetName, IList<object> headers);
    Task AppendAsync(string sheetName, IList<object> row);
    Task UpdateAsync(string range, IList<IList<object>> values);
    Task ClearAsync(string range);
    Task WriteRowsAsync(string startCellRange, IList<IList<object>> values);
    Task UpsertRowByIdAsync(string sheetName, string id, IList<object> row);
    Task UpsertRowsByIdAsync(string sheetName, IEnumerable<(string Id, IList<object> Values)> rows);
    Task DeleteRowsByIdAsync(string sheetName, IEnumerable<string> ids);
    void InvalidateSheetCache(string sheetName);
}
