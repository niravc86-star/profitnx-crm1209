using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfitNx.CRM.Services;

public class YearlyTargetService : IYearlyTargetService
{
    private readonly IWebHostEnvironment _environment;
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public YearlyTargetService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<decimal> GetTargetAsync(int year)
    {
        var root = await ReadAsync();
        var node = root[year.ToString()];
        if (node != null && decimal.TryParse(node.ToString(), out var value)) return value;
        return 0m;
    }

    public async Task SetTargetAsync(int year, decimal amount)
    {
        await FileLock.WaitAsync();
        try
        {
            var root = await ReadAsync();
            root[year.ToString()] = Math.Round(Math.Max(0m, amount), 2);
            var path = GetPath();
            var tempPath = path + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(options));
            File.Move(tempPath, path, true);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private string GetPath()
    {
        var directory = Path.Combine(_environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "yearly-targets.json");
    }

    private async Task<JsonObject> ReadAsync()
    {
        var path = GetPath();
        if (!File.Exists(path)) return new JsonObject();
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch
        {
            // Never break the dashboard because a previous partial file exists.
            return new JsonObject();
        }
    }
}
