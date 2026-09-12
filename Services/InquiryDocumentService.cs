using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class InquiryDocumentService : IInquiryDocumentService
{
    private readonly IWebHostEnvironment _env;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly HashSet<string> AllowedDocTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Quotation", "PriceList", "BankDetails", "UpiImage", "Brochure", "Other"
    };

    public InquiryDocumentService(IWebHostEnvironment env) => _env = env;

    private string RootDir(string inquiryId) => Path.Combine(_env.WebRootPath, "uploads", "inquiries", Sanitize(inquiryId));
    private string MetaPath(string inquiryId) => Path.Combine(RootDir(inquiryId), "documents.json");
    private string SendLogPath(string inquiryId) => Path.Combine(RootDir(inquiryId), "send-logs.json");

    public Task<List<InquiryDocument>> GetForInquiryAsync(string inquiryId)
    {
        var list = ReadJson<List<InquiryDocument>>(MetaPath(inquiryId)) ?? new List<InquiryDocument>();
        return Task.FromResult(list.OrderByDescending(x => x.UploadedAt).ToList());
    }

    public async Task<InquiryDocument> SaveAsync(string inquiryId, IFormFile file, string docType, string uploadedByUserId, string uploadedByName, string? notes = null)
    {
        if (file == null || file.Length <= 0) throw new InvalidOperationException("No file uploaded.");
        if (file.Length > 15 * 1024 * 1024) throw new InvalidOperationException("File too large (max 15 MB).");

        var type = AllowedDocTypes.Contains(docType ?? "") ? docType! : "Other";
        var dir = RootDir(inquiryId);
        Directory.CreateDirectory(dir);

        var safeOriginal = Path.GetFileName(file.FileName);
        var ext = Path.GetExtension(safeOriginal);
        var stored = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var physical = Path.Combine(dir, stored);
        await using (var stream = File.Create(physical))
            await file.CopyToAsync(stream);

        var doc = new InquiryDocument
        {
            InquiryId = inquiryId,
            DocType = type,
            FileName = safeOriginal,
            StoredFileName = stored,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSize = file.Length,
            UploadedByUserId = uploadedByUserId,
            UploadedByName = uploadedByName,
            Notes = notes ?? string.Empty
        };

        var list = await GetForInquiryAsync(inquiryId);
        list.Insert(0, doc);
        WriteJson(MetaPath(inquiryId), list);
        return doc;
    }

    public async Task DeleteAsync(string inquiryId, string documentId)
    {
        var list = await GetForInquiryAsync(inquiryId);
        var doc = list.FirstOrDefault(x => x.Id.Equals(documentId, StringComparison.OrdinalIgnoreCase));
        if (doc == null) return;
        var path = Path.Combine(RootDir(inquiryId), doc.StoredFileName);
        if (File.Exists(path)) File.Delete(path);
        list.RemoveAll(x => x.Id.Equals(documentId, StringComparison.OrdinalIgnoreCase));
        WriteJson(MetaPath(inquiryId), list);
    }

    public async Task<string?> GetPhysicalPathAsync(string inquiryId, string documentId)
    {
        var list = await GetForInquiryAsync(inquiryId);
        var doc = list.FirstOrDefault(x => x.Id.Equals(documentId, StringComparison.OrdinalIgnoreCase));
        if (doc == null) return null;
        var path = Path.Combine(RootDir(inquiryId), doc.StoredFileName);
        return File.Exists(path) ? path : null;
    }

    public Task<List<InquirySendLog>> GetSendLogsAsync(string inquiryId)
    {
        var list = ReadJson<List<InquirySendLog>>(SendLogPath(inquiryId)) ?? new List<InquirySendLog>();
        return Task.FromResult(list.OrderByDescending(x => x.SentAt).ToList());
    }

    public async Task AddSendLogAsync(InquirySendLog log)
    {
        var list = await GetSendLogsAsync(log.InquiryId);
        list.Insert(0, log);
        if (list.Count > 200) list = list.Take(200).ToList();
        Directory.CreateDirectory(RootDir(log.InquiryId));
        WriteJson(SendLogPath(log.InquiryId), list);
    }

    private T? ReadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            string json;
            lock (_lock) { json = File.ReadAllText(path); }
            return JsonSerializer.Deserialize<T>(json);
        }
        catch { return default; }
    }

    private void WriteJson<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(data, JsonOpts);
        lock (_lock) { File.WriteAllText(path, json); }
    }

    private static string Sanitize(string id)
    {
        var chars = (id ?? "unknown").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }
}
