using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class UserService : IUserService
{
    private readonly IGoogleSheetsService _googleSheetsService;
    private const string ReadRange = "Users!A2:U";

    public UserService(IGoogleSheetsService googleSheetsService) => _googleSheetsService = googleSheetsService;

    public async Task<AppUser?> ValidateUserAsync(string username, string password)
    {
        var user = (await GetAllUsersAsync()).FirstOrDefault(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && x.IsActive);
        if (user == null || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(user.PasswordHash)) return null;
        if (user.PasswordHash == password) return user;
        // Backward compatibility only: older accounts saved before this update may still hold a
        // bcrypt hash. Once that user's password is next changed/saved it is stored as plain text.
        try { return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null; }
        catch { return null; }
    }

    public async Task<List<AppUser>> GetAllUsersAsync()
    {
        var rows = await _googleSheetsService.ReadAsync(ReadRange);
        return rows.Where(r => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 0)))
            .Select(r => new AppUser
            {
                Id = SheetValueHelper.GetString(r, 0), FullName = SheetValueHelper.GetString(r, 1), Username = SheetValueHelper.GetString(r, 2),
                PasswordHash = SheetValueHelper.GetString(r, 3), Role = SheetValueHelper.GetString(r, 4), PartnerCode = SheetValueHelper.GetString(r, 5),
                Mobile = SheetValueHelper.GetString(r, 6), Email = SheetValueHelper.GetString(r, 7), City = SheetValueHelper.GetString(r, 8),
                IsActive = SheetValueHelper.GetBool(r, 9), TargetAmount = SheetValueHelper.GetDecimal(r, 10), MarginPercent = SheetValueHelper.GetDecimal(r, 11),
                Notes = SheetValueHelper.GetString(r, 12), TargetFromDate = SheetValueHelper.GetNullableDate(r, 13), TargetToDate = SheetValueHelper.GetNullableDate(r, 14),
                SmtpHost = SheetValueHelper.GetString(r, 15), SmtpPort = SheetValueHelper.GetInt(r, 16) == 0 ? 587 : SheetValueHelper.GetInt(r, 16),
                SmtpUsername = SheetValueHelper.GetString(r, 17), SmtpPassword = SheetValueHelper.GetString(r, 18),
                SmtpEnableSsl = r.Count <= 19 || SheetValueHelper.GetBool(r, 19),
                ThemePreference = string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 20)) ? "classic" : SheetValueHelper.GetString(r, 20)
            }).OrderBy(x => x.Role).ThenBy(x => x.FullName).ToList();
    }

    public async Task<List<AppUser>> GetActiveUsersAsync() => (await GetAllUsersAsync()).Where(x => x.IsActive).ToList();
    public async Task<List<AppUser>> GetPartnersAsync() => (await GetAllUsersAsync()).Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase) && x.IsActive).ToList();
    public async Task<AppUser?> GetByIdAsync(string id) => (await GetAllUsersAsync()).FirstOrDefault(x => x.Id == id);

    public async Task SetActiveAsync(string id, bool isActive)
    {
        var user = await GetByIdAsync(id);
        if (user == null) return;
        user.IsActive = isActive;
        await _googleSheetsService.UpsertRowByIdAsync("Users", user.Id, ToRow(user));
    }

    // Saves the user's chosen UI theme against their own account (credential-wise),
    // so it is restored automatically the next time they log in on any device.
    public async Task SetThemePreferenceAsync(string id, string theme)
    {
        var user = await GetByIdAsync(id);
        if (user == null) return;
        user.ThemePreference = string.IsNullOrWhiteSpace(theme) ? "classic" : theme.Trim();
        await _googleSheetsService.UpsertRowByIdAsync("Users", user.Id, ToRow(user));
    }

    public Task DeleteAsync(string id) => _googleSheetsService.DeleteRowsByIdAsync("Users", new[] { id });

    public async Task SaveAsync(AppUser user, string? plainPassword = null)
    {
        var users = await GetAllUsersAsync();
        if (users.Any(x => x.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase) && !x.Id.Equals(user.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Username already exists. Please use a different username.");

        var existing = users.FirstOrDefault(x => x.Id == user.Id);
        if (existing == null)
        {
            user.Id = string.IsNullOrWhiteSpace(user.Id) ? Guid.NewGuid().ToString() : user.Id;
            // Stored as entered — no hashing — so Admin can see and edit it again later.
            user.PasswordHash = string.IsNullOrWhiteSpace(plainPassword) ? "123456" : plainPassword.Trim();
        }
        else
        {
            existing.FullName = user.FullName; existing.Username = user.Username; existing.Role = user.Role; existing.PartnerCode = user.PartnerCode;
            existing.Mobile = user.Mobile; existing.Email = user.Email; existing.City = user.City; existing.IsActive = user.IsActive;
            existing.TargetAmount = user.TargetAmount; existing.MarginPercent = user.MarginPercent; existing.TargetFromDate = user.TargetFromDate;
            existing.TargetToDate = user.TargetToDate; existing.Notes = user.Notes; existing.SmtpHost = user.SmtpHost;
            existing.SmtpPort = user.SmtpPort <= 0 ? 587 : user.SmtpPort; existing.SmtpUsername = user.SmtpUsername;
            if (!string.IsNullOrWhiteSpace(user.SmtpPassword)) existing.SmtpPassword = user.SmtpPassword; existing.SmtpEnableSsl = user.SmtpEnableSsl;
            if (!string.IsNullOrWhiteSpace(plainPassword))
            {
                // Stored as entered — no hashing — so it can be shown again next time Admin edits this user.
                existing.PasswordHash = plainPassword.Trim();
            }
            // Theme preference is managed separately (SetThemePreferenceAsync) so the admin edit form never resets it.
            user = existing;
        }
        await _googleSheetsService.UpsertRowByIdAsync("Users", user.Id, ToRow(user));
    }

    private static IList<object> ToRow(AppUser x) => new List<object>
    {
        x.Id, x.FullName, x.Username, x.PasswordHash, x.Role, x.PartnerCode, x.Mobile, x.Email, x.City, x.IsActive,
        x.TargetAmount, x.MarginPercent, x.Notes, x.TargetFromDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        x.TargetToDate?.ToString("yyyy-MM-dd") ?? string.Empty, x.SmtpHost, x.SmtpPort <= 0 ? 587 : x.SmtpPort,
        x.SmtpUsername, x.SmtpPassword, x.SmtpEnableSsl, string.IsNullOrWhiteSpace(x.ThemePreference) ? "classic" : x.ThemePreference
    };
}
