using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public class InquiryService : IInquiryService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly IUserService _users;
    private readonly IDealerService _dealers;
    private readonly INotificationService _notifications;
    private readonly IStockService _stock;
    private readonly IProductService _products;
    private const string ReadRange = "Inquiries!A2:BC";
    private const string UpdateReadRange = "InquiryUpdates!A2:L";

    public static readonly string[] StatusOptions =
    {
        "New Inquiry", "Follow Up", "Demo Scheduled", "Demo Done", "Negotiation",
        "Forwarded to Admin", "Forwarded to Partner", "Forwarded to User", "Sold", "Close"
    };
    public static readonly string[] QualityOptions = { "Genuine", "Not Genuine" };

    public InquiryService(IGoogleSheetsService sheets, IUserService users, IDealerService dealers,
        INotificationService notifications, IStockService stock, IProductService products)
    {
        _sheets = sheets; _users = users; _dealers = dealers; _notifications = notifications; _stock = stock; _products = products;
    }

    public async Task<List<Inquiry>> GetAllAsync()
    {
        var rows = await _sheets.ReadAsync(ReadRange);
        return rows.Where(x => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(x, 0)))
            .Select(MapInquiry).OrderByDescending(x => x.LastUpdated).ToList();
    }

    public async Task<List<InquiryUpdate>> GetUpdatesAsync(string inquiryId)
    {
        var rows = await _sheets.ReadAsync(UpdateReadRange);
        return rows.Where(r => SheetValueHelper.GetString(r, 1).Equals(inquiryId, StringComparison.OrdinalIgnoreCase))
            .Select(r => new InquiryUpdate
            {
                Id = SheetValueHelper.GetString(r, 0), InquiryId = SheetValueHelper.GetString(r, 1),
                UpdateDate = SheetValueHelper.GetDateTime(r, 2) ?? DateTime.Now, UserId = SheetValueHelper.GetString(r, 3),
                UserName = SheetValueHelper.GetString(r, 4), Status = SheetValueHelper.GetString(r, 5), Note = SheetValueHelper.GetString(r, 6),
                NextFollowUpDate = SheetValueHelper.GetNullableDate(r, 7), LicenseNumber = SheetValueHelper.GetString(r, 8),
                BillNo = SheetValueHelper.GetString(r, 9), BillDate = SheetValueHelper.GetNullableDate(r, 10), CloseReason = SheetValueHelper.GetString(r, 11)
            }).OrderByDescending(x => x.UpdateDate).ToList();
    }

    public async Task<List<Inquiry>> SearchAsync(InquiryFilterViewModel filter, string? role = null, string? userId = null)
    {
        // Role-scoped base set is enforced here so list/report/detail/access checks
        // cannot leak another role's inquiries via filters or direct IDs.
        IEnumerable<Inquiry> q = await GetVisibleForRoleAsync(role, userId);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            q = q.Where(x => x.FirmName.Contains(term, StringComparison.OrdinalIgnoreCase) || x.PersonName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.City.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Mobile1.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (x.ProductName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) || x.DealerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.BillNo.Contains(term, StringComparison.OrdinalIgnoreCase) || x.LicenseNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.SupportExecutiveName.Contains(term, StringComparison.OrdinalIgnoreCase) || x.InquiryQuality.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(x => x.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.InquiryQuality)) q = q.Where(x => x.InquiryQuality.Equals(filter.InquiryQuality, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.PartnerId)) q = q.Where(x => x.ForwardedToPartnerId.Equals(filter.PartnerId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.ProductName)) q = q.Where(x => (x.ProductName ?? string.Empty).Equals(filter.ProductName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.AssignedUserId)) q = q.Where(x => x.AssignedUserId.Equals(filter.AssignedUserId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.SupportUser))
        {
            var su = filter.SupportUser.Trim();
            q = q.Where(x => x.SupportExecutiveName.Equals(su, StringComparison.OrdinalIgnoreCase) || x.AssignedUserName.Equals(su, StringComparison.OrdinalIgnoreCase));
        }
        if (filter.FromDate.HasValue) q = q.Where(x => x.CreatedDate.Date >= filter.FromDate.Value.Date);
        if (filter.ToDate.HasValue) q = q.Where(x => x.CreatedDate.Date <= filter.ToDate.Value.Date);
        if (filter.TodayFollowUps)
        {
            var today = DateTime.Today;
            q = q.Where(x => x.NextFollowUpDate?.Date == today && !IsFinal(x.Status));
        }
        else
        {
            if (filter.FollowUpFromDate.HasValue) q = q.Where(x => x.NextFollowUpDate?.Date >= filter.FollowUpFromDate.Value.Date);
            if (filter.FollowUpToDate.HasValue) q = q.Where(x => x.NextFollowUpDate?.Date <= filter.FollowUpToDate.Value.Date);
        }
        if (filter.Focus?.Equals("Hot", StringComparison.OrdinalIgnoreCase) == true)
            q = q.Where(x => x.InquiryQuality.Equals("Genuine", StringComparison.OrdinalIgnoreCase) && (x.Status.Equals("Demo Done", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Negotiation", StringComparison.OrdinalIgnoreCase)));
        else if (filter.Focus?.Equals("MissingNextAction", StringComparison.OrdinalIgnoreCase) == true)
            q = q.Where(x => !IsFinal(x.Status) && !x.InquiryQuality.Equals("Not Genuine", StringComparison.OrdinalIgnoreCase) && !x.NextFollowUpDate.HasValue);
        return q.OrderByDescending(x => x.LastUpdated).ToList();
    }

    /// <summary>
    /// Central role-based inquiry visibility. Admin (and unknown roles treated as full access
    /// only when role is null/empty for internal callers) sees everything. Other roles are
    /// strictly limited to their ownership/assignment scope.
    /// </summary>
    public async Task<List<Inquiry>> GetVisibleForRoleAsync(string? role, string? userId)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return await GetAllAsync();

        if (role.Equals("Partner", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(userId))
            return await GetByPartnerAsync(userId);

        if (role.Equals("Support", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(userId))
            return await GetBySupportUserAsync(userId);

        if (role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase))
            return await GetBySupportHeadAsync(userId);

        if (role.Equals("User", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(userId))
            return await GetByUserAsync(userId);

        // Unknown custom roles: deny by default (empty) rather than leak all data.
        return new List<Inquiry>();
    }

    /// <summary>
    /// Support Head sees ONLY Support Team ownership work.
    /// Admin/Partner/User-created CRM inquiries must NOT appear here.
    ///
    /// Note: On create, SupportExecutiveName defaults to the creator name for ALL roles
    /// (including Admin). So a non-empty SupportExecutiveName alone is NOT ownership proof.
    /// We only trust SupportExecutiveName when it matches an actual Support / SupportHead user.
    /// </summary>
    public async Task<List<Inquiry>> GetBySupportHeadAsync(string? supportHeadUserId = null)
    {
        var all = await GetAllAsync();
        var supportUsers = (await _users.GetAllUsersAsync())
            .Where(u => u.Role.Equals("Support", StringComparison.OrdinalIgnoreCase)
                     || u.Role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var supportIds = supportUsers.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supportNames = supportUsers
            .SelectMany(u => new[] { u.FullName, u.Username })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return all.Where(x => IsSupportTeamOwnedInquiry(x, supportIds, supportNames)).ToList();
    }

    /// <summary>
    /// True only when the inquiry belongs to Support Team work — never merely because
    /// Admin created it or SupportExecutiveName was auto-filled with Admin's name.
    /// </summary>
    private static bool IsSupportTeamOwnedInquiry(
        Inquiry x,
        HashSet<string> supportIds,
        HashSet<string> supportNames)
    {
        // Primary ownership: created by Support role → InquirySource = "Support Team"
        if (!string.IsNullOrWhiteSpace(x.InquirySource)
            && x.InquirySource.Equals("Support Team", StringComparison.OrdinalIgnoreCase))
            return true;

        // Assigned / forwarded to a real Support or SupportHead user id
        if (!string.IsNullOrWhiteSpace(x.AssignedUserId) && supportIds.Contains(x.AssignedUserId))
            return true;
        if (!string.IsNullOrWhiteSpace(x.ForwardedToUserId) && supportIds.Contains(x.ForwardedToUserId))
            return true;

        // Name matches an actual Support / SupportHead account (not Admin/Partner names)
        if (!string.IsNullOrWhiteSpace(x.AssignedUserName) && supportNames.Contains(x.AssignedUserName.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(x.ForwardedToUserName) && supportNames.Contains(x.ForwardedToUserName.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(x.SupportExecutiveName) && supportNames.Contains(x.SupportExecutiveName.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(x.AttendedByName) && supportNames.Contains(x.AttendedByName.Trim())
            && !string.IsNullOrWhiteSpace(x.AttendedByRole)
            && (x.AttendedByRole.Equals("Support", StringComparison.OrdinalIgnoreCase)
                || x.AttendedByRole.Equals("SupportHead", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Explicitly marked attended-by-role Support AND source is support-ish is already
        // covered above. Do NOT accept AttendedByRole alone — Admin inquiries opened by
        // Support would leak.
        return false;
    }

    public async Task<List<Inquiry>> GetByPartnerAsync(string partnerId)

    {
        var partner = (await _users.GetAllUsersAsync()).FirstOrDefault(x => x.Id.Equals(partnerId, StringComparison.OrdinalIgnoreCase));
        return (await GetAllAsync()).Where(x => x.ForwardedToPartnerId.Equals(partnerId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(partner?.PartnerCode) && x.ForwardedToPartnerId.Equals(partner.PartnerCode, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(partner?.FullName) && x.ForwardedToPartnerName.Equals(partner.FullName, StringComparison.OrdinalIgnoreCase))
            || x.AssignedUserId.Equals(partnerId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<Inquiry>> GetBySupportUserAsync(string supportUserId)
    {
        var user = (await _users.GetAllUsersAsync()).FirstOrDefault(x => x.Id.Equals(supportUserId, StringComparison.OrdinalIgnoreCase));
        var fullName = user?.FullName ?? string.Empty;
        var userName = user?.Username ?? string.Empty;
        return (await GetAllAsync()).Where(x =>
            x.AssignedUserId.Equals(supportUserId, StringComparison.OrdinalIgnoreCase)
            || x.ForwardedToUserId.Equals(supportUserId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(fullName) && (
                x.AssignedUserName.Equals(fullName, StringComparison.OrdinalIgnoreCase)
                || x.ForwardedToUserName.Equals(fullName, StringComparison.OrdinalIgnoreCase)
                || x.SupportExecutiveName.Equals(fullName, StringComparison.OrdinalIgnoreCase)
                || x.AttendedByName.Equals(fullName, StringComparison.OrdinalIgnoreCase)))
            || (!string.IsNullOrWhiteSpace(userName) && (
                x.AssignedUserName.Equals(userName, StringComparison.OrdinalIgnoreCase)
                || x.SupportExecutiveName.Equals(userName, StringComparison.OrdinalIgnoreCase)))
        ).ToList();
    }

    private async Task<List<Inquiry>> GetByUserAsync(string userId)
    {
        var user = (await _users.GetAllUsersAsync()).FirstOrDefault(x => x.Id.Equals(userId, StringComparison.OrdinalIgnoreCase));
        return (await GetAllAsync()).Where(x => x.AssignedUserId.Equals(userId, StringComparison.OrdinalIgnoreCase)
            || x.ForwardedToUserId.Equals(userId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(user?.FullName) && (x.AssignedUserName.Equals(user.FullName, StringComparison.OrdinalIgnoreCase) || x.ForwardedToUserName.Equals(user.FullName, StringComparison.OrdinalIgnoreCase)))).ToList();
    }

    public async Task<Inquiry?> GetByIdAsync(string id) => (await GetAllAsync()).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task CreateAsync(InquiryFormViewModel model, string currentUserId, string currentUserName, string currentUserRole)
    {
        var inquiry = await BuildInquiryAsync(model, currentUserId, currentUserName, currentUserRole, model.Id ?? Guid.NewGuid().ToString());
        var allUsers = await _users.GetAllUsersAsync();
        if (currentUserRole.Equals("Support", StringComparison.OrdinalIgnoreCase) || currentUserRole.Equals("Partner", StringComparison.OrdinalIgnoreCase))
            SetForwarding(inquiry, "Admin", null, currentUserId, currentUserName, currentUserRole);
        else if (!string.IsNullOrWhiteSpace(inquiry.ForwardedToPartnerId)) SetForwarding(inquiry, "Partner", allUsers.FirstOrDefault(x => x.Id == inquiry.ForwardedToPartnerId), currentUserId, currentUserName, currentUserRole);
        else if (!string.IsNullOrWhiteSpace(inquiry.ForwardedToUserId)) SetForwarding(inquiry, "User", allUsers.FirstOrDefault(x => x.Id == inquiry.ForwardedToUserId), currentUserId, currentUserName, currentUserRole);

        if (inquiry.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)) await ApplySalePricingAsync(inquiry, null, null, currentUserId, currentUserName, currentUserRole);
        await _sheets.UpsertRowByIdAsync("Inquiries", inquiry.Id, ToRow(inquiry));
        await AppendUpdateAsync(inquiry.Id, currentUserId, currentUserName, inquiry.Status, string.IsNullOrWhiteSpace(inquiry.Remarks) ? "Inquiry created" : inquiry.Remarks, inquiry.NextFollowUpDate, inquiry.LicenseNumber, inquiry.BillNo, inquiry.BillDate, inquiry.CloseReason);
        await NotifyCreatedAsync(inquiry, allUsers, currentUserId, currentUserName, currentUserRole);
    }

    public async Task UpdateAsync(InquiryFormViewModel model, string currentUserId, string currentUserName, string currentUserRole)
    {
        var existing = string.IsNullOrWhiteSpace(model.Id) ? null : await GetByIdAsync(model.Id);
        if (existing == null) return;
        var updated = await BuildInquiryAsync(model, existing.AssignedUserId, existing.AssignedUserName, existing.AttendedByRole, existing.Id);
        updated.SupportExecutiveName = existing.SupportExecutiveName;
        updated.InquirySource = existing.InquirySource;
        updated.ForwardedByUserId = existing.ForwardedByUserId; updated.ForwardedByName = existing.ForwardedByName; updated.ForwardedByRole = existing.ForwardedByRole;
        updated.ForwardedDate = existing.ForwardedDate; updated.IsAttended = existing.IsAttended; updated.AttendedDate = existing.AttendedDate;
        updated.AttendedByUserId = existing.AttendedByUserId; updated.AttendedByName = existing.AttendedByName;
        updated.SoldByName = existing.SoldByName; updated.SoldByRole = existing.SoldByRole;
        updated.PriceType = existing.PriceType; updated.UnitPriceWithoutGst = existing.UnitPriceWithoutGst; updated.UnitPriceWithGst = existing.UnitPriceWithGst; updated.AppliedMarginPercent = existing.AppliedMarginPercent;
        updated.LastUpdated = DateTime.Now;
        if (updated.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)) await ApplySalePricingAsync(updated, model.AmountWithoutGst, model.AmountWithGst, currentUserId, currentUserName, currentUserRole);
        if (updated.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)) updated.ClosedDate ??= DateTime.Today;
        await _sheets.UpsertRowByIdAsync("Inquiries", updated.Id, ToRow(updated));
        var note = !string.IsNullOrWhiteSpace(updated.StatusReason) ? updated.StatusReason : !string.IsNullOrWhiteSpace(updated.Remarks) ? updated.Remarks : "Inquiry edited";
        await AppendUpdateAsync(updated.Id, currentUserId, currentUserName, updated.Status, note, updated.NextFollowUpDate, updated.LicenseNumber, updated.BillNo, updated.BillDate, updated.CloseReason);
        await NotifyUpdateAsync(updated, currentUserId, currentUserName, $"{currentUserName}: {note}");
    }

    public Task DeleteAsync(string id) => _sheets.DeleteRowsByIdAsync("Inquiries", new[] { id });

    public async Task UpdateStatusAsync(string inquiryId, string status, string note, string currentUserId, string currentUserName, string currentUserRole,
        DateTime? nextFollowUp, DateTime? demoScheduledDate, DateTime? demoDoneDate, string? licenseNumber, string? billNo, DateTime? billDate,
        string? closeReason, decimal? amountWithoutGst = null, decimal? amountWithGst = null, string? inquiryQuality = null,
        string? soldProductName = null, int? soldLicenses = null, string? forwardToPartnerId = null, string? forwardToUserId = null)
    {
        var inquiry = await GetByIdAsync(inquiryId); if (inquiry == null) return;
        var allUsers = await _users.GetAllUsersAsync();
        if (!string.IsNullOrWhiteSpace(status)) inquiry.Status = status.Trim();
        inquiry.StatusReason = note ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(inquiryQuality)) inquiry.InquiryQuality = inquiryQuality.Trim();
        inquiry.NextFollowUpDate = nextFollowUp;
        if (demoScheduledDate.HasValue) inquiry.DemoScheduledDate = demoScheduledDate;
        if (demoDoneDate.HasValue) inquiry.DemoDoneDate = demoDoneDate;
        if (!string.IsNullOrWhiteSpace(soldProductName)) inquiry.ProductName = soldProductName.Trim();
        if (soldLicenses.GetValueOrDefault() > 0) inquiry.LicensesPurchased = soldLicenses!.Value;
        if (!string.IsNullOrWhiteSpace(licenseNumber)) inquiry.LicenseNumber = licenseNumber.Trim();
        if (!string.IsNullOrWhiteSpace(billNo)) inquiry.BillNo = billNo.Trim();
        if (billDate.HasValue) inquiry.BillDate = billDate;
        if (!string.IsNullOrWhiteSpace(closeReason)) inquiry.CloseReason = closeReason.Trim();
        inquiry.LastUpdated = DateTime.Now;
        inquiry.IsAttended = true; inquiry.AttendedDate = DateTime.Now; inquiry.AttendedByUserId = currentUserId; inquiry.AttendedByName = currentUserName;

        if (inquiry.Status.Equals("Forwarded to Admin", StringComparison.OrdinalIgnoreCase)) SetForwarding(inquiry, "Admin", null, currentUserId, currentUserName, currentUserRole);
        else if (inquiry.Status.Equals("Forwarded to Partner", StringComparison.OrdinalIgnoreCase))
        {
            var target = allUsers.FirstOrDefault(x => x.Id.Equals(forwardToPartnerId ?? string.Empty, StringComparison.OrdinalIgnoreCase) && x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase));
            SetForwarding(inquiry, "Partner", target, currentUserId, currentUserName, currentUserRole);
        }
        else if (inquiry.Status.Equals("Forwarded to User", StringComparison.OrdinalIgnoreCase))
        {
            var target = allUsers.FirstOrDefault(x => x.Id.Equals(forwardToUserId ?? string.Empty, StringComparison.OrdinalIgnoreCase) && x.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
            SetForwarding(inquiry, "User", target, currentUserId, currentUserName, currentUserRole);
        }
        else if (inquiry.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)) await ApplySalePricingAsync(inquiry, amountWithoutGst, amountWithGst, currentUserId, currentUserName, currentUserRole);
        else if (inquiry.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)) inquiry.ClosedDate = DateTime.Today;

        await _sheets.UpsertRowByIdAsync("Inquiries", inquiry.Id, ToRow(inquiry));
        await AppendUpdateAsync(inquiry.Id, currentUserId, currentUserName,
            string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(inquiryQuality) ? "Genuine Status: " + inquiry.InquiryQuality : inquiry.Status,
            note, nextFollowUp, licenseNumber, billNo, billDate, closeReason);

        if (inquiry.Status.StartsWith("Forwarded to ", StringComparison.OrdinalIgnoreCase)) await NotifyForwardedAsync(inquiry, allUsers, currentUserId);
        else await NotifyUpdateAsync(inquiry, currentUserId, currentUserName, $"{currentUserName} updated inquiry status to {inquiry.Status}. {note}");
    }

    public async Task<List<Inquiry>> GetPendingAttentionAsync(string role, string userId)
    {
        var now = DateTime.Now;
        var scoped = await SearchAsync(new InquiryFilterViewModel(), role, userId);
        // Change (2026-08-06): a next-follow-up date is stored as a date only (no time,
        // see ToRow/FromRow - it's saved as "yyyy-MM-dd" and read back as midnight of that
        // day). Comparing that against "now" with "<" meant an inquiry due TODAY was already
        // being treated as overdue from 12:00 AM onward, so by the evening it showed several
        // hours "Pending for..." even though the user still had the whole day to follow up.
        // Fixed to only flag it once the due DATE itself has fully passed (i.e. from the next
        // day onward) - not while today is still in progress.
        return scoped.Where(x => !IsFinal(x.Status) &&
            ((!x.IsAttended && IsForwardedTo(x, role, userId)) || (x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date < now.Date)))
            .OrderBy(x => !x.IsAttended ? x.ForwardedDate ?? x.LastUpdated : x.NextFollowUpDate)
            .Take(25).ToList();
    }

    public async Task MarkAttendedAsync(string inquiryId, string userId, string userName, string role)
    {
        var inquiry = await GetByIdAsync(inquiryId); if (inquiry == null || inquiry.IsAttended || !IsForwardedTo(inquiry, role, userId)) return;
        inquiry.IsAttended = true; inquiry.AttendedDate = DateTime.Now; inquiry.AttendedByUserId = userId; inquiry.AttendedByName = userName; inquiry.LastUpdated = DateTime.Now;
        await _sheets.UpsertRowByIdAsync("Inquiries", inquiry.Id, ToRow(inquiry));
        await AppendUpdateAsync(inquiry.Id, userId, userName, "Attended", $"Inquiry attended by {userName}", inquiry.NextFollowUpDate, inquiry.LicenseNumber, inquiry.BillNo, inquiry.BillDate, inquiry.CloseReason);
    }

    public async Task<int> GetMonthlySubmittedCountAsync(string userId, DateTime month)
        => (await GetAllAsync()).Count(x => x.AssignedUserId.Equals(userId, StringComparison.OrdinalIgnoreCase) && x.CreatedDate.Year == month.Year && x.CreatedDate.Month == month.Month);

    private async Task<Inquiry> BuildInquiryAsync(InquiryFormViewModel model, string ownerId, string ownerName, string ownerRole, string id)
    {
        var allUsers = await _users.GetAllUsersAsync(); var allDealers = await _dealers.GetAllAsync();
        var partner = allUsers.FirstOrDefault(x => (x.Id.Equals(model.ForwardedToPartnerId ?? string.Empty, StringComparison.OrdinalIgnoreCase) || x.PartnerCode.Equals(model.ForwardedToPartnerId ?? string.Empty, StringComparison.OrdinalIgnoreCase)) && x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase));
        var user = allUsers.FirstOrDefault(x => x.Id.Equals(model.ForwardedToUserId ?? string.Empty, StringComparison.OrdinalIgnoreCase) && x.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
        var dealer = allDealers.FirstOrDefault(x => x.Id.Equals(model.DealerId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? allDealers.FirstOrDefault(x => x.Id.Equals(partner?.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase) || x.DealerName.Equals(partner?.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase) || x.ContactPerson.Equals(partner?.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        return new Inquiry
        {
            Id=id, CreatedDate=model.CreatedDate==default?DateTime.Today:model.CreatedDate, FirmName=model.FirmName??string.Empty, PersonName=model.PersonName??string.Empty,
            City=model.City??string.Empty, Mobile1=model.Mobile1??string.Empty, Mobile2=model.Mobile2??string.Empty, Email1=model.Email1??string.Empty, Email2=model.Email2??string.Empty,
            ProductName=model.ProductName??string.Empty, VersionType=model.VersionType??string.Empty, Remarks=model.Remarks??string.Empty,
            Status=string.IsNullOrWhiteSpace(model.Status)?"New Inquiry":model.Status, StatusReason=model.StatusReason??string.Empty,
            ForwardedToPartnerId=partner?.Id??string.Empty, ForwardedToPartnerName=dealer?.DealerName??partner?.FullName??string.Empty,
            ForwardedToUserId=user?.Id??string.Empty, ForwardedToUserName=user?.FullName??string.Empty,
            AssignedUserId=ownerId, AssignedUserName=ownerName, AttendedByRole=ownerRole, NextFollowUpDate=model.NextFollowUpDate,
            DemoScheduledDate=model.DemoScheduledDate, DemoDoneDate=model.DemoDoneDate, LicensesPurchased=model.LicensesPurchased,
            LicenseNumber=model.LicenseNumber??string.Empty, BillNo=model.BillNo??string.Empty, BillDate=model.BillDate,
            AmountWithoutGst=model.AmountWithoutGst, AmountWithGst=model.AmountWithGst,
            SoldDate=model.Status=="Sold"?(model.BillDate??DateTime.Today):null, ClosedDate=model.Status=="Close"?DateTime.Today:null,
            CloseReason=model.CloseReason??string.Empty, CustomerInfo=model.CustomerInfo??string.Empty, DealerId=dealer?.Id??string.Empty, DealerName=dealer?.DealerName??string.Empty,
            LastUpdated=DateTime.Now, SupportExecutiveName=string.IsNullOrWhiteSpace(model.SupportExecutiveName)?ownerName:model.SupportExecutiveName,
            InquirySource=string.IsNullOrWhiteSpace(model.InquirySource)?(ownerRole=="Support"?"Support Team":ownerRole=="Partner"?"Partner":"CRM"):model.InquirySource,
            InquiryQuality=model.InquiryQuality??string.Empty
        };
    }

    private async Task ApplySalePricingAsync(Inquiry inquiry, decimal? fallbackWithout, decimal? fallbackWith, string userId, string userName, string role)
    {
        var qty=Math.Max(1,inquiry.LicensesPurchased); var productList=await _products.GetAllAsync(); var product=productList.FirstOrDefault(x=>x.Name.Equals(inquiry.ProductName??string.Empty,StringComparison.OrdinalIgnoreCase)&&x.Version.Equals(inquiry.VersionType??string.Empty,StringComparison.OrdinalIgnoreCase))??productList.FirstOrDefault(x=>x.Name.Equals(inquiry.ProductName??string.Empty,StringComparison.OrdinalIgnoreCase));
        var users=await _users.GetAllUsersAsync(); var dealers=await _dealers.GetAllAsync();
        var partner=users.FirstOrDefault(x=>x.Id.Equals(inquiry.ForwardedToPartnerId,StringComparison.OrdinalIgnoreCase)||x.PartnerCode.Equals(inquiry.ForwardedToPartnerId,StringComparison.OrdinalIgnoreCase))
            ??(role.Equals("Partner",StringComparison.OrdinalIgnoreCase)?users.FirstOrDefault(x=>x.Id.Equals(userId,StringComparison.OrdinalIgnoreCase)):null);
        var dealer=dealers.FirstOrDefault(x=>x.Id.Equals(partner?.PartnerCode??string.Empty,StringComparison.OrdinalIgnoreCase)||x.Id.Equals(inquiry.DealerId,StringComparison.OrdinalIgnoreCase)||x.DealerName.Equals(inquiry.ForwardedToPartnerName,StringComparison.OrdinalIgnoreCase)||x.DealerName.Equals(partner?.FullName??string.Empty,StringComparison.OrdinalIgnoreCase)||x.ContactPerson.Equals(partner?.FullName??string.Empty,StringComparison.OrdinalIgnoreCase));
        // Pricing depends on WHO is actually recording/closing the sale, not on who the
        // inquiry happened to be forwarded to. Admin or User closing it => Direct Price.
        // Only a Partner-role login closing it => Partner Price (with their margin deducted).
        // This also keeps SoldByRole/SoldByName (used for target-achievement attribution
        // on the dashboards) consistent with the price that was actually applied.
        var isPartner=role.Equals("Partner",StringComparison.OrdinalIgnoreCase); var margin=Math.Clamp(dealer?.MarginPercent??partner?.MarginPercent??0m,0m,100m);
        var baseWithout=product==null?(fallbackWithout.GetValueOrDefault()>0?fallbackWithout.Value:inquiry.AmountWithoutGst/qty):(isPartner?product.PartnerPriceWithoutGst:product.PriceWithoutGst);
        var baseWith=product==null?(fallbackWith.GetValueOrDefault()>0?fallbackWith.Value:inquiry.AmountWithGst/qty):(isPartner?product.PartnerPriceWithGst:product.PriceWithGst);
        inquiry.PriceType=isPartner?"Partner":"Direct Customer"; inquiry.UnitPriceWithoutGst=baseWithout; inquiry.UnitPriceWithGst=baseWith; inquiry.AppliedMarginPercent=isPartner?margin:0m;
        inquiry.AmountWithoutGst=Math.Round(baseWithout*qty*(isPartner?(1m-margin/100m):1m),2); inquiry.AmountWithGst=Math.Round(baseWith*qty*(isPartner?(1m-margin/100m):1m),2);
        inquiry.SoldDate=inquiry.BillDate??DateTime.Today; inquiry.SoldByName=userName; inquiry.SoldByRole=role;
        if(isPartner&&string.IsNullOrWhiteSpace(inquiry.ForwardedToPartnerId)&&partner!=null){inquiry.ForwardedToPartnerId=partner.Id;inquiry.ForwardedToPartnerName=string.IsNullOrWhiteSpace(inquiry.ForwardedToPartnerName)?partner.FullName:inquiry.ForwardedToPartnerName;}
        if(dealer!=null){inquiry.DealerId=dealer.Id;inquiry.DealerName=dealer.DealerName;inquiry.ForwardedToPartnerName=dealer.DealerName;}
        if(isPartner) await _stock.RegisterSoldAsync(inquiry.ProductName??string.Empty,inquiry.VersionType,inquiry.ForwardedToPartnerId,inquiry.ForwardedToPartnerName,qty,inquiry.LicenseNumber,inquiry.BillNo,inquiry.BillDate);
    }

    private static void SetForwarding(Inquiry inquiry, string role, AppUser? target, string byId, string byName, string byRole)
    {
        if ((role.Equals("Partner", StringComparison.OrdinalIgnoreCase) || role.Equals("User", StringComparison.OrdinalIgnoreCase)) && target == null)
            throw new InvalidOperationException($"A valid active {role.ToLowerInvariant()} target is required.");
        inquiry.ForwardedToRole=role; inquiry.ForwardedByUserId=byId; inquiry.ForwardedByName=byName; inquiry.ForwardedByRole=byRole; inquiry.ForwardedDate=DateTime.Now;
        inquiry.IsAttended=false; inquiry.AttendedDate=null; inquiry.AttendedByUserId=string.Empty; inquiry.AttendedByName=string.Empty;
        if(role.Equals("Partner",StringComparison.OrdinalIgnoreCase)){inquiry.ForwardedToPartnerId=target?.Id??string.Empty;inquiry.ForwardedToPartnerName=target?.FullName??string.Empty;inquiry.ForwardedToUserId=string.Empty;inquiry.ForwardedToUserName=string.Empty;}
        else if(role.Equals("User",StringComparison.OrdinalIgnoreCase)){inquiry.ForwardedToUserId=target?.Id??string.Empty;inquiry.ForwardedToUserName=target?.FullName??string.Empty;}
        else if(role.Equals("Admin",StringComparison.OrdinalIgnoreCase)){inquiry.ForwardedToUserId=string.Empty;inquiry.ForwardedToUserName=string.Empty;}
    }

    private async Task NotifyCreatedAsync(Inquiry inquiry,List<AppUser> users,string currentUserId,string currentUserName,string currentUserRole)
    {
        if(currentUserRole.Equals("Support",StringComparison.OrdinalIgnoreCase)){await _notifications.NotifySupportToAdminAsync(inquiry,currentUserName,users.FirstOrDefault(x=>x.Role.Equals("Admin",StringComparison.OrdinalIgnoreCase))?.Email??string.Empty);return;}
        if(inquiry.ForwardedToRole.Equals("Partner",StringComparison.OrdinalIgnoreCase)){var p=users.FirstOrDefault(x=>x.Id==inquiry.ForwardedToPartnerId);if(p!=null)await _notifications.NotifyAdminToPartnerAsync(inquiry,p.FullName,p.Mobile,p.Email);return;}
        if(inquiry.ForwardedToRole.Equals("User",StringComparison.OrdinalIgnoreCase)){var u=users.FirstOrDefault(x=>x.Id==inquiry.ForwardedToUserId);if(u!=null)await _notifications.NotifyAdminToUserAsync(inquiry,u.FullName,u.Mobile,u.Email);return;}
        foreach(var a in users.Where(x=>x.IsActive&&x.Role.Equals("Admin",StringComparison.OrdinalIgnoreCase)&&x.Id!=currentUserId))await _notifications.NotifyInquiryCreatedAsync(inquiry,a.FullName,a.Mobile,a.Email,currentUserName,currentUserRole);
    }

    private async Task NotifyForwardedAsync(Inquiry inquiry,List<AppUser> users,string currentUserId)
    {
        if(inquiry.ForwardedToRole.Equals("Admin",StringComparison.OrdinalIgnoreCase)){foreach(var a in users.Where(x=>x.IsActive&&x.Role.Equals("Admin",StringComparison.OrdinalIgnoreCase)&&x.Id!=currentUserId))await _notifications.NotifyInquiryForwardAsync(inquiry,a.FullName,a.Mobile,a.Email);}
        else if(inquiry.ForwardedToRole.Equals("Partner",StringComparison.OrdinalIgnoreCase)){var p=users.FirstOrDefault(x=>x.Id==inquiry.ForwardedToPartnerId);if(p!=null)await _notifications.NotifyAdminToPartnerAsync(inquiry,p.FullName,p.Mobile,p.Email);}
        else {var u=users.FirstOrDefault(x=>x.Id==inquiry.ForwardedToUserId);if(u!=null)await _notifications.NotifyAdminToUserAsync(inquiry,u.FullName,u.Mobile,u.Email);}
    }

    private async Task NotifyUpdateAsync(Inquiry inquiry,string currentUserId,string currentUserName,string note)
    {
        var users=await _users.GetAllUsersAsync();var sent=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in new[]{inquiry.ForwardedToPartnerId,inquiry.ForwardedToUserId,inquiry.AssignedUserId}.Where(x=>!string.IsNullOrWhiteSpace(x)))
        {var u=users.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase));if(u!=null&&u.Id!=currentUserId&&sent.Add(u.Id)){if(u.Role.Equals("Support",StringComparison.OrdinalIgnoreCase))await _notifications.NotifyInquiryUpdateCrmOnlyAsync(inquiry,u.FullName,note);else await _notifications.NotifyInquiryUpdateAsync(inquiry,u.FullName,u.Mobile,u.Email,note);}}
        foreach(var a in users.Where(x=>x.IsActive&&x.Role.Equals("Admin",StringComparison.OrdinalIgnoreCase)&&x.Id!=currentUserId&&sent.Add(x.Id)))await _notifications.NotifyInquiryUpdateAsync(inquiry,a.FullName,a.Mobile,a.Email,note);
    }

    private async Task AppendUpdateAsync(string inquiryId,string userId,string userName,string status,string? note,DateTime? next,string? license,string? bill,DateTime? billDate,string? close)
        => await _sheets.AppendAsync("InquiryUpdates",new List<object>{Guid.NewGuid().ToString(),inquiryId,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),userId,userName,status,note??string.Empty,next?.ToString("yyyy-MM-dd")??string.Empty,license??string.Empty,bill??string.Empty,billDate?.ToString("yyyy-MM-dd")??string.Empty,close??string.Empty});

    private static bool IsFinal(string? status)=>status?.Equals("Sold",StringComparison.OrdinalIgnoreCase)==true||status?.Equals("Close",StringComparison.OrdinalIgnoreCase)==true||status?.Equals("Closed",StringComparison.OrdinalIgnoreCase)==true;
    private static bool IsForwardedTo(Inquiry x,string role,string userId)=>
        (x.ForwardedToRole.Equals("Admin",StringComparison.OrdinalIgnoreCase)&&role.Equals("Admin",StringComparison.OrdinalIgnoreCase))||
        (x.ForwardedToRole.Equals("Partner",StringComparison.OrdinalIgnoreCase)&&role.Equals("Partner",StringComparison.OrdinalIgnoreCase)&&x.ForwardedToPartnerId.Equals(userId,StringComparison.OrdinalIgnoreCase))||
        (x.ForwardedToRole.Equals("User",StringComparison.OrdinalIgnoreCase)&&x.ForwardedToUserId.Equals(userId,StringComparison.OrdinalIgnoreCase));

    private static IList<object> ToRow(Inquiry x)=>new List<object>{
        x.Id,x.CreatedDate.ToString("yyyy-MM-dd"),x.FirmName,x.PersonName,x.City,x.Mobile1,x.Mobile2,x.Email1,x.Email2,x.ProductName??string.Empty,x.VersionType,x.Remarks,x.Status,x.StatusReason,
        x.ForwardedToPartnerId,x.ForwardedToPartnerName,x.AssignedUserId,x.AssignedUserName,x.AttendedByRole,x.NextFollowUpDate?.ToString("yyyy-MM-dd")??string.Empty,x.DemoScheduledDate?.ToString("yyyy-MM-dd")??string.Empty,
        x.DemoDoneDate?.ToString("yyyy-MM-dd")??string.Empty,x.LicensesPurchased,x.LicenseNumber,x.BillNo,x.BillDate?.ToString("yyyy-MM-dd")??string.Empty,x.AmountWithoutGst,x.AmountWithGst,x.SoldDate?.ToString("yyyy-MM-dd")??string.Empty,
        x.ClosedDate?.ToString("yyyy-MM-dd")??string.Empty,x.CloseReason,x.CustomerInfo,x.DealerId,x.DealerName,x.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss"),x.SupportExecutiveName,x.InquirySource,x.SoldByName,x.SoldByRole,x.InquiryQuality,
        x.ForwardedToUserId,x.ForwardedToUserName,x.ForwardedToRole,x.ForwardedByUserId,x.ForwardedByName,x.ForwardedByRole,x.ForwardedDate?.ToString("yyyy-MM-dd HH:mm:ss")??string.Empty,x.IsAttended,
        x.AttendedDate?.ToString("yyyy-MM-dd HH:mm:ss")??string.Empty,x.AttendedByUserId,x.AttendedByName,x.PriceType,x.UnitPriceWithoutGst,x.UnitPriceWithGst,x.AppliedMarginPercent};

    private static Inquiry MapInquiry(IList<object> r)=>new(){
        Id=SheetValueHelper.GetString(r,0),CreatedDate=SheetValueHelper.GetDate(r,1,DateTime.Today),FirmName=SheetValueHelper.GetString(r,2),PersonName=SheetValueHelper.GetString(r,3),City=SheetValueHelper.GetString(r,4),Mobile1=SheetValueHelper.GetString(r,5),Mobile2=SheetValueHelper.GetString(r,6),Email1=SheetValueHelper.GetString(r,7),Email2=SheetValueHelper.GetString(r,8),ProductName=SheetValueHelper.GetString(r,9),VersionType=SheetValueHelper.GetString(r,10),Remarks=SheetValueHelper.GetString(r,11),Status=SheetValueHelper.GetString(r,12),StatusReason=SheetValueHelper.GetString(r,13),ForwardedToPartnerId=SheetValueHelper.GetString(r,14),ForwardedToPartnerName=SheetValueHelper.GetString(r,15),AssignedUserId=SheetValueHelper.GetString(r,16),AssignedUserName=SheetValueHelper.GetString(r,17),AttendedByRole=SheetValueHelper.GetString(r,18),NextFollowUpDate=SheetValueHelper.GetNullableDate(r,19),DemoScheduledDate=SheetValueHelper.GetNullableDate(r,20),DemoDoneDate=SheetValueHelper.GetNullableDate(r,21),LicensesPurchased=SheetValueHelper.GetInt(r,22),LicenseNumber=SheetValueHelper.GetString(r,23),BillNo=SheetValueHelper.GetString(r,24),BillDate=SheetValueHelper.GetNullableDate(r,25),AmountWithoutGst=SheetValueHelper.GetDecimal(r,26),AmountWithGst=SheetValueHelper.GetDecimal(r,27),SoldDate=SheetValueHelper.GetNullableDate(r,28),ClosedDate=SheetValueHelper.GetNullableDate(r,29),CloseReason=SheetValueHelper.GetString(r,30),CustomerInfo=SheetValueHelper.GetString(r,31),DealerId=SheetValueHelper.GetString(r,32),DealerName=SheetValueHelper.GetString(r,33),LastUpdated=SheetValueHelper.GetDateTime(r,34)??DateTime.Now,SupportExecutiveName=SheetValueHelper.GetString(r,35),InquirySource=SheetValueHelper.GetString(r,36),SoldByName=SheetValueHelper.GetString(r,37),SoldByRole=SheetValueHelper.GetString(r,38),InquiryQuality=SheetValueHelper.GetString(r,39),ForwardedToUserId=SheetValueHelper.GetString(r,40),ForwardedToUserName=SheetValueHelper.GetString(r,41),ForwardedToRole=SheetValueHelper.GetString(r,42),ForwardedByUserId=SheetValueHelper.GetString(r,43),ForwardedByName=SheetValueHelper.GetString(r,44),ForwardedByRole=SheetValueHelper.GetString(r,45),ForwardedDate=SheetValueHelper.GetDateTime(r,46),IsAttended=r.Count<=47||SheetValueHelper.GetBool(r,47,true),AttendedDate=SheetValueHelper.GetDateTime(r,48),AttendedByUserId=SheetValueHelper.GetString(r,49),AttendedByName=SheetValueHelper.GetString(r,50),PriceType=SheetValueHelper.GetString(r,51),UnitPriceWithoutGst=SheetValueHelper.GetDecimal(r,52),UnitPriceWithGst=SheetValueHelper.GetDecimal(r,53),AppliedMarginPercent=SheetValueHelper.GetDecimal(r,54)};
}
