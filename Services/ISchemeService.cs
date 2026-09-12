using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public interface ISchemeService
{
    Task<List<Scheme>> GetAllAsync();
    Task<Scheme?> GetByIdAsync(string id);
    Task SaveAsync(Scheme scheme);
    Task DeleteAsync(string id);

    Task<List<SchemeParticipation>> GetParticipantsAsync();
    Task<List<SchemeParticipation>> GetParticipantsBySchemeAsync(string schemeId);
    Task AddParticipantAsync(SchemeParticipation participation);
    Task RemoveParticipantAsync(string id);
}
