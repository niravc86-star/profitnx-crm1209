using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class DealerService : IDealerService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private const string ReadRange = "Dealers!A2:U";
    public DealerService(IGoogleSheetsService googleSheetsService) => _googleSheetsService = googleSheetsService;

    public async Task<List<Dealer>> GetAllAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(ReadRange);
        return rows.Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0))).Select(r => new Dealer
        {
            Id = SheetValueHelper.GetString(r, 0), DealerName = SheetValueHelper.GetString(r, 1), ContactPerson = SheetValueHelper.GetString(r, 2),
            City = SheetValueHelper.GetString(r, 3), Mobile = SheetValueHelper.GetString(r, 4), Email = SheetValueHelper.GetString(r, 5),
            IsActive = SheetValueHelper.GetBool(r, 6), MarginPercent = SheetValueHelper.GetDecimal(r, 7), YearlyTargetAmount = SheetValueHelper.GetDecimal(r, 8),
            TargetYear = SheetValueHelper.GetInt(r, 9) == 0 ? DateTime.Today.Year : SheetValueHelper.GetInt(r, 9),
            TargetFromDate = SheetValueHelper.GetNullableDate(r, 10), TargetToDate = SheetValueHelper.GetNullableDate(r, 11),
            JoiningDate = SheetValueHelper.GetNullableDate(r, 12), GivesPss = SheetValueHelper.GetBool(r, 13), GivesApi = SheetValueHelper.GetBool(r, 14),
            PssApiChangeDate = SheetValueHelper.GetNullableDate(r, 15), PssApiRemark = SheetValueHelper.GetString(r, 16),
            LeftDate = SheetValueHelper.GetNullableDate(r, 17), LeftRemark = SheetValueHelper.GetString(r, 18), Notes = SheetValueHelper.GetString(r, 19),
            TargetRowsJson = SheetValueHelper.GetString(r, 20)
        }).OrderBy(x => x.DealerName).ToList();
    }

    public async Task<List<Dealer>> GetActiveAsync() => (await GetAllAsync()).Where(x => x.IsActive).ToList();
    public async Task<Dealer?> GetByIdAsync(string id) => (await GetAllAsync()).FirstOrDefault(x => x.Id == id);

    public async Task SaveAsync(Dealer dealer)
    {
        var existing = (await GetAllAsync()).FirstOrDefault(x => x.Id == dealer.Id);
        if (existing == null) dealer.Id = string.IsNullOrWhiteSpace(dealer.Id) ? Guid.NewGuid().ToString() : dealer.Id;
        else
        {
            existing.DealerName = dealer.DealerName; existing.ContactPerson = dealer.ContactPerson; existing.City = dealer.City;
            existing.Mobile = dealer.Mobile; existing.Email = dealer.Email; existing.IsActive = dealer.IsActive; existing.MarginPercent = dealer.MarginPercent;
            existing.YearlyTargetAmount = dealer.YearlyTargetAmount; existing.TargetYear = dealer.TargetYear; existing.TargetFromDate = dealer.TargetFromDate;
            existing.TargetToDate = dealer.TargetToDate; existing.TargetRowsJson = dealer.TargetRowsJson; existing.JoiningDate = dealer.JoiningDate;
            existing.GivesPss = dealer.GivesPss; existing.GivesApi = dealer.GivesApi; existing.PssApiChangeDate = dealer.PssApiChangeDate;
            existing.PssApiRemark = dealer.PssApiRemark; existing.LeftDate = dealer.LeftDate; existing.LeftRemark = dealer.LeftRemark; existing.Notes = dealer.Notes;
            dealer = existing;
        }
        await _googleSheetsService.UpsertRowByIdAsync("Dealers", dealer.Id, ToRow(dealer));
    }

    public Task DeleteAsync(string id) => _googleSheetsService.DeleteRowsByIdAsync("Dealers", new[] { id });

    private static IList<object> ToRow(Dealer x) => new List<object>
    {
        x.Id, x.DealerName, x.ContactPerson, x.City, x.Mobile, x.Email, x.IsActive, x.MarginPercent, x.YearlyTargetAmount,
        x.TargetYear == 0 ? DateTime.Today.Year : x.TargetYear, x.TargetFromDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        x.TargetToDate?.ToString("yyyy-MM-dd") ?? string.Empty, x.JoiningDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        x.GivesPss, x.GivesApi, x.PssApiChangeDate?.ToString("yyyy-MM-dd") ?? string.Empty, x.PssApiRemark,
        x.LeftDate?.ToString("yyyy-MM-dd") ?? string.Empty, x.LeftRemark, x.Notes, x.TargetRowsJson
    };
}
