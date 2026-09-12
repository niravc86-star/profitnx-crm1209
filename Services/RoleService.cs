using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class RoleService : IRoleService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private const string ReadRange = "Roles!A2:E";
    public RoleService(IGoogleSheetsService googleSheetsService) => _googleSheetsService = googleSheetsService;

    public async Task<List<RoleMaster>> GetAllAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(ReadRange);
        var roles = rows.Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0))).Select(r =>
        {
            var name = SheetValueHelper.GetString(r, 1);
            return new RoleMaster { Id = SheetValueHelper.GetString(r, 0), Name = name, Description = SheetValueHelper.GetString(r, 4), IsActive = SheetValueHelper.GetBool(r, 3), LandingPage = GetSafeLandingPage(name) };
        }).OrderBy(x => x.Name).ToList();
        if (roles.Any()) return roles;

        roles = new List<RoleMaster>
        {
            new() { Id="R-ADMIN", Name="Admin", Description="Full access", IsActive=true, LandingPage="/Dashboard" },
            new() { Id="R-SUPPORT", Name="Support", Description="Support inquiry entry and own status report", IsActive=true, LandingPage="/Inquiry" },
            new() { Id="R-SUPPORTHEAD", Name="SupportHead", Description="Support head monitoring and team reports", IsActive=true, LandingPage="/Inquiry" },
            new() { Id="R-PARTNER", Name="Partner", Description="Partner forwarded inquiry handling", IsActive=true, LandingPage="/Inquiry" },
            new() { Id="R-USER", Name="User", Description="Internal CRM user", IsActive=true, LandingPage="/Inquiry" }
        };
        await _googleSheetsService.UpsertRowsByIdAsync("Roles", roles.Select(x => (x.Id, ToRow(x))));
        return roles;
    }

    private static string GetSafeLandingPage(string roleName) => roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "/Dashboard" : "/Inquiry";
    public async Task<RoleMaster?> GetByIdAsync(string id) => (await GetAllAsync()).FirstOrDefault(x => x.Id == id);
    public async Task<RoleMaster?> GetByNameAsync(string name) => (await GetAllAsync()).FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public async Task SaveAsync(RoleMaster role)
    {
        var existing = (await GetAllAsync()).FirstOrDefault(x => x.Id == role.Id);
        if (existing == null) role.Id = string.IsNullOrWhiteSpace(role.Id) ? Guid.NewGuid().ToString() : role.Id;
        else { existing.Name = role.Name; existing.Description = role.Description; existing.IsActive = role.IsActive; role = existing; }
        role.LandingPage = GetSafeLandingPage(role.Name);
        await _googleSheetsService.UpsertRowByIdAsync("Roles", role.Id, ToRow(role));
    }

    public Task DeleteAsync(string id) => _googleSheetsService.DeleteRowsByIdAsync("Roles", new[] { id });
    private static IList<object> ToRow(RoleMaster x) => new List<object> { x.Id, x.Name, false, x.IsActive, x.Description };
}
