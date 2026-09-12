using Microsoft.AspNetCore.Http;
using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface IInquiryDocumentService
{
    Task<List<InquiryDocument>> GetForInquiryAsync(string inquiryId);
    Task<InquiryDocument> SaveAsync(string inquiryId, IFormFile file, string docType, string uploadedByUserId, string uploadedByName, string? notes = null);
    Task DeleteAsync(string inquiryId, string documentId);
    Task<string?> GetPhysicalPathAsync(string inquiryId, string documentId);
    Task<List<InquirySendLog>> GetSendLogsAsync(string inquiryId);
    Task AddSendLogAsync(InquirySendLog log);
}
