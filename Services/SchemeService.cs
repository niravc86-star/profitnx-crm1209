using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class SchemeService : ISchemeService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private const string SchemeReadRange = "Schemes!A2:L";
    private const string ParticipationReadRange = "SchemeParticipation!A2:I";
    public SchemeService(IGoogleSheetsService googleSheetsService) => _googleSheetsService = googleSheetsService;

    public async Task<List<Scheme>> GetAllAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(SchemeReadRange);
        return rows.Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0))).Select(r => new Scheme
        {
            Id=SheetValueHelper.GetString(r,0), Name=SheetValueHelper.GetString(r,1), SchemeType=SheetValueHelper.GetString(r,2), Description=SheetValueHelper.GetString(r,3),
            StartDate=SheetValueHelper.GetDate(r,4,DateTime.Today), EndDate=SheetValueHelper.GetDate(r,5,DateTime.Today), ExtraMarginPercent=SheetValueHelper.GetDecimal(r,6),
            DiscountAmount=SheetValueHelper.GetDecimal(r,7), TargetAmount=SheetValueHelper.GetDecimal(r,8), ApplicableRoles=SheetValueHelper.GetString(r,9),
            IsActive=SheetValueHelper.GetBool(r,10), Notes=SheetValueHelper.GetString(r,11)
        }).OrderByDescending(x=>x.StartDate).ThenBy(x=>x.Name).ToList();
    }
    public async Task<Scheme?> GetByIdAsync(string id) => (await GetAllAsync()).FirstOrDefault(x=>x.Id==id);
    public async Task SaveAsync(Scheme scheme)
    {
        var existing=(await GetAllAsync()).FirstOrDefault(x=>x.Id==scheme.Id);
        if(existing==null) scheme.Id=string.IsNullOrWhiteSpace(scheme.Id)?Guid.NewGuid().ToString():scheme.Id;
        else { existing.Name=scheme.Name; existing.SchemeType=scheme.SchemeType; existing.Description=scheme.Description; existing.StartDate=scheme.StartDate; existing.EndDate=scheme.EndDate; existing.ExtraMarginPercent=scheme.ExtraMarginPercent; existing.DiscountAmount=scheme.DiscountAmount; existing.TargetAmount=scheme.TargetAmount; existing.ApplicableRoles=scheme.ApplicableRoles; existing.IsActive=scheme.IsActive; existing.Notes=scheme.Notes; scheme=existing; }
        await _googleSheetsService.UpsertRowByIdAsync("Schemes",scheme.Id,ToRow(scheme));
    }
    public async Task DeleteAsync(string id)
    {
        var participantIds=(await GetParticipantsAsync()).Where(x=>x.SchemeId==id).Select(x=>x.Id).ToList();
        await _googleSheetsService.DeleteRowsByIdAsync("Schemes",new[]{id});
        if(participantIds.Count>0) await _googleSheetsService.DeleteRowsByIdAsync("SchemeParticipation",participantIds);
    }
    public async Task<List<SchemeParticipation>> GetParticipantsAsync()
    {
        var rows=await _googleSheetsService.ReadAsync(ParticipationReadRange);
        return rows.Where(r=>!string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r,0))).Select(r=>new SchemeParticipation
        {
            Id=SheetValueHelper.GetString(r,0), SchemeId=SheetValueHelper.GetString(r,1), SchemeName=SheetValueHelper.GetString(r,2), UserId=SheetValueHelper.GetString(r,3),
            UserName=SheetValueHelper.GetString(r,4), Role=SheetValueHelper.GetString(r,5), PartnerCode=SheetValueHelper.GetString(r,6),
            JoinedDate=SheetValueHelper.GetDate(r,7,DateTime.Today), ActiveUntil=SheetValueHelper.GetNullableDate(r,8)
        }).OrderByDescending(x=>x.JoinedDate).ToList();
    }
    public async Task<List<SchemeParticipation>> GetParticipantsBySchemeAsync(string schemeId) => (await GetParticipantsAsync()).Where(x=>x.SchemeId==schemeId).ToList();
    public async Task AddParticipantAsync(SchemeParticipation participation)
    {
        if((await GetParticipantsAsync()).Any(x=>x.SchemeId==participation.SchemeId && x.UserId==participation.UserId)) throw new InvalidOperationException("Selected user/partner is already joined in this scheme.");
        participation.Id=string.IsNullOrWhiteSpace(participation.Id)?Guid.NewGuid().ToString():participation.Id;
        await _googleSheetsService.UpsertRowByIdAsync("SchemeParticipation",participation.Id,ToRow(participation));
    }
    public Task RemoveParticipantAsync(string id)=>_googleSheetsService.DeleteRowsByIdAsync("SchemeParticipation",new[]{id});
    private static IList<object> ToRow(Scheme x)=>new List<object>{x.Id,x.Name,x.SchemeType,x.Description,x.StartDate.ToString("yyyy-MM-dd"),x.EndDate.ToString("yyyy-MM-dd"),x.ExtraMarginPercent,x.DiscountAmount,x.TargetAmount,x.ApplicableRoles,x.IsActive,x.Notes};
    private static IList<object> ToRow(SchemeParticipation x)=>new List<object>{x.Id,x.SchemeId,x.SchemeName,x.UserId,x.UserName,x.Role,x.PartnerCode,x.JoinedDate.ToString("yyyy-MM-dd"),x.ActiveUntil?.ToString("yyyy-MM-dd")??string.Empty};
}
