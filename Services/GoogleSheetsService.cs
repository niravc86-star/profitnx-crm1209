using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace ProfitNx.CRM.Services;

public class GoogleSheetsService : IGoogleSheetsService
{
    private readonly IConfiguration configuration;
    private readonly IMemoryCache memoryCache;
    private readonly string contentRootPath;
    private readonly object initializationLock = new();
    private SheetsService? sheetsService;
    private string spreadsheetId = string.Empty;
    private static readonly SemaphoreSlim SheetLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> ReadCacheKeys = new(StringComparer.OrdinalIgnoreCase);

    public GoogleSheetsService(
        IConfiguration configuration,
        IMemoryCache cache,
        IWebHostEnvironment environment)
    {
        this.configuration = configuration;
        memoryCache = cache;
        contentRootPath = environment.ContentRootPath;

        // Do not read/validate the private key in the constructor. This service is a
        // singleton and is required by the Login controller. Constructor exceptions
        // therefore prevented even the Login page from opening. Configuration is now
        // validated lazily when the first Google Sheets operation is performed, so the
        // application can show a clear setup message instead of an unhandled exception.
    }

    private SheetsService Service => GetOrCreateService();
    private string SpreadsheetId
    {
        get
        {
            _ = Service;
            return spreadsheetId;
        }
    }

    private SheetsService GetOrCreateService()
    {
        if (sheetsService != null) return sheetsService;

        lock (initializationLock)
        {
            if (sheetsService != null) return sheetsService;

            spreadsheetId = (configuration["GoogleSheets:SpreadsheetId"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(spreadsheetId) ||
                spreadsheetId.Equals("YOUR_GOOGLE_SHEET_ID", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Google Sheets is not configured. Set GoogleSheets:SpreadsheetId in appsettings.json " +
                    "to the ID from your Google Sheet URL.");
            }

            var credentialJson = configuration["GoogleSheets:CredentialsJson"];
            if (string.IsNullOrWhiteSpace(credentialJson))
                credentialJson = Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_JSON");

            GoogleCredential credential;
            if (!string.IsNullOrWhiteSpace(credentialJson))
            {
                ValidateServiceAccountJson(credentialJson, "GoogleSheets:CredentialsJson / GOOGLE_SERVICE_ACCOUNT_JSON");
                credential = GoogleCredential.FromJson(credentialJson);
            }
            else
            {
                var credentialPath = ResolveCredentialPath(out var searchedPaths);
                if (credentialPath == null)
                {
                    throw new InvalidOperationException(
                        "Google service-account credential file was not found. Copy your REAL Google service-account key " +
                        "as 'google-service-account.json' into the project/published application folder, or set " +
                        "GoogleSheets:CredentialsFile to an absolute path. You can also set the " +
                        "GOOGLE_APPLICATION_CREDENTIALS environment variable. Do not rename the example file without " +
                        "replacing its placeholder values. Searched: " + string.Join("; ", searchedPaths));
                }

                string jsonText;
                try
                {
                    jsonText = File.ReadAllText(credentialPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        $"Google service-account credential file could not be read: '{credentialPath}'. " +
                        "Check file permissions for the account running the web application.", ex);
                }

                ValidateServiceAccountJson(jsonText, credentialPath);
                credential = GoogleCredential.FromJson(jsonText);
            }

            sheetsService = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential.CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "ProfitNx CRM"
            });

            return sheetsService;
        }
    }

    private string? ResolveCredentialPath(out IReadOnlyList<string> searchedPaths)
    {
        var configuredPath = configuration["GoogleSheets:CredentialsFile"];
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = configuration["GoogleSheets:CredentialsPath"];
        var environmentPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");

        var candidateValues = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidateValues.Add(configuredPath.Trim());
        if (!string.IsNullOrWhiteSpace(environmentPath)) candidateValues.Add(environmentPath.Trim());
        if (candidateValues.Count == 0) candidateValues.Add("google-service-account.json");

        var candidates = new List<string>();
        foreach (var candidateValue in candidateValues)
        {
            if (Path.IsPathRooted(candidateValue))
            {
                candidates.Add(Path.GetFullPath(candidateValue));
                continue;
            }

            candidates.Add(Path.GetFullPath(Path.Combine(contentRootPath, candidateValue)));
            candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, candidateValue)));
            candidates.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), candidateValue)));

            // App_Data is convenient on IIS/published deployments and is already part
            // of this project. Only the file name is appended to avoid duplicate folders.
            candidates.Add(Path.GetFullPath(Path.Combine(contentRootPath, "App_Data", Path.GetFileName(candidateValue))));
        }

        var distinctCandidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        searchedPaths = distinctCandidates;
        return distinctCandidates.FirstOrDefault(File.Exists);
    }

    private static void ValidateServiceAccountJson(string jsonText, string source)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            var valid = root.TryGetProperty("type", out var type) &&
                        string.Equals(type.GetString(), "service_account", StringComparison.Ordinal) &&
                        root.TryGetProperty("private_key", out var privateKey) &&
                        !string.IsNullOrWhiteSpace(privateKey.GetString()) &&
                        root.TryGetProperty("client_email", out var clientEmail) &&
                        !string.IsNullOrWhiteSpace(clientEmail.GetString());

            if (!valid || jsonText.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The Google credential from '{source}' is not a valid service-account key or still contains placeholder values. " +
                    "Download a fresh JSON key from Google Cloud IAM and replace the file.");
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException(
                $"The Google credential from '{source}' is not valid JSON.", ex);
        }
    }

    public async Task EnsureSheetAsync(string sheetName, IList<object> headers)
    {
        if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentException("Sheet name is required.", nameof(sheetName));
        await SheetLock.WaitAsync();
        try
        {
            var getRequest = Service.Spreadsheets.Get(SpreadsheetId);
            getRequest.Fields = "sheets.properties";
            var spreadsheet = await ExecuteWithRetryAsync(async () => await getRequest.ExecuteAsync());
            var exists = spreadsheet.Sheets?.Any(x => string.Equals(x.Properties?.Title, sheetName, StringComparison.OrdinalIgnoreCase)) == true;
            if (!exists)
            {
                var addBody = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>
                    {
                        new() { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = sheetName } } }
                    }
                };
                var addRequest = Service.Spreadsheets.BatchUpdate(addBody, SpreadsheetId);
                await ExecuteWithRetryAsync(async () => { await addRequest.ExecuteAsync(); return true; });
            }

            if (headers != null && headers.Count > 0)
            {
                var headerBody = new ValueRange { Values = new List<IList<object>> { headers.ToList() } };
                var headerRequest = Service.Spreadsheets.Values.Update(headerBody, SpreadsheetId, $"{sheetName}!A1");
                headerRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                await ExecuteWithRetryAsync(async () => { await headerRequest.ExecuteAsync(); return true; });
            }
            InvalidateSheetCache(sheetName);
        }
        finally { SheetLock.Release(); }
    }

    public async Task<IList<IList<object>>> ReadAsync(string range)
    {
        var cacheKey = "gsheet_read_" + range.Trim().ToLowerInvariant();
        ReadCacheKeys.TryAdd(cacheKey, 0);
        var rows = await memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            // Longer cache window reduces repeated full-range Google Sheets reads
            // during navigation; writes still invalidate via InvalidateSheetCache.
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return await ReadDirectAsync(range);
        });
        return rows ?? new List<IList<object>>();
    }

    public async Task AppendAsync(string sheetName, IList<object> row)
    {
        await SheetLock.WaitAsync();
        try
        {
            await AppendRowsDirectAsync(sheetName, new List<IList<object>> { row.ToList() });
            InvalidateSheetCache(sheetName);
        }
        finally { SheetLock.Release(); }
    }

    public async Task UpdateAsync(string range, IList<IList<object>> values)
    {
        await SheetLock.WaitAsync();
        try
        {
            var body = new ValueRange { Values = values };
            var request = Service.Spreadsheets.Values.Update(body, SpreadsheetId, range);
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await ExecuteWithRetryAsync(async () => { await request.ExecuteAsync(); return true; });
            InvalidateSheetCache(GetSheetName(range));
        }
        finally { SheetLock.Release(); }
    }

    public async Task ClearAsync(string range)
    {
        await SheetLock.WaitAsync();
        try
        {
            var request = Service.Spreadsheets.Values.Clear(new ClearValuesRequest(), SpreadsheetId, range);
            await ExecuteWithRetryAsync(async () => { await request.ExecuteAsync(); return true; });
            InvalidateSheetCache(GetSheetName(range));
        }
        finally { SheetLock.Release(); }
    }

    public async Task WriteRowsAsync(string startCellRange, IList<IList<object>> values)
    {
        if (values == null || values.Count == 0) return;
        await UpdateAsync(startCellRange, values);
    }

    public Task UpsertRowByIdAsync(string sheetName, string id, IList<object> row)
        => UpsertRowsByIdAsync(sheetName, new[] { (id, row) });

    public async Task UpsertRowsByIdAsync(string sheetName, IEnumerable<(string Id, IList<object> Values)> rows)
    {
        var items = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToList();
        if (items.Count == 0) return;

        await SheetLock.WaitAsync();
        try
        {
            // Read only the ID column. This avoids clearing and rewriting 100,000+ rows.
            var idRows = await ReadDirectAsync($"{sheetName}!A2:A");
            var rowMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < idRows.Count; i++)
            {
                var existingId = idRows[i].Count > 0 ? Convert.ToString(idRows[i][0])?.Trim() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(existingId) && !rowMap.ContainsKey(existingId)) rowMap[existingId] = i + 2;
            }

            var updates = new List<ValueRange>();
            var appends = new List<IList<object>>();
            foreach (var item in items)
            {
                if (rowMap.TryGetValue(item.Id, out var rowNumber))
                    updates.Add(new ValueRange { Range = $"{sheetName}!A{rowNumber}", Values = new List<IList<object>> { item.Values.ToList() } });
                else
                    appends.Add(item.Values.ToList());
            }

            if (updates.Count > 0)
            {
                var body = new BatchUpdateValuesRequest
                {
                    ValueInputOption = "USER_ENTERED",
                    Data = updates
                };
                var request = Service.Spreadsheets.Values.BatchUpdate(body, SpreadsheetId);
                await ExecuteWithRetryAsync(async () => { await request.ExecuteAsync(); return true; });
            }

            if (appends.Count > 0) await AppendRowsDirectAsync(sheetName, appends);
            InvalidateSheetCache(sheetName);
        }
        finally { SheetLock.Release(); }
    }

    public async Task DeleteRowsByIdAsync(string sheetName, IEnumerable<string> ids)
    {
        var idSet = ids.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0) return;

        await SheetLock.WaitAsync();
        try
        {
            var idRows = await ReadDirectAsync($"{sheetName}!A2:A");
            var rowIndexes = new List<int>(); // zero-based sheet row indexes
            for (var i = 0; i < idRows.Count; i++)
            {
                var existingId = idRows[i].Count > 0 ? Convert.ToString(idRows[i][0])?.Trim() ?? string.Empty : string.Empty;
                if (idSet.Contains(existingId)) rowIndexes.Add(i + 1);
            }
            if (rowIndexes.Count == 0) return;

            var sheetId = await GetSheetIdAsync(sheetName);
            var requests = rowIndexes.OrderByDescending(x => x).Select(index => new Request
            {
                DeleteDimension = new DeleteDimensionRequest
                {
                    Range = new DimensionRange
                    {
                        SheetId = sheetId,
                        Dimension = "ROWS",
                        StartIndex = index,
                        EndIndex = index + 1
                    }
                }
            }).ToList();

            var body = new BatchUpdateSpreadsheetRequest { Requests = requests };
            var request = Service.Spreadsheets.BatchUpdate(body, SpreadsheetId);
            await ExecuteWithRetryAsync(async () => { await request.ExecuteAsync(); return true; });
            InvalidateSheetCache(sheetName);
        }
        finally { SheetLock.Release(); }
    }

    public void InvalidateSheetCache(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName)) return;
        var normalized = "gsheet_read_" + sheetName.Trim().Trim('\'').ToLowerInvariant() + "!";
        foreach (var key in ReadCacheKeys.Keys)
        {
            if (key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                memoryCache.Remove(key);
                ReadCacheKeys.TryRemove(key, out _);
            }
        }
    }

    private async Task<IList<IList<object>>> ReadDirectAsync(string range)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var request = Service.Spreadsheets.Values.Get(SpreadsheetId, range);
            var response = await request.ExecuteAsync();
            return response.Values ?? new List<IList<object>>();
        });
    }

    private async Task AppendRowsDirectAsync(string sheetName, IList<IList<object>> rows)
    {
        var body = new ValueRange { Values = rows };
        var request = Service.Spreadsheets.Values.Append(body, SpreadsheetId, $"{sheetName}!A:A");
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        request.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await ExecuteWithRetryAsync(async () => { await request.ExecuteAsync(); return true; });
    }

    private async Task<int> GetSheetIdAsync(string sheetName)
    {
        var request = Service.Spreadsheets.Get(SpreadsheetId);
        request.Fields = "sheets.properties";
        var spreadsheet = await ExecuteWithRetryAsync(async () => await request.ExecuteAsync());
        var sheet = spreadsheet.Sheets?.FirstOrDefault(x => string.Equals(x.Properties?.Title, sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet?.Properties?.SheetId == null) throw new InvalidOperationException($"Google Sheet tab '{sheetName}' was not found.");
        return sheet.Properties.SheetId.Value;
    }

    private static string GetSheetName(string range)
    {
        var bang = range.IndexOf('!');
        return (bang >= 0 ? range[..bang] : range).Trim().Trim('\'');
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        var retry = 0;
        while (true)
        {
            try { return await action(); }
            catch (GoogleApiException ex) when ((int)ex.HttpStatusCode is 429 or 500 or 502 or 503 or 504)
            {
                retry++;
                if (retry > 5) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * retry * retry));
            }
        }
    }
}
