using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;
using System.Security.Cryptography;
using System.Text;

namespace ProfitNx.CRM.Services;

public class ImplementationService : IImplementationService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly IUserService _users;
    private readonly INotificationService _notifications;
    private readonly IInquiryService _inquiries;
    private readonly IConfiguration _configuration;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool schemaReady;

    private const string CaseSheet = "ImplementationCases";
    private const string ScheduleSheet = "ImplementationSchedules";
    private const string ActivitySheet = "ImplementationActivities";
    private const string OtpSheet = "ImplementationOtpApprovals";
    // FIX (2026-08-17): CaseHeaders() already listed 66 columns (up to "BN") before
    // today's 4 new Manual* columns (now 70, up to "BR"), but this read range
    // stopped at "BI" (61 cols) - so StatusBeforePause and its neighbours were
    // already being silently dropped on every read, and the new Manual fields
    // would have suffered the same fate. Widened to cover every header column.
    private const string CaseReadRange = CaseSheet + "!A2:BR";
    private const string ScheduleReadRange = ScheduleSheet + "!A2:S";
    private const string ActivityReadRange = ActivitySheet + "!A2:K";
    private const string OtpReadRange = OtpSheet + "!A2:M";

    public ImplementationService(IGoogleSheetsService sheets, IUserService users, INotificationService notifications, IInquiryService inquiries, IConfiguration configuration)
    {
        _sheets = sheets;
        _users = users;
        _notifications = notifications;
        _inquiries = inquiries;
        _configuration = configuration;
    }

    public async Task EnsureSchemaAsync()
    {
        if (schemaReady) return;
        await SchemaLock.WaitAsync();
        try
        {
            if (schemaReady) return;
            await _sheets.EnsureSheetAsync(CaseSheet, CaseHeaders());
            await _sheets.EnsureSheetAsync(ScheduleSheet, ScheduleHeaders());
            await _sheets.EnsureSheetAsync(ActivitySheet, ActivityHeaders());
            await _sheets.EnsureSheetAsync(OtpSheet, OtpHeaders());
            schemaReady = true;
        }
        finally { SchemaLock.Release(); }
    }

    public async Task<List<ImplementationCase>> GetVisibleCasesAsync(string role, string userId)
    {
        var all = await GetAllAsync();
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase)) return all;

        // Support Head oversees the full Implementation/Training board:
        // pending assignment (Admin requests) + every team member's scheduled work.
        if (role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase))
        {
            return all;
        }

        if (role.Equals("Support", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSchemaAsync();
            var activityRows = await _sheets.ReadAsync(ActivityReadRange);
            var transferredByCase = activityRows
                .Where(x => SheetValueHelper.GetString(x, 3).Equals("Transferred", StringComparison.OrdinalIgnoreCase)
                    && SheetValueHelper.GetString(x, 4).Equals(userId, StringComparison.OrdinalIgnoreCase))
                .Select(MapActivity)
                .GroupBy(x => x.CaseId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(a => a.ActivityDate).First(), StringComparer.OrdinalIgnoreCase);

            var visible = new List<ImplementationCase>();
            foreach (var item in all)
            {
                var isActiveOwner = item.AssignedMemberId.Equals(userId, StringComparison.OrdinalIgnoreCase)
                    || item.SupportHeadId.Equals(userId, StringComparison.OrdinalIgnoreCase);
                if (isActiveOwner)
                {
                    visible.Add(item);
                    continue;
                }
                if (transferredByCase.TryGetValue(item.Id, out var transfer))
                {
                    item.IsTransferredHistoryView = true;
                    item.TransferredHistoryNote = $"Transferred to {Value(transfer.ToUserName)} on {transfer.ActivityDate:dd/MM/yyyy hh:mm tt}. Reason: {Value(transfer.Note)}";
                    visible.Add(item);
                }
            }
            return visible;
        }

        return all.Where(x => x.SoldByUserId.Equals(userId, StringComparison.OrdinalIgnoreCase) || x.CreatedByUserId.Equals(userId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<ImplementationCase?> GetByIdAsync(string id)
        => (await GetAllAsync()).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task<ImplementationCase?> FindCustomerByLicenseAsync(string licenseNumber)
    {
        var key = (licenseNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key)) return null;
        var existing = (await GetAllAsync()).FirstOrDefault(x => x.LicenseNumber.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var inquiry = (await _inquiries.GetAllAsync()).Where(x => x.LicenseNumber.Equals(key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SoldDate ?? x.BillDate ?? x.CreatedDate).FirstOrDefault();
        if (inquiry == null) return null;
        return new ImplementationCase
        {
            InquiryId = inquiry.Id,
            FirmName = string.IsNullOrWhiteSpace(inquiry.FirmName) ? inquiry.CustomerInfo : inquiry.FirmName,
            PersonName = inquiry.PersonName,
            Mobile = inquiry.Mobile1,
            Email = inquiry.Email1,
            City = inquiry.City,
            ProductName = inquiry.ProductName ?? string.Empty,
            LicenseCount = Math.Max(1, inquiry.LicensesPurchased),
            LicenseNumber = inquiry.LicenseNumber,
            BillNumber = inquiry.BillNo,
            SaleDate = inquiry.SoldDate ?? inquiry.BillDate ?? DateTime.Today
        };
    }

    public async Task<ImplementationCase?> GetByFeedbackTokenAsync(string token)
        => (await GetAllAsync()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.FeedbackToken) && x.FeedbackToken.Equals(token, StringComparison.OrdinalIgnoreCase));

    public async Task<List<ImplementationSchedule>> GetSchedulesAsync(string caseId)
    {
        await EnsureSchemaAsync();
        var rows = await _sheets.ReadAsync(ScheduleReadRange);
        return rows.Where(x => SheetValueHelper.GetString(x, 1).Equals(caseId, StringComparison.OrdinalIgnoreCase))
            .Select(MapSchedule).OrderBy(x => x.ScheduleDate).ThenBy(x => x.StartTime).ToList();
    }

    public async Task<List<ImplementationActivity>> GetActivitiesAsync(string caseId)
    {
        await EnsureSchemaAsync();
        var rows = await _sheets.ReadAsync(ActivityReadRange);
        return rows.Where(x => SheetValueHelper.GetString(x, 1).Equals(caseId, StringComparison.OrdinalIgnoreCase))
            .Select(MapActivity).OrderByDescending(x => x.ActivityDate).ToList();
    }

    public async Task<ImplementationCase> CreateAsync(ImplementationFormViewModel model, string actorUserId, string actorName, string actorRole)
    {
        await EnsureSchemaAsync();
        var allUsers = await _users.GetActiveUsersAsync();
        var head = allUsers.FirstOrDefault(x => x.Id.Equals(model.SupportHeadId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var member = allUsers.FirstOrDefault(x => x.Id.Equals(model.AssignedMemberId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var item = new ImplementationCase
        {
            Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString() : model.Id,
            InquiryId = model.InquiryId ?? string.Empty,
            SaleDate = model.SaleDate == default ? DateTime.Today : model.SaleDate,
            FirmName = model.FirmName?.Trim() ?? string.Empty,
            PersonName = model.PersonName?.Trim() ?? string.Empty,
            Mobile = model.Mobile?.Trim() ?? string.Empty,
            Email = model.Email?.Trim() ?? string.Empty,
            City = model.City?.Trim() ?? string.Empty,
            ProductName = model.ProductName?.Trim() ?? string.Empty,
            LicenseCount = Math.Max(1, model.LicenseCount),
            LicenseNumber = model.LicenseNumber?.Trim() ?? string.Empty,
            PinNumber = model.PinNumber?.Trim() ?? string.Empty,
            BillNumber = model.BillNumber?.Trim() ?? string.Empty,
            SoldByUserId = actorUserId,
            SoldByName = actorName,
            SoldByRole = actorRole,
            SupportHeadId = head?.Id ?? string.Empty,
            SupportHeadName = head?.FullName ?? string.Empty,
            AssignedMemberId = member?.Id ?? string.Empty,
            AssignedMemberName = member?.FullName ?? string.Empty,
            Status = member == null ? "New Record" : "Assigned",
            Stage = member == null ? "Awaiting Assignment" : "Customer Contact Pending",
            Priority = string.IsNullOrWhiteSpace(model.Priority) ? "Normal" : model.Priority.Trim(),
            PlannedDays = Math.Max(0, model.PlannedDays),
            CustomerContacted = model.CustomerContacted,
            PreferredStartDate = model.PreferredStartDate,
            PreferredTime = model.PreferredTime?.Trim() ?? string.Empty,
            LastProgressNote = model.Notes?.Trim() ?? string.Empty,
            CreatedDate = DateTime.Now,
            CreatedByUserId = actorUserId,
            CreatedByName = actorName,
            LastUpdated = DateTime.Now,
            LastUpdatedByUserId = actorUserId,
            LastUpdatedByName = actorName,
            ServiceType = string.IsNullOrWhiteSpace(model.ServiceType) ? "New Implementation" : model.ServiceType.Trim(),
            IsPaidTraining = model.IsPaidTraining || string.Equals(model.ServiceType, "Paid Training", StringComparison.OrdinalIgnoreCase),
            PaidTrainingAmount = Math.Max(0, model.PaidTrainingAmount),
            PaymentReference = model.PaymentReference?.Trim() ?? string.Empty,
            TrainingRequired = model.TrainingRequired,
            ManualProvided = model.ManualProvided,
            IsManualPaid = model.ManualProvided && model.IsManualPaid,
            ManualAmount = model.ManualProvided && model.IsManualPaid ? Math.Max(0, model.ManualAmount) : 0,
            ManualPaymentReference = model.ManualProvided && model.IsManualPaid ? (model.ManualPaymentReference?.Trim() ?? string.Empty) : string.Empty
        };
        ApplyAi(item, new List<ImplementationSchedule>());
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Created", string.Empty, string.Empty, item.AssignedMemberId, item.AssignedMemberName,
            string.IsNullOrWhiteSpace(model.Notes) ? "Implementation / training record created." : model.Notes, actorUserId, actorName);
        await NotifyCreatedAsync(item, allUsers);
        return item;
    }

    public async Task<ImplementationCase?> EnsureFromSoldInquiryAsync(Inquiry inquiry, string actorUserId, string actorName, string actorRole)
    {
        // This method is called only after an authorised user explicitly confirms
        // the Sold -> Implementation handover popup. Partner sales are supported too.
        if (inquiry == null || !inquiry.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)) return null;
        await EnsureSchemaAsync();
        var existing = (await GetAllAsync()).FirstOrDefault(x => x.InquiryId.Equals(inquiry.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var model = new ImplementationFormViewModel
        {
            InquiryId = inquiry.Id,
            FirmName = string.IsNullOrWhiteSpace(inquiry.FirmName) ? inquiry.CustomerInfo : inquiry.FirmName,
            PersonName = inquiry.PersonName,
            Mobile = inquiry.Mobile1,
            Email = inquiry.Email1,
            City = inquiry.City,
            ProductName = inquiry.ProductName ?? string.Empty,
            LicenseCount = Math.Max(1, inquiry.LicensesPurchased),
            LicenseNumber = inquiry.LicenseNumber,
            BillNumber = inquiry.BillNo,
            SaleDate = inquiry.SoldDate ?? inquiry.BillDate ?? DateTime.Today,
            Priority = "High",
            Notes = "Created after the seller confirmed implementation setup for this Sold inquiry. Verify PIN, ownership and schedule."
        };
        var created = await CreateAsync(model, actorUserId, actorName, actorRole);
        created.SoldByName = string.IsNullOrWhiteSpace(inquiry.SoldByName) ? actorName : inquiry.SoldByName;
        created.SoldByRole = string.IsNullOrWhiteSpace(inquiry.SoldByRole) ? actorRole : inquiry.SoldByRole;
        var soldByUser = (await _users.GetActiveUsersAsync()).FirstOrDefault(x =>
            x.FullName.Equals(created.SoldByName, StringComparison.OrdinalIgnoreCase) ||
            x.Username.Equals(created.SoldByName, StringComparison.OrdinalIgnoreCase));
        created.SoldByUserId = soldByUser?.Id ?? actorUserId;
        created.LastUpdated = DateTime.Now;
        await _sheets.UpsertRowByIdAsync(CaseSheet, created.Id, ToRow(created));
        if (soldByUser != null && !soldByUser.Id.Equals(actorUserId, StringComparison.OrdinalIgnoreCase))
        {
            var sellerNote = $"Your direct sale for {created.FirmName} is now available in the implementation and training workflow.";
            await _notifications.SendImplementationNotificationAsync(created, new[] { soldByUser }, "Implementation / Training Record Created", sellerNote, BuildInternalUrl(created.Id), "ImplementationSellerCreated");
        }
        return created;
    }

    public async Task UpdateAsync(ImplementationFormViewModel model, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(model.Id) ?? throw new InvalidOperationException("Implementation record not found.");
        var users = await _users.GetActiveUsersAsync();
        var head = users.FirstOrDefault(x => x.Id.Equals(model.SupportHeadId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var member = users.FirstOrDefault(x => x.Id.Equals(model.AssignedMemberId ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        // Customer and license master details are intentionally immutable here.
        // Only implementation/training workflow fields can be updated after creation.
        item.Priority = string.IsNullOrWhiteSpace(model.Priority) ? item.Priority : model.Priority.Trim();
        item.SupportHeadId = head?.Id ?? item.SupportHeadId;
        item.SupportHeadName = head?.FullName ?? item.SupportHeadName;
        item.AssignedMemberId = member?.Id ?? item.AssignedMemberId;
        item.AssignedMemberName = member?.FullName ?? item.AssignedMemberName;
        item.CustomerContacted = model.CustomerContacted;
        item.PreferredStartDate = model.PreferredStartDate;
        item.PreferredTime = model.PreferredTime?.Trim() ?? string.Empty;
        item.PlannedDays = Math.Max(0, model.PlannedDays);
        item.ServiceType = string.IsNullOrWhiteSpace(model.ServiceType) ? item.ServiceType : model.ServiceType.Trim();
        item.IsPaidTraining = model.IsPaidTraining || item.ServiceType.Equals("Paid Training", StringComparison.OrdinalIgnoreCase);
        item.PaidTrainingAmount = Math.Max(0, model.PaidTrainingAmount);
        item.PaymentReference = model.PaymentReference?.Trim() ?? string.Empty;
        item.TrainingRequired = model.TrainingRequired;
        item.ManualProvided = model.ManualProvided;
        item.IsManualPaid = model.ManualProvided && model.IsManualPaid;
        item.ManualAmount = model.ManualProvided && model.IsManualPaid ? Math.Max(0, model.ManualAmount) : 0;
        item.ManualPaymentReference = model.ManualProvided && model.IsManualPaid ? (model.ManualPaymentReference?.Trim() ?? string.Empty) : string.Empty;
        if (!string.IsNullOrWhiteSpace(model.Notes)) item.LastProgressNote = model.Notes.Trim();
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Workflow Updated", string.Empty, string.Empty, item.AssignedMemberId, item.AssignedMemberName,
            string.IsNullOrWhiteSpace(model.Notes) ? "Implementation and training workflow updated. Customer master details remained locked." : model.Notes, actorUserId, actorName);
    }

    public async Task AssignAsync(string caseId, string supportHeadId, string assignedMemberId, string note, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        var users = await _users.GetActiveUsersAsync();
        var head = users.FirstOrDefault(x => x.Id.Equals(supportHeadId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var member = users.FirstOrDefault(x => x.Id.Equals(assignedMemberId ?? string.Empty, StringComparison.OrdinalIgnoreCase) && x.Role.Equals("Support", StringComparison.OrdinalIgnoreCase));
        if (member == null) throw new InvalidOperationException("Please select an active support team member.");
        var oldMemberId = item.AssignedMemberId;
        var oldMemberName = item.AssignedMemberName;
        item.SupportHeadId = head?.Id ?? item.SupportHeadId;
        item.SupportHeadName = head?.FullName ?? item.SupportHeadName;
        item.AssignedMemberId = member.Id;
        item.AssignedMemberName = member.FullName;
        item.Status = "Assigned";
        item.Stage = item.CustomerContacted ? "Scheduling" : "Customer Contact Pending";
        item.LastProgressNote = string.IsNullOrWhiteSpace(note) ? $"Assigned to {member.FullName}." : note.Trim();
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Assigned", oldMemberId, oldMemberName, member.Id, member.FullName, item.LastProgressNote, actorUserId, actorName);
        var recipients = ResolveRecipients(users, item, includeAllAdmins: true, member);
        await _notifications.SendImplementationNotificationAsync(item, recipients, "Implementation Assigned", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationAssigned");
    }

    public async Task<ImplementationSchedule> SaveScheduleAsync(ImplementationScheduleViewModel model, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(model.CaseId) ?? throw new InvalidOperationException("Implementation record not found.");
        var users = await _users.GetActiveUsersAsync();
        var member = users.FirstOrDefault(x => x.Id.Equals(model.AssignedMemberId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? users.FirstOrDefault(x => x.Id.Equals(item.AssignedMemberId, StringComparison.OrdinalIgnoreCase));
        if (member == null) throw new InvalidOperationException("Assign a support team member before scheduling.");
        var existing = string.IsNullOrWhiteSpace(model.Id) ? null : (await GetSchedulesAsync(item.Id)).FirstOrDefault(x => x.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
        var schedule = existing ?? new ImplementationSchedule { Id = Guid.NewGuid().ToString(), CaseId = item.Id, CreatedAt = DateTime.Now, CreatedByUserId = actorUserId, CreatedByName = actorName };
        schedule.DayNumber = Math.Max(1, model.DayNumber);
        schedule.ScheduleDate = model.ScheduleDate.Date;
        schedule.StartTime = NormalizeTime(model.StartTime, "10:00");
        schedule.EndTime = NormalizeTime(model.EndTime, string.Empty);
        schedule.TopicPlan = model.TopicPlan?.Trim() ?? string.Empty;
        schedule.Stage = string.IsNullOrWhiteSpace(model.Stage) ? "Training" : model.Stage.Trim();
        schedule.AssignedMemberId = member.Id;
        schedule.AssignedMemberName = member.FullName;
        schedule.Status = existing?.Status is "Completed" or "Cancelled" ? existing.Status : "Scheduled";
        schedule.LastUpdated = DateTime.Now;
        schedule.ReminderSentAt = null;
        await _sheets.UpsertRowByIdAsync(ScheduleSheet, schedule.Id, ToRow(schedule));

        item.AssignedMemberId = member.Id;
        item.AssignedMemberName = member.FullName;
        item.CustomerContacted = true;
        item.Status = "Scheduled";
        item.Stage = schedule.Stage;
        item.PlannedDays = Math.Max(item.PlannedDays, schedule.DayNumber);
        item.PreferredStartDate ??= schedule.ScheduleDate;
        item.PreferredTime = schedule.StartTime;
        item.LastProgressNote = $"Day {schedule.DayNumber} scheduled on {schedule.ScheduleDate:dd/MM/yyyy} at {schedule.StartTime}.";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, existing == null ? "Schedule Created" : "Schedule Updated", string.Empty, string.Empty, member.Id, member.FullName,
            item.LastProgressNote + " " + schedule.TopicPlan, actorUserId, actorName);
        var recipients = ResolveRecipients(users, item, includeAllAdmins: true, member);
        await _notifications.SendImplementationNotificationAsync(item, recipients, "Training Schedule Updated", item.LastProgressNote + $"\nPlanned topics: {schedule.TopicPlan}", BuildInternalUrl(item.Id), "ImplementationScheduled");
        return schedule;
    }

    public async Task UpdateProgressAsync(ImplementationProgressViewModel model, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(model.CaseId) ?? throw new InvalidOperationException("Implementation record not found.");
        var schedules = await GetSchedulesAsync(item.Id);
        var schedule = schedules.FirstOrDefault(x => x.Id.Equals(model.ScheduleId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Schedule record not found.");
        schedule.TopicsCovered = model.TopicsCovered?.Trim() ?? string.Empty;
        schedule.Notes = model.Notes?.Trim() ?? string.Empty;
        schedule.Status = string.IsNullOrWhiteSpace(model.Status) ? "Completed" : model.Status.Trim();
        schedule.Stage = string.IsNullOrWhiteSpace(model.Stage) ? schedule.Stage : model.Stage.Trim();
        schedule.CompletedAt = schedule.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? DateTime.Now : null;
        schedule.LastUpdated = DateTime.Now;
        await _sheets.UpsertRowByIdAsync(ScheduleSheet, schedule.Id, ToRow(schedule));

        schedules = await GetSchedulesAsync(item.Id);
        var completed = schedules.Count(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        var calculatedProgress = schedules.Count == 0 ? 0 : (int)Math.Round(completed * 100m / schedules.Count);
        item.ProgressPercent = Math.Max(calculatedProgress, Math.Clamp(model.ProgressPercent, 0, 100));
        item.Status = item.ProgressPercent >= 100 ? "Ready to Complete" : "In Progress";
        item.Stage = schedule.Stage;
        item.ActualStartDate ??= schedule.ScheduleDate;
        item.LastProgressNote = $"Day {schedule.DayNumber}: {schedule.TopicsCovered}" + (string.IsNullOrWhiteSpace(schedule.Notes) ? string.Empty : $" | {schedule.Notes}");
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, schedules);
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Progress Updated", schedule.AssignedMemberId, schedule.AssignedMemberName, schedule.AssignedMemberId, schedule.AssignedMemberName,
            item.LastProgressNote, actorUserId, actorName);
    }

    public async Task RequestOtpAsync(string caseId, string purpose, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        purpose = NormalizeOtpPurpose(purpose);
        var users = await _users.GetActiveUsersAsync();
        var approvers = users.Where(x => x.IsActive && (x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || x.Role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase))).ToList();
        if (approvers.Count == 0) throw new InvalidOperationException("No active Admin or Support Head is available for OTP approval.");

        var otpMinutes = int.TryParse(_configuration["Implementation:OtpExpiryMinutes"], out var configuredMinutes) ? Math.Clamp(configuredMinutes, 3, 30) : 10;
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var approval = new ImplementationOtpApproval
        {
            Id = Guid.NewGuid().ToString(), CaseId = caseId, Purpose = purpose,
            RequestedByUserId = actorUserId, RequestedByName = actorName,
            RequestedAt = DateTime.Now, ExpiresAt = DateTime.Now.AddMinutes(otpMinutes),
            ApproverNames = string.Join(", ", approvers.Select(x => x.FullName)),
        };
        approval.OtpHash = HashOtp(approval.Id, otp);
        await _sheets.UpsertRowByIdAsync(OtpSheet, approval.Id, ToRow(approval));
        var note = string.Join(Environment.NewLine, new[]
        {
            $"Security OTP: {otp}",
            $"Purpose: {purpose}",
            $"Requested by: {actorName}",
            $"Valid until: {approval.ExpiresAt:dd/MM/yyyy hh:mm tt}. Share this OTP only after verifying the request."
        });
        await _notifications.SendImplementationNotificationAsync(item, approvers, $"{purpose} OTP Approval", note, BuildInternalUrl(item.Id), "ImplementationOtpApproval", false);
        await AddActivityAsync(item.Id, "OTP Requested", actorUserId, actorName, string.Empty, "Admin / Support Head", $"{purpose} OTP requested. Valid for {otpMinutes} minutes.", actorUserId, actorName);
    }

    public async Task TransferAsync(string caseId, string newMemberId, string reason, string otp, string actorUserId, string actorName)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Transfer reason is required.");
        await ValidateAndConsumeOtpAsync(caseId, "Transfer", otp, actorUserId, actorName);
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        var users = await _users.GetActiveUsersAsync();
        var newMember = users.FirstOrDefault(x => x.Id.Equals(newMemberId ?? string.Empty, StringComparison.OrdinalIgnoreCase) && x.Role.Equals("Support", StringComparison.OrdinalIgnoreCase));
        if (newMember == null) throw new InvalidOperationException("Please select an active support team member.");
        if (newMember.Id.Equals(item.AssignedMemberId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Please select a different support member.");
        var oldId = item.AssignedMemberId;
        var oldName = item.AssignedMemberName;
        item.TransferFromUserId = oldId;
        item.TransferFromName = oldName;
        item.TransferReason = reason.Trim();
        item.LastTransferDate = DateTime.Now;
        item.AssignedMemberId = newMember.Id;
        item.AssignedMemberName = newMember.FullName;
        item.Status = "Transferred";
        item.LastProgressNote = $"Transferred from {Value(oldName)} to {newMember.FullName} on {DateTime.Now:dd/MM/yyyy hh:mm tt}. Reason: {reason.Trim()}";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));

        var schedules = await GetSchedulesAsync(item.Id);
        foreach (var schedule in schedules.Where(x => !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)))
        {
            schedule.AssignedMemberId = newMember.Id;
            schedule.AssignedMemberName = newMember.FullName;
            schedule.ReminderSentAt = null;
            schedule.LastUpdated = DateTime.Now;
            await _sheets.UpsertRowByIdAsync(ScheduleSheet, schedule.Id, ToRow(schedule));
        }
        await AddActivityAsync(item.Id, "Transferred", oldId, oldName, newMember.Id, newMember.FullName, reason.Trim(), actorUserId, actorName);
        var recipients = ResolveRecipients(users, item, includeAllAdmins: true, newMember);
        var oldMember = users.FirstOrDefault(x => x.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase));
        if (oldMember != null) recipients.Add(oldMember);
        await _notifications.SendImplementationNotificationAsync(item, recipients, "Implementation Transferred", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationTransferred");
    }

    public async Task ReopenAsync(string caseId, string remarks, string otp, string actorUserId, string actorName)
    {
        if (string.IsNullOrWhiteSpace(remarks)) throw new InvalidOperationException("Re-open remarks are required.");
        await ValidateAndConsumeOtpAsync(caseId, "Reopen", otp, actorUserId, actorName);
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        if (!item.IsTrainingCompleted && !item.ClosedWithoutTraining) throw new InvalidOperationException("This record is already open.");
        item.IsTrainingCompleted = false;
        item.ClosedWithoutTraining = false;
        item.TrainingRequired = true;
        item.Status = "Reopened";
        item.Stage = "Reopened - Action Required";
        item.ProgressPercent = Math.Min(item.ProgressPercent, 95);
        item.CompletionDate = null;
        item.ReopenedCount += 1;
        item.LastReopenedDate = DateTime.Now;
        item.LastReopenedByName = actorName;
        item.LastProgressNote = $"Re-opened with OTP approval. Remarks: {remarks.Trim()}";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Reopened", item.AssignedMemberId, item.AssignedMemberName, item.AssignedMemberId, item.AssignedMemberName, remarks.Trim(), actorUserId, actorName);
        var users = await _users.GetActiveUsersAsync();
        await _notifications.SendImplementationNotificationAsync(item, ResolveRecipients(users, item, includeAllAdmins: true), "Implementation Re-opened", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationReopened", false);
    }

    public async Task CloseWithoutTrainingAsync(string caseId, string remarks, string actorUserId, string actorName)
    {
        if (string.IsNullOrWhiteSpace(remarks)) throw new InvalidOperationException("Closure remarks are required.");
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        item.TrainingRequired = false;
        item.ClosedWithoutTraining = true;
        item.IsTrainingCompleted = true;
        item.Status = "Closed - Training Not Required";
        item.Stage = "Closed";
        item.CompletionDate = DateTime.Now;
        item.NoTrainingRemarks = remarks.Trim();
        item.LastProgressNote = $"Training not required. Closed with remarks: {remarks.Trim()}";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Closed - Training Not Required", item.AssignedMemberId, item.AssignedMemberName, string.Empty, string.Empty, remarks.Trim(), actorUserId, actorName);
        var users = await _users.GetActiveUsersAsync();
        await _notifications.SendImplementationNotificationAsync(item, ResolveRecipients(users, item, includeAllAdmins: true), "Training Not Required - Record Closed", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationNoTrainingClose", false);
    }

    public async Task PauseAsync(string caseId, string reason, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        if (item.IsTrainingCompleted) throw new InvalidOperationException("This record is already completed and cannot be paused.");
        if (item.IsPaused) throw new InvalidOperationException("This training is already paused.");
        item.IsPaused = true;
        item.StatusBeforePause = item.Status;
        item.PauseReason = (reason ?? string.Empty).Trim();
        item.PausedDate = DateTime.Now;
        item.PausedByName = actorName;
        item.ResumedDate = null;
        item.Status = "Paused";
        item.Stage = "Paused";
        item.LastProgressNote = string.IsNullOrWhiteSpace(item.PauseReason)
            ? $"Training paused by {actorName} on {DateTime.Now:dd/MM/yyyy hh:mm tt}."
            : $"Training paused by {actorName} on {DateTime.Now:dd/MM/yyyy hh:mm tt}. Reason: {item.PauseReason}";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Training Paused", item.AssignedMemberId, item.AssignedMemberName, item.AssignedMemberId, item.AssignedMemberName,
            item.LastProgressNote, actorUserId, actorName);
        var users = await _users.GetActiveUsersAsync();
        await _notifications.SendImplementationNotificationAsync(item, ResolveRecipients(users, item, includeAllAdmins: true), "Training Paused", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationPaused", false);
    }

    public async Task ResumeAsync(string caseId, string remarks, string actorUserId, string actorName)
    {
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        if (!item.IsPaused) throw new InvalidOperationException("This training is not paused.");
        item.IsPaused = false;
        item.ResumedDate = DateTime.Now;
        var restoredStatus = string.IsNullOrWhiteSpace(item.StatusBeforePause)
            ? (item.ProgressPercent >= 100 ? "Ready to Complete" : "In Progress")
            : item.StatusBeforePause;
        item.Status = restoredStatus;
        item.Stage = "Resumed";
        var resumeRemarks = (remarks ?? string.Empty).Trim();
        item.LastProgressNote = string.IsNullOrWhiteSpace(resumeRemarks)
            ? $"Training resumed by {actorName} on {DateTime.Now:dd/MM/yyyy hh:mm tt}."
            : $"Training resumed by {actorName} on {DateTime.Now:dd/MM/yyyy hh:mm tt}. Remarks: {resumeRemarks}";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Training Resumed", item.AssignedMemberId, item.AssignedMemberName, item.AssignedMemberId, item.AssignedMemberName,
            item.LastProgressNote, actorUserId, actorName);
        var users = await _users.GetActiveUsersAsync();
        await _notifications.SendImplementationNotificationAsync(item, ResolveRecipients(users, item, includeAllAdmins: true), "Training Resumed", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationResumed", false);
    }

    public async Task CompleteAsync(string caseId, IEnumerable<string> trainingTopics, string otherPointsCovered, string actorUserId, string actorName, string feedbackBaseUrl)
    {
        var item = await GetByIdAsync(caseId) ?? throw new InvalidOperationException("Implementation record not found.");
        var selectedTopics = ImplementationTrainingTopicCatalog.Normalize(trainingTopics);
        var otherPoints = (otherPointsCovered ?? string.Empty).Trim();
        if (selectedTopics.Count == 0 && string.IsNullOrWhiteSpace(otherPoints))
            throw new InvalidOperationException("Select at least one covered training point or enter Other Points Covered before completing training.");

        item.IsTrainingCompleted = true;
        item.Status = "Completed";
        item.Stage = "Completed";
        item.ProgressPercent = 100;
        item.CompletionDate = DateTime.Now;
        item.FeedbackToken = string.IsNullOrWhiteSpace(item.FeedbackToken) ? Guid.NewGuid().ToString("N") : item.FeedbackToken;
        item.FeedbackSentDate = DateTime.Now;
        var completedSchedules = await GetSchedulesAsync(item.Id);

        var topicLines = selectedTopics.Select(x => $"Covered: {x}").ToList();
        if (!string.IsNullOrWhiteSpace(otherPoints)) topicLines.Add($"Other Points: {otherPoints}");
        item.TrainingTopicsSummary = string.Join(Environment.NewLine, topicLines);
        item.LastProgressNote = $"Training completed with {selectedTopics.Count} selected topic(s){(string.IsNullOrWhiteSpace(otherPoints) ? string.Empty : " and additional other points")}. Customer feedback requested.";
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByUserId = actorUserId;
        item.LastUpdatedByName = actorName;
        ApplyAi(item, completedSchedules);
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Completed", item.AssignedMemberId, item.AssignedMemberName, string.Empty, string.Empty,
            $"{item.LastProgressNote}{Environment.NewLine}{item.TrainingTopicsSummary}", actorUserId, actorName);

        var configuredBase = (_configuration["Implementation:PublicBaseUrl"] ?? string.Empty).Trim().TrimEnd('/');
        var baseUrl = !string.IsNullOrWhiteSpace(configuredBase) ? configuredBase : (feedbackBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var feedbackUrl = $"{baseUrl}/Feedback/Training/{Uri.EscapeDataString(item.FeedbackToken)}";
        await _notifications.SendFeedbackRequestAsync(item, feedbackUrl);
        var users = await _users.GetActiveUsersAsync();
        await _notifications.SendImplementationNotificationAsync(item, ResolveRecipients(users, item, includeAllAdmins: true), "Training Completed", item.LastProgressNote, BuildInternalUrl(item.Id), "ImplementationCompleted", false);
    }

    public async Task SubmitFeedbackAsync(string token, int rating, string remarks)
    {
        var item = await GetByFeedbackTokenAsync(token) ?? throw new InvalidOperationException("Feedback link is invalid or expired.");
        item.FeedbackRating = Math.Clamp(rating, 1, 5);
        item.FeedbackRemarks = remarks?.Trim() ?? string.Empty;
        item.FeedbackDate = DateTime.Now;
        item.LastUpdated = DateTime.Now;
        item.LastUpdatedByName = "Customer Feedback";
        ApplyAi(item, await GetSchedulesAsync(item.Id));
        await _sheets.UpsertRowByIdAsync(CaseSheet, item.Id, ToRow(item));
        await AddActivityAsync(item.Id, "Feedback Received", string.Empty, item.PersonName, string.Empty, "Profit Nx Team",
            $"Rating {item.FeedbackRating}/5. {item.FeedbackRemarks}", string.Empty, "Customer");
        var users = await _users.GetActiveUsersAsync();
        var feedbackRecipients = ResolveRecipients(users, item, includeAllAdmins: true)
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .ToList();
        var configuredReceiver = (_configuration["Implementation:FeedbackReceiverEmail"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredReceiver) && !feedbackRecipients.Any(x => x.Email.Equals(configuredReceiver, StringComparison.OrdinalIgnoreCase)))
        {
            feedbackRecipients.Add(new AppUser
            {
                Id = "configured-feedback-receiver",
                FullName = "Feedback Receiver",
                Role = "Admin",
                Email = configuredReceiver,
                IsActive = true
            });
        }
        var adminSender = users.FirstOrDefault(x => x.IsActive
                && x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.Email)
                && !string.IsNullOrWhiteSpace(x.SmtpHost));

        await _notifications.SendFeedbackReceivedEmailAsync(item, feedbackRecipients, adminSender);
        await _notifications.SendImplementationNotificationAsync(item, ResolveRecipients(users, item, includeAllAdmins: true), "Customer Feedback Received",
            $"Rating: {item.FeedbackRating}/5\nRemarks: {item.FeedbackRemarks}", BuildInternalUrl(item.Id), "ImplementationFeedbackReceived", false);
    }

    public async Task DeleteAsync(string caseId)
    {
        await EnsureSchemaAsync();
        var schedules = await GetSchedulesAsync(caseId);
        var activities = await GetActivitiesAsync(caseId);
        var otpRows = await _sheets.ReadAsync(OtpReadRange);
        var otpIds = otpRows.Where(x => SheetValueHelper.GetString(x, 1).Equals(caseId, StringComparison.OrdinalIgnoreCase))
            .Select(x => SheetValueHelper.GetString(x, 0)).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (schedules.Count > 0) await _sheets.DeleteRowsByIdAsync(ScheduleSheet, schedules.Select(x => x.Id));
        if (activities.Count > 0) await _sheets.DeleteRowsByIdAsync(ActivitySheet, activities.Select(x => x.Id));
        if (otpIds.Count > 0) await _sheets.DeleteRowsByIdAsync(OtpSheet, otpIds);
        await _sheets.DeleteRowsByIdAsync(CaseSheet, new[] { caseId });
    }

    public async Task<List<ImplementationSchedule>> GetUpcomingForUserAsync(string role, string userId, DateTime from, DateTime to)
    {
        await EnsureSchemaAsync();
        var visibleCases = await GetVisibleCasesAsync(role, userId);
        var caseIds = visibleCases.Where(x => !x.IsTransferredHistoryView).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await _sheets.ReadAsync(ScheduleReadRange);
        return rows.Select(MapSchedule)
            .Where(x => caseIds.Contains(x.CaseId) && !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            .Where(x => TryScheduleDateTime(x, out var when) && when >= from && when <= to)
            .OrderBy(x => x.ScheduleDate).ThenBy(x => x.StartTime).ToList();
    }

    public async Task ProcessDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync();
        var reminderMinutes = int.TryParse(_configuration["Implementation:ReminderMinutes"], out var configured) ? Math.Clamp(configured, 5, 120) : 15;
        var now = DateTime.Now;
        var rows = await _sheets.ReadAsync(ScheduleReadRange);
        var schedules = rows.Select(MapSchedule)
            .Where(x => !x.ReminderSentAt.HasValue && !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            .Where(x => TryScheduleDateTime(x, out var when) && when >= now.AddMinutes(-2) && when <= now.AddMinutes(reminderMinutes))
            .ToList();
        if (schedules.Count == 0) return;
        var allCases = await GetAllAsync();
        var allUsers = await _users.GetActiveUsersAsync();
        foreach (var schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = allCases.FirstOrDefault(x => x.Id.Equals(schedule.CaseId, StringComparison.OrdinalIgnoreCase));
            if (item == null || item.IsTrainingCompleted) continue;
            var when = GetScheduleDateTime(schedule);
            var note = $"Day {schedule.DayNumber} {schedule.Stage} session starts at {when:dd/MM/yyyy hh:mm tt}. Planned topics: {schedule.TopicPlan}";
            var recipients = ResolveRecipients(allUsers, item, includeAllAdmins: ReadBoolean("Implementation:NotifyAllAdmins", true));
            await _notifications.SendImplementationNotificationAsync(item, recipients, "15-Minute Training Reminder", note, BuildInternalUrl(item.Id), "ImplementationReminder");
            schedule.ReminderSentAt = DateTime.Now;
            schedule.LastUpdated = DateTime.Now;
            await _sheets.UpsertRowByIdAsync(ScheduleSheet, schedule.Id, ToRow(schedule));
            await AddActivityAsync(item.Id, "Reminder Sent", string.Empty, string.Empty, schedule.AssignedMemberId, schedule.AssignedMemberName, note, string.Empty, "System");
        }
    }

    private async Task<List<ImplementationCase>> GetAllAsync()
    {
        await EnsureSchemaAsync();
        var rows = await _sheets.ReadAsync(CaseReadRange);
        var scheduleRows = await _sheets.ReadAsync(ScheduleReadRange);
        var schedulesByCase = scheduleRows
            .Where(x => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(x, 0)))
            .Select(MapSchedule)
            .GroupBy(x => x.CaseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var list = rows.Where(x => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(x, 0))).Select(MapCase).ToList();
        foreach (var item in list)
            ApplyAi(item, schedulesByCase.TryGetValue(item.Id, out var schedules) ? schedules : new List<ImplementationSchedule>());
        return list.OrderByDescending(x => x.LastUpdated).ToList();
    }

    private async Task NotifyCreatedAsync(ImplementationCase item, List<AppUser> allUsers)
    {
        var recipients = allUsers.Where(x => x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || x.Role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase)
            || x.Role.Equals("Support", StringComparison.OrdinalIgnoreCase)).ToList();
        var creator = allUsers.FirstOrDefault(x => x.Id.Equals(item.CreatedByUserId, StringComparison.OrdinalIgnoreCase));
        if (creator != null) recipients.Add(creator);
        var note = $"A new implementation or training record is ready for assignment and planning. Assign a support member and contact the customer. License: {Value(item.LicenseNumber)}.";
        await _notifications.SendImplementationNotificationAsync(item, recipients, "New Implementation / Training Record", note, BuildInternalUrl(item.Id), "ImplementationCreated");
    }

    private async Task AddActivityAsync(string caseId, string type, string fromId, string fromName, string toId, string toName, string note, string actorId, string actorName)
    {
        var activity = new ImplementationActivity
        {
            Id = Guid.NewGuid().ToString(), CaseId = caseId, ActivityDate = DateTime.Now, ActivityType = type,
            FromUserId = fromId ?? string.Empty, FromUserName = fromName ?? string.Empty, ToUserId = toId ?? string.Empty,
            ToUserName = toName ?? string.Empty, Note = note ?? string.Empty, PerformedByUserId = actorId ?? string.Empty,
            PerformedByName = actorName ?? string.Empty
        };
        await _sheets.AppendAsync(ActivitySheet, ToRow(activity));
    }

    private List<AppUser> ResolveRecipients(List<AppUser> allUsers, ImplementationCase item, bool includeAllAdmins, params AppUser?[] extra)
    {
        var ids = new[] { item.SupportHeadId, item.AssignedMemberId, item.SoldByUserId, item.CreatedByUserId }
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recipients = allUsers.Where(x => ids.Contains(x.Id)).ToList();
        if (includeAllAdmins) recipients.AddRange(allUsers.Where(x => x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)));
        recipients.AddRange(extra.Where(x => x != null).Cast<AppUser>());
        return recipients.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
    }

    private string BuildInternalUrl(string caseId)
    {
        var baseUrl = (_configuration["Implementation:PublicBaseUrl"] ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? $"/Implementation/Details/{Uri.EscapeDataString(caseId)}" : $"{baseUrl}/Implementation/Details/{Uri.EscapeDataString(caseId)}";
    }

    private bool ReadBoolean(string key, bool defaultValue)
    {
        var value = _configuration[key];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static void ApplyAi(ImplementationCase item, List<ImplementationSchedule> schedules)
    {
        var ageDays = Math.Max(0, (DateTime.Today - item.SaleDate.Date).Days);
        var overdue = schedules.Count(x => TryScheduleDateTime(x, out var when) && when < DateTime.Now && !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
        if (item.IsTrainingCompleted)
        {
            item.AiRiskLevel = item.FeedbackRating is > 0 and < 4 ? "Medium" : "Low";
            item.AiRecommendation = item.FeedbackRating == 0 ? "Feedback is pending. Follow up with the customer." : item.FeedbackRating < 4 ? "Review feedback remarks and create a service recovery call." : "Onboarding completed successfully. Keep this customer in the reference pool.";
        }
        else if (string.IsNullOrWhiteSpace(item.AssignedMemberId) && ageDays >= 1)
        {
            item.AiRiskLevel = ageDays >= 3 ? "Critical" : "High";
            item.AiRecommendation = "Assign a support member immediately and contact the customer today.";
        }
        else if (overdue > 0)
        {
            item.AiRiskLevel = overdue > 1 ? "Critical" : "High";
            item.AiRecommendation = $"{overdue} scheduled session(s) are overdue. Confirm customer availability and reschedule or complete the day record.";
        }
        else if (ageDays >= 7 && item.ProgressPercent < 50)
        {
            item.AiRiskLevel = "High";
            item.AiRecommendation = "Progress is slow for the sale age. Review blockers and prepare a recovery schedule.";
        }
        else if (item.ProgressPercent > 0)
        {
            item.AiRiskLevel = "Low";
            item.AiRecommendation = "Continue the planned schedule and document every covered point before closing each day.";
        }
        else
        {
            item.AiRiskLevel = "Medium";
            item.AiRecommendation = item.CustomerContacted ? "Create the first installation/training schedule." : "Call the customer, confirm date/time and record the preferred schedule.";
        }
    }

    private async Task ValidateAndConsumeOtpAsync(string caseId, string purpose, string otp, string actorUserId, string actorName)
    {
        purpose = NormalizeOtpPurpose(purpose);
        if (string.IsNullOrWhiteSpace(otp) || otp.Trim().Length != 6) throw new InvalidOperationException("Enter the 6-digit OTP provided by Admin or Support Head.");
        await EnsureSchemaAsync();
        var approvals = (await _sheets.ReadAsync(OtpReadRange)).Where(x => !string.IsNullOrWhiteSpace(SheetValueHelper.GetString(x, 0)))
            .Select(MapOtp).Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)
                && x.Purpose.Equals(purpose, StringComparison.OrdinalIgnoreCase)
                && x.RequestedByUserId.Equals(actorUserId, StringComparison.OrdinalIgnoreCase)
                && !x.IsUsed && x.ExpiresAt >= DateTime.Now)
            .OrderByDescending(x => x.RequestedAt).ToList();
        var approval = approvals.FirstOrDefault(x => FixedEquals(x.OtpHash, HashOtp(x.Id, otp.Trim())));
        if (approval == null) throw new InvalidOperationException("OTP is invalid, expired, or was not requested by this login. Request a new OTP.");
        approval.IsUsed = true;
        approval.UsedAt = DateTime.Now;
        approval.UsedByUserId = actorUserId;
        approval.UsedByName = actorName;
        await _sheets.UpsertRowByIdAsync(OtpSheet, approval.Id, ToRow(approval));
    }

    private string HashOtp(string approvalId, string otp)
    {
        var secret = _configuration["Implementation:OtpSecret"];
        if (string.IsNullOrWhiteSpace(secret)) secret = "ProfitNx-CRM-Implementation-OTP-v1";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{approvalId}|{otp}|{secret}"));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string NormalizeOtpPurpose(string purpose)
        => (purpose ?? string.Empty).Trim().Equals("Reopen", StringComparison.OrdinalIgnoreCase) ? "Reopen" : "Transfer";

    private static bool TryScheduleDateTime(ImplementationSchedule schedule, out DateTime value)
    {
        value = schedule.ScheduleDate.Date;
        if (!TimeSpan.TryParse(schedule.StartTime, out var time)) return false;
        value = schedule.ScheduleDate.Date.Add(time);
        return true;
    }

    private static DateTime GetScheduleDateTime(ImplementationSchedule schedule)
        => TryScheduleDateTime(schedule, out var value) ? value : schedule.ScheduleDate.Date;

    private static string NormalizeTime(string? value, string fallback)
        => TimeSpan.TryParse(value, out var time) ? time.ToString(@"hh\:mm") : fallback;

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim();

    private static ImplementationCase MapCase(IList<object> r) => new()
    {
        Id = SheetValueHelper.GetString(r, 0), InquiryId = SheetValueHelper.GetString(r, 1), SaleDate = SheetValueHelper.GetDate(r, 2, DateTime.Today),
        FirmName = SheetValueHelper.GetString(r, 3), PersonName = SheetValueHelper.GetString(r, 4), Mobile = SheetValueHelper.GetString(r, 5),
        Email = SheetValueHelper.GetString(r, 6), City = SheetValueHelper.GetString(r, 7), ProductName = SheetValueHelper.GetString(r, 8),
        LicenseCount = Math.Max(1, SheetValueHelper.GetInt(r, 9)), LicenseNumber = SheetValueHelper.GetString(r, 10), PinNumber = SheetValueHelper.GetString(r, 11),
        BillNumber = SheetValueHelper.GetString(r, 12), SoldByUserId = SheetValueHelper.GetString(r, 13), SoldByName = SheetValueHelper.GetString(r, 14),
        SoldByRole = SheetValueHelper.GetString(r, 15), SupportHeadId = SheetValueHelper.GetString(r, 16), SupportHeadName = SheetValueHelper.GetString(r, 17),
        AssignedMemberId = SheetValueHelper.GetString(r, 18), AssignedMemberName = SheetValueHelper.GetString(r, 19), Status = SheetValueHelper.GetString(r, 20),
        Stage = SheetValueHelper.GetString(r, 21), Priority = SheetValueHelper.GetString(r, 22), PlannedDays = SheetValueHelper.GetInt(r, 23),
        ProgressPercent = SheetValueHelper.GetInt(r, 24), CustomerContacted = SheetValueHelper.GetBool(r, 25, false), PreferredStartDate = SheetValueHelper.GetNullableDate(r, 26),
        PreferredTime = SheetValueHelper.GetString(r, 27), ActualStartDate = SheetValueHelper.GetNullableDate(r, 28), CompletionDate = SheetValueHelper.GetNullableDate(r, 29),
        IsTrainingCompleted = SheetValueHelper.GetBool(r, 30, false), LastProgressNote = SheetValueHelper.GetString(r, 31), TransferFromUserId = SheetValueHelper.GetString(r, 32),
        TransferFromName = SheetValueHelper.GetString(r, 33), TransferReason = SheetValueHelper.GetString(r, 34), FeedbackToken = SheetValueHelper.GetString(r, 35),
        FeedbackSentDate = SheetValueHelper.GetNullableDate(r, 36), FeedbackRating = SheetValueHelper.GetInt(r, 37), FeedbackRemarks = SheetValueHelper.GetString(r, 38),
        FeedbackDate = SheetValueHelper.GetNullableDate(r, 39), AiRiskLevel = SheetValueHelper.GetString(r, 40), AiRecommendation = SheetValueHelper.GetString(r, 41),
        CreatedDate = SheetValueHelper.GetDateTime(r, 42) ?? DateTime.Now, CreatedByUserId = SheetValueHelper.GetString(r, 43), CreatedByName = SheetValueHelper.GetString(r, 44),
        LastUpdated = SheetValueHelper.GetDateTime(r, 45) ?? DateTime.Now, LastUpdatedByUserId = SheetValueHelper.GetString(r, 46), LastUpdatedByName = SheetValueHelper.GetString(r, 47),
        ServiceType = string.IsNullOrWhiteSpace(SheetValueHelper.GetString(r, 48)) ? "New Implementation" : SheetValueHelper.GetString(r, 48),
        IsPaidTraining = SheetValueHelper.GetBool(r, 49, false), PaidTrainingAmount = SheetValueHelper.GetDecimal(r, 50), PaymentReference = SheetValueHelper.GetString(r, 51),
        TrainingRequired = SheetValueHelper.GetBool(r, 52, true), ClosedWithoutTraining = SheetValueHelper.GetBool(r, 53, false), NoTrainingRemarks = SheetValueHelper.GetString(r, 54),
        ReopenedCount = SheetValueHelper.GetInt(r, 55), LastReopenedDate = SheetValueHelper.GetDateTime(r, 56), LastReopenedByName = SheetValueHelper.GetString(r, 57),
        LastTransferDate = SheetValueHelper.GetDateTime(r, 58), TrainingTopicsSummary = SheetValueHelper.GetString(r, 59),
        IsPaused = SheetValueHelper.GetBool(r, 60, false), PauseReason = SheetValueHelper.GetString(r, 61), PausedDate = SheetValueHelper.GetNullableDate(r, 62),
        PausedByName = SheetValueHelper.GetString(r, 63), ResumedDate = SheetValueHelper.GetNullableDate(r, 64), StatusBeforePause = SheetValueHelper.GetString(r, 65),
        ManualProvided = SheetValueHelper.GetBool(r, 66, false), IsManualPaid = SheetValueHelper.GetBool(r, 67, false),
        ManualAmount = SheetValueHelper.GetDecimal(r, 68), ManualPaymentReference = SheetValueHelper.GetString(r, 69)
    };

    private static ImplementationSchedule MapSchedule(IList<object> r) => new()
    {
        Id = SheetValueHelper.GetString(r, 0), CaseId = SheetValueHelper.GetString(r, 1), DayNumber = Math.Max(1, SheetValueHelper.GetInt(r, 2)),
        ScheduleDate = SheetValueHelper.GetDate(r, 3, DateTime.Today), StartTime = SheetValueHelper.GetString(r, 4), EndTime = SheetValueHelper.GetString(r, 5),
        TopicPlan = SheetValueHelper.GetString(r, 6), TopicsCovered = SheetValueHelper.GetString(r, 7), Notes = SheetValueHelper.GetString(r, 8),
        Stage = SheetValueHelper.GetString(r, 9), Status = SheetValueHelper.GetString(r, 10), AssignedMemberId = SheetValueHelper.GetString(r, 11),
        AssignedMemberName = SheetValueHelper.GetString(r, 12), CompletedAt = SheetValueHelper.GetDateTime(r, 13), ReminderSentAt = SheetValueHelper.GetDateTime(r, 14),
        CreatedAt = SheetValueHelper.GetDateTime(r, 15) ?? DateTime.Now, CreatedByUserId = SheetValueHelper.GetString(r, 16), CreatedByName = SheetValueHelper.GetString(r, 17),
        LastUpdated = SheetValueHelper.GetDateTime(r, 18) ?? DateTime.Now
    };

    private static ImplementationActivity MapActivity(IList<object> r) => new()
    {
        Id = SheetValueHelper.GetString(r, 0), CaseId = SheetValueHelper.GetString(r, 1), ActivityDate = SheetValueHelper.GetDateTime(r, 2) ?? DateTime.Now,
        ActivityType = SheetValueHelper.GetString(r, 3), FromUserId = SheetValueHelper.GetString(r, 4), FromUserName = SheetValueHelper.GetString(r, 5),
        ToUserId = SheetValueHelper.GetString(r, 6), ToUserName = SheetValueHelper.GetString(r, 7), Note = SheetValueHelper.GetString(r, 8),
        PerformedByUserId = SheetValueHelper.GetString(r, 9), PerformedByName = SheetValueHelper.GetString(r, 10)
    };

    private static ImplementationOtpApproval MapOtp(IList<object> r) => new()
    {
        Id = SheetValueHelper.GetString(r, 0), CaseId = SheetValueHelper.GetString(r, 1), Purpose = SheetValueHelper.GetString(r, 2),
        OtpHash = SheetValueHelper.GetString(r, 3), RequestedByUserId = SheetValueHelper.GetString(r, 4), RequestedByName = SheetValueHelper.GetString(r, 5),
        RequestedAt = SheetValueHelper.GetDateTime(r, 6) ?? DateTime.Now, ExpiresAt = SheetValueHelper.GetDateTime(r, 7) ?? DateTime.Now,
        ApproverNames = SheetValueHelper.GetString(r, 8), IsUsed = SheetValueHelper.GetBool(r, 9, false), UsedAt = SheetValueHelper.GetDateTime(r, 10),
        UsedByUserId = SheetValueHelper.GetString(r, 11), UsedByName = SheetValueHelper.GetString(r, 12)
    };

    private static IList<object> ToRow(ImplementationCase x) => new List<object>
    {
        x.Id, x.InquiryId, x.SaleDate.ToString("yyyy-MM-dd"), x.FirmName, x.PersonName, x.Mobile, x.Email, x.City, x.ProductName, x.LicenseCount,
        x.LicenseNumber, x.PinNumber, x.BillNumber, x.SoldByUserId, x.SoldByName, x.SoldByRole, x.SupportHeadId, x.SupportHeadName,
        x.AssignedMemberId, x.AssignedMemberName, x.Status, x.Stage, x.Priority, x.PlannedDays, x.ProgressPercent, x.CustomerContacted,
        x.PreferredStartDate?.ToString("yyyy-MM-dd") ?? string.Empty, x.PreferredTime, x.ActualStartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        x.CompletionDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, x.IsTrainingCompleted, x.LastProgressNote, x.TransferFromUserId,
        x.TransferFromName, x.TransferReason, x.FeedbackToken, x.FeedbackSentDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        x.FeedbackRating, x.FeedbackRemarks, x.FeedbackDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, x.AiRiskLevel, x.AiRecommendation,
        x.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"), x.CreatedByUserId, x.CreatedByName, x.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss"),
        x.LastUpdatedByUserId, x.LastUpdatedByName, x.ServiceType, x.IsPaidTraining, x.PaidTrainingAmount, x.PaymentReference, x.TrainingRequired,
        x.ClosedWithoutTraining, x.NoTrainingRemarks, x.ReopenedCount, x.LastReopenedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        x.LastReopenedByName, x.LastTransferDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, x.TrainingTopicsSummary,
        x.IsPaused, x.PauseReason, x.PausedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        x.PausedByName, x.ResumedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, x.StatusBeforePause,
        x.ManualProvided, x.IsManualPaid, x.ManualAmount, x.ManualPaymentReference
    };

    private static IList<object> ToRow(ImplementationSchedule x) => new List<object>
    {
        x.Id, x.CaseId, x.DayNumber, x.ScheduleDate.ToString("yyyy-MM-dd"), x.StartTime, x.EndTime, x.TopicPlan, x.TopicsCovered,
        x.Notes, x.Stage, x.Status, x.AssignedMemberId, x.AssignedMemberName, x.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        x.ReminderSentAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, x.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), x.CreatedByUserId,
        x.CreatedByName, x.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss")
    };

    private static IList<object> ToRow(ImplementationActivity x) => new List<object>
    {
        x.Id, x.CaseId, x.ActivityDate.ToString("yyyy-MM-dd HH:mm:ss"), x.ActivityType, x.FromUserId, x.FromUserName,
        x.ToUserId, x.ToUserName, x.Note, x.PerformedByUserId, x.PerformedByName
    };

    private static IList<object> ToRow(ImplementationOtpApproval x) => new List<object>
    {
        x.Id, x.CaseId, x.Purpose, x.OtpHash, x.RequestedByUserId, x.RequestedByName, x.RequestedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        x.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss"), x.ApproverNames, x.IsUsed, x.UsedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        x.UsedByUserId, x.UsedByName
    };

    private static IList<object> CaseHeaders() => new List<object>
    {
        "Id","InquiryId","SaleDate","FirmName","PersonName","Mobile","Email","City","ProductName","LicenseCount","LicenseNumber","PinNumber","BillNumber",
        "SoldByUserId","SoldByName","SoldByRole","SupportHeadId","SupportHeadName","AssignedMemberId","AssignedMemberName","Status","Stage","Priority","PlannedDays",
        "ProgressPercent","CustomerContacted","PreferredStartDate","PreferredTime","ActualStartDate","CompletionDate","IsTrainingCompleted","LastProgressNote",
        "TransferFromUserId","TransferFromName","TransferReason","FeedbackToken","FeedbackSentDate","FeedbackRating","FeedbackRemarks","FeedbackDate","AiRiskLevel",
        "AiRecommendation","CreatedDate","CreatedByUserId","CreatedByName","LastUpdated","LastUpdatedByUserId","LastUpdatedByName",
        "ServiceType","IsPaidTraining","PaidTrainingAmount","PaymentReference","TrainingRequired","ClosedWithoutTraining","NoTrainingRemarks",
        "ReopenedCount","LastReopenedDate","LastReopenedByName","LastTransferDate","TrainingTopicsSummary",
        "IsPaused","PauseReason","PausedDate","PausedByName","ResumedDate","StatusBeforePause",
        "ManualProvided","IsManualPaid","ManualAmount","ManualPaymentReference"
    };

    private static IList<object> ScheduleHeaders() => new List<object>
    {
        "Id","CaseId","DayNumber","ScheduleDate","StartTime","EndTime","TopicPlan","TopicsCovered","Notes","Stage","Status","AssignedMemberId",
        "AssignedMemberName","CompletedAt","ReminderSentAt","CreatedAt","CreatedByUserId","CreatedByName","LastUpdated"
    };

    private static IList<object> ActivityHeaders() => new List<object>
    {
        "Id","CaseId","ActivityDate","ActivityType","FromUserId","FromUserName","ToUserId","ToUserName","Note","PerformedByUserId","PerformedByName"
    };

    private static IList<object> OtpHeaders() => new List<object>
    {
        "Id","CaseId","Purpose","OtpHash","RequestedByUserId","RequestedByName","RequestedAt","ExpiresAt","ApproverNames","IsUsed","UsedAt","UsedByUserId","UsedByName"
    };
}
