using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;
using System.Security.Claims;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class InquiryController : Controller
{
    private readonly IInquiryService _inquiryService;
    private readonly IUserService _userService;
    private readonly IProductService _productService;
    private readonly IDealerService _dealerService;
    private readonly IPermissionService _permissionService;
    private readonly IImplementationService _implementationService;
    private readonly IInquiryDocumentService _documents;
    private readonly INotificationService _notifications;

    public InquiryController(IInquiryService inquiryService, IUserService userService, IProductService productService, IDealerService dealerService, IPermissionService permissionService, IImplementationService implementationService, IInquiryDocumentService documents, INotificationService notifications)
    {
        _inquiryService = inquiryService;
        _userService = userService;
        _productService = productService;
        _dealerService = dealerService;
        _permissionService = permissionService;
        _implementationService = implementationService;
        _documents = documents;
        _notifications = notifications;
    }

    public async Task<IActionResult> Index(InquiryFilterViewModel filter)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.view")) return RedirectToAction("AccessDenied", "Account");
        var role = CurrentRole();
        var userId = CurrentUserId();
        ViewBag.Filter = filter;
        var activePartners = await _userService.GetPartnersAsync();
        var partnerMasters = await _dealerService.GetAllAsync();
        ViewBag.Partners = activePartners;
        ViewBag.PartnerMargins = activePartners.ToDictionary(
            x => x.Id,
            x => partnerMasters.FirstOrDefault(d => d.Id.Equals(x.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || d.DealerName.Equals(x.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.MarginPercent ?? x.MarginPercent,
            StringComparer.OrdinalIgnoreCase);
        ViewBag.Users = (await _userService.GetActiveUsersAsync()).Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.SupportUsers = (await _userService.GetAllUsersAsync()).Where(x => x.Role.Equals("Support", StringComparison.OrdinalIgnoreCase) && x.IsActive).ToList();
        ViewBag.Products = (await _productService.GetAllAsync()).Where(x => x.IsActive).ToList();
        ViewBag.Statuses = InquiryService.StatusOptions;
        ViewBag.QualityOptions = InquiryService.QualityOptions;
        ViewBag.RoleName = role;
        ViewBag.IsSupport = role.Equals("Support", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsSupportHead = role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsPartner = role.Equals("Partner", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsUser = role.Equals("User", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        // Resolve action and smart-feature rights once. No smart option is inferred
        // from a role name; Role Wise and User Wise rights are the source of truth.
        var canEditInquiry = await _permissionService.HasPermissionAsync(User, "inquiry.edit");
        var canDeleteInquiry = await _permissionService.HasPermissionAsync(User, "inquiry.delete");
        var canUpdateInquiryStatus = await _permissionService.HasPermissionAsync(User, "inquiry.status");
        var canViewInquiryReport = await _permissionService.HasPermissionAsync(User, "inquiry.report.view");
        var canViewFollowupReminder = await _permissionService.HasPermissionAsync(User, "inquiry.followup.reminder.view");
        var canViewSmartFilters = await _permissionService.HasPermissionAsync(User, "inquiry.smartfilters.view");
        var canViewTimeline = await _permissionService.HasPermissionAsync(User, "inquiry.timeline.view");
        var canManageFollowup = await _permissionService.HasPermissionAsync(User, "inquiry.followup.manage");

        ViewBag.CanEditInquiry = canEditInquiry;
        ViewBag.CanDeleteInquiry = canDeleteInquiry;
        ViewBag.CanUpdateInquiryStatus = canUpdateInquiryStatus;
        ViewBag.CanViewInquiryReport = canViewInquiryReport;
        ViewBag.CanViewFollowupReminder = canViewFollowupReminder;
        ViewBag.CanViewSmartFilters = canViewSmartFilters;
        ViewBag.CanViewTimeline = canViewTimeline;
        ViewBag.CanManageFollowup = canManageFollowup;
        ViewBag.CanSetupImplementationFromSold = await _permissionService.HasPermissionAsync(User, "implementation.sold.setup");
        ViewBag.CanForwardAdmin = await _permissionService.HasPermissionAsync(User, "inquiry.assign.admin");
        ViewBag.CanForwardPartner = await _permissionService.HasPermissionAsync(User, "inquiry.assign.partner");
        ViewBag.CanForwardUser = await _permissionService.HasPermissionAsync(User, "inquiry.assign.user");

        // A denied smart-filter permission must also block a manually entered
        // ?Focus=Hot / MissingNextAction URL, not only hide the cards.
        if (!canViewSmartFilters) filter.Focus = null;

        var inquiries = await _inquiryService.SearchAsync(filter, role, userId);
        var updateMap = new Dictionary<string, List<InquiryUpdate>>();
        if (canViewTimeline)
        {
            foreach (var inquiry in inquiries.Take(100))
                updateMap[inquiry.Id] = await _inquiryService.GetUpdatesAsync(inquiry.Id);
        }
        ViewBag.UpdateMap = updateMap;

        // Point 2 fix: show "what was sent" right on the pipeline list itself,
        // so it doesn't have to be searched again inside each inquiry.
        var docSendSummary = new Dictionary<string, (int SentCount, string LastSentAt, string LastSendType)>();
        foreach (var inquiry in inquiries.Take(200))
        {
            var sends = await _documents.GetSendLogsAsync(inquiry.Id);
            if (sends.Count == 0) continue;
            var last = sends.OrderByDescending(s => s.SentAt).First();
            docSendSummary[inquiry.Id] = (sends.Count, last.SentAt.ToString("dd/MM/yyyy hh:mm tt"), last.SendType);
        }
        ViewBag.DocSendSummary = docSendSummary;
        return View(inquiries);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        // Create access is controlled only by Role Wise / Separate User Rights.
        var role = CurrentRole();
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.create")
            && !await _permissionService.HasPermissionAsync(User, "inquiry.create.support"))
            return RedirectToAction("AccessDenied", "Account");

        await LoadCreateEditBags();
        return View("Edit", new InquiryFormViewModel
        {
            SupportExecutiveName = (role.Equals("Support", StringComparison.OrdinalIgnoreCase) || role.Equals("Partner", StringComparison.OrdinalIgnoreCase)) ? (User.FindFirstValue(ClaimTypes.Name) ?? string.Empty) : string.Empty,
            InquirySource = role.Equals("Support", StringComparison.OrdinalIgnoreCase) ? "Support Team" : role.Equals("Partner", StringComparison.OrdinalIgnoreCase) ? "Partner" : "CRM"
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        // Timeline/detail is independently controlled in Role Wise/User Wise rights.
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.view")
            || !await _permissionService.HasPermissionAsync(User, "inquiry.timeline.view"))
            return RedirectToAction("AccessDenied", "Account");
        var inquiry = await _inquiryService.GetByIdAsync(id);
        if (inquiry == null) return NotFound();
        if (!await CanAccessInquiryAsync(inquiry)) return RedirectToAction("AccessDenied", "Account");
        await _inquiryService.MarkAttendedAsync(inquiry.Id, CurrentUserId(), User.Identity?.Name ?? string.Empty, CurrentRole());
        await LoadCreateEditBags();
        ViewBag.Updates = await _inquiryService.GetUpdatesAsync(id);
        ViewBag.IsReadOnly = true;
        return View("Edit", new InquiryFormViewModel
        {
            Id = inquiry.Id,
            CreatedDate = inquiry.CreatedDate,
            FirmName = inquiry.FirmName,
            PersonName = inquiry.PersonName,
            City = inquiry.City,
            Mobile1 = inquiry.Mobile1,
            Mobile2 = inquiry.Mobile2,
            Email1 = inquiry.Email1,
            Email2 = inquiry.Email2,
            ProductName = inquiry.ProductName,
            VersionType = inquiry.VersionType,
            Remarks = inquiry.Remarks,
            Status = inquiry.Status,
            StatusReason = inquiry.StatusReason,
            ForwardedToPartnerId = inquiry.ForwardedToPartnerId,
            ForwardedToUserId = inquiry.ForwardedToUserId,
            DealerId = inquiry.DealerId,
            NextFollowUpDate = inquiry.NextFollowUpDate,
            DemoScheduledDate = inquiry.DemoScheduledDate,
            DemoDoneDate = inquiry.DemoDoneDate,
            LicensesPurchased = inquiry.LicensesPurchased,
            LicenseNumber = inquiry.LicenseNumber,
            BillNo = inquiry.BillNo,
            BillDate = inquiry.BillDate,
            AmountWithoutGst = inquiry.AmountWithoutGst,
            AmountWithGst = inquiry.AmountWithGst,
            CloseReason = inquiry.CloseReason,
            CustomerInfo = inquiry.CustomerInfo,
            SupportExecutiveName = inquiry.SupportExecutiveName,
            InquirySource = inquiry.InquirySource,
            InquiryQuality = inquiry.InquiryQuality
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(InquiryFormViewModel model)
    {
        // Create access is controlled only by Role Wise / Separate User Rights.
        var role = CurrentRole();
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.create")
            && !await _permissionService.HasPermissionAsync(User, "inquiry.create.support"))
            return RedirectToAction("AccessDenied", "Account");
        var canManageFollowup = await _permissionService.HasPermissionAsync(User, "inquiry.followup.manage");
        if (!canManageFollowup)
        {
            model.NextFollowUpDate = null;
            model.DemoScheduledDate = null;
            model.DemoDoneDate = null;
            model.StatusReason = string.Empty;
        }

        // Product selection is optional. Remove any automatic/non-nullable model validation for ProductName.
        ModelState.Remove(nameof(InquiryFormViewModel.ProductName));
        ValidateInquiry(model, canManageFollowup);
        ModelState.Remove(nameof(InquiryFormViewModel.ProductName));
        if (CurrentRole().Equals("Support", StringComparison.OrdinalIgnoreCase))
        {
            model.ForwardedToPartnerId = string.Empty;
            model.SupportExecutiveName = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            model.InquirySource = "Support Team";
        }
        if (CurrentRole().Equals("Partner", StringComparison.OrdinalIgnoreCase)) model.ForwardedToPartnerId = CurrentUserId();
        if (!ModelState.IsValid) { await LoadCreateEditBags(); return View("Edit", model); }
        await _inquiryService.CreateAsync(model, CurrentUserId(), User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, CurrentRole());
        TempData["Success"] = CurrentRole().Equals("Support", StringComparison.OrdinalIgnoreCase) ? "Inquiry sent to Admin successfully." : "Inquiry saved successfully.";
        var monthlyCount = await _inquiryService.GetMonthlySubmittedCountAsync(CurrentUserId(), DateTime.Today);
        if (monthlyCount > 0 && monthlyCount % 5 == 0 && await _permissionService.HasPermissionAsync(User, "inquiry.achievement.view"))
            TempData["AchievementFlash"] = $"🎉👏 Excellent work! You have completed {monthlyCount} inquiries this month. Keep growing! 🚀🏆";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        // Require explicit edit permission to open the full Edit page.
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.edit")) return RedirectToAction("AccessDenied", "Account");
        var inquiry = await _inquiryService.GetByIdAsync(id);
        if (inquiry == null) return NotFound();
        if (!await CanAccessInquiryAsync(inquiry)) return RedirectToAction("AccessDenied", "Account");
        await _inquiryService.MarkAttendedAsync(inquiry.Id, CurrentUserId(), User.Identity?.Name ?? string.Empty, CurrentRole());
        await LoadCreateEditBags();
        ViewBag.Updates = await _permissionService.HasPermissionAsync(User, "inquiry.timeline.view")
            ? await _inquiryService.GetUpdatesAsync(id)
            : new List<InquiryUpdate>();
        return View(new InquiryFormViewModel
        {
            Id = inquiry.Id,
            CreatedDate = inquiry.CreatedDate,
            FirmName = inquiry.FirmName,
            PersonName = inquiry.PersonName,
            City = inquiry.City,
            Mobile1 = inquiry.Mobile1,
            Mobile2 = inquiry.Mobile2,
            Email1 = inquiry.Email1,
            Email2 = inquiry.Email2,
            ProductName = inquiry.ProductName,
            VersionType = inquiry.VersionType,
            Remarks = inquiry.Remarks,
            Status = inquiry.Status,
            StatusReason = inquiry.StatusReason,
            ForwardedToPartnerId = inquiry.ForwardedToPartnerId,
            ForwardedToUserId = inquiry.ForwardedToUserId,
            DealerId = inquiry.DealerId,
            NextFollowUpDate = inquiry.NextFollowUpDate,
            DemoScheduledDate = inquiry.DemoScheduledDate,
            DemoDoneDate = inquiry.DemoDoneDate,
            LicensesPurchased = inquiry.LicensesPurchased,
            LicenseNumber = inquiry.LicenseNumber,
            BillNo = inquiry.BillNo,
            BillDate = inquiry.BillDate,
            AmountWithoutGst = inquiry.AmountWithoutGst,
            AmountWithGst = inquiry.AmountWithGst,
            CloseReason = inquiry.CloseReason,
            CustomerInfo = inquiry.CustomerInfo,
            SupportExecutiveName = inquiry.SupportExecutiveName,
            InquirySource = inquiry.InquirySource,
            InquiryQuality = inquiry.InquiryQuality
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(InquiryFormViewModel model, bool setupImplementation = false)
    {
        // Full inquiry edit requires explicit permission 'inquiry.edit'. Allow Admin by default via permission service.
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.edit")) return RedirectToAction("AccessDenied", "Account");
        var existing = string.IsNullOrWhiteSpace(model.Id) ? null : await _inquiryService.GetByIdAsync(model.Id);
        if (existing == null) return NotFound();
        if (!await CanAccessInquiryAsync(existing)) return RedirectToAction("AccessDenied", "Account");

        var role = CurrentRole();
        var canUpdateStatus = await _permissionService.HasPermissionAsync(User, "inquiry.status");
        var canManageFollowup = await _permissionService.HasPermissionAsync(User, "inquiry.followup.manage");
        var canEditAmount = await _permissionService.HasPermissionAsync(User, "field.inquiry.amount");
        var canEditCustomerInfo = await _permissionService.HasPermissionAsync(User, "field.inquiry.customerinfo");

        // Non-admin users get the same compact layout as their New Inquiry screen.
        // Preserve fields which are intentionally not shown in that compact form.
        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            model.SupportExecutiveName = existing.SupportExecutiveName;
            model.InquirySource = existing.InquirySource;
            model.Email2 = existing.Email2;
            model.VersionType = existing.VersionType;
            model.DealerId = existing.DealerId;
            model.ForwardedToPartnerId = role.Equals("Partner", StringComparison.OrdinalIgnoreCase)
                ? CurrentUserId()
                : existing.ForwardedToPartnerId;
            model.ForwardedToUserId = existing.ForwardedToUserId;
        }

        // Inquiry Status and Follow-up Manage are independent rights. Preserve
        // denied fields server-side so crafted POST requests cannot bypass UI.
        if (!canUpdateStatus)
        {
            model.Status = existing.Status;
            model.InquiryQuality = existing.InquiryQuality;
            model.LicensesPurchased = existing.LicensesPurchased;
            model.LicenseNumber = existing.LicenseNumber;
            model.BillNo = existing.BillNo;
            model.BillDate = existing.BillDate;
            model.CloseReason = existing.CloseReason;
        }

        if (!canManageFollowup)
        {
            model.StatusReason = existing.StatusReason;
            model.NextFollowUpDate = existing.NextFollowUpDate;
            model.DemoScheduledDate = existing.DemoScheduledDate;
            model.DemoDoneDate = existing.DemoDoneDate;
        }

        if (!canEditAmount)
        {
            model.AmountWithoutGst = existing.AmountWithoutGst;
            model.AmountWithGst = existing.AmountWithGst;
        }

        if (!canEditCustomerInfo)
            model.CustomerInfo = existing.CustomerInfo;

        // Latest Follow-up Note is optional (not compulsory) even when the status changes.
        // If the user leaves it blank, auto-fill a simple status-change note instead of blocking save.
        var statusChanged = canUpdateStatus
            && !string.Equals(model.Status, existing.Status, StringComparison.OrdinalIgnoreCase);
        if (statusChanged
            && (string.IsNullOrWhiteSpace(model.StatusReason)
                || string.Equals((model.StatusReason ?? string.Empty).Trim(), existing.StatusReason?.Trim(), StringComparison.Ordinal)))
        {
            model.StatusReason = $"Status changed from {existing.Status} to {model.Status}.";
        }

        // Product selection is optional. Remove any automatic/non-nullable model validation for ProductName.
        ModelState.Remove(nameof(InquiryFormViewModel.ProductName));
        ValidateInquiry(model, canManageFollowup);
        ModelState.Remove(nameof(InquiryFormViewModel.ProductName));
        if (!ModelState.IsValid)
        {
            await LoadCreateEditBags();
            ViewBag.Updates = await _permissionService.HasPermissionAsync(User, "inquiry.timeline.view")
                ? await _inquiryService.GetUpdatesAsync(existing.Id)
                : new List<InquiryUpdate>();
            return View(model);
        }

        if (statusChanged && model.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && setupImplementation
            && !await _permissionService.HasPermissionAsync(User, "implementation.sold.setup"))
            return RedirectToAction("AccessDenied", "Account");

        await _inquiryService.UpdateAsync(model, CurrentUserId(), User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, role);

        if (statusChanged && model.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && setupImplementation)
        {
            try
            {
                var soldInquiry = await _inquiryService.GetByIdAsync(existing.Id);
                var implementation = soldInquiry == null
                    ? null
                    : await _implementationService.EnsureFromSoldInquiryAsync(soldInquiry, CurrentUserId(), User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, role);
                if (implementation != null)
                {
                    TempData["Success"] = "Sale saved and implementation setup created from the inquiry details. Complete assignment and schedule.";
                    if (await _permissionService.HasPermissionAsync(User, "implementation.view"))
                        return RedirectToAction("Details", "Implementation", new { id = implementation.Id });
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Sale saved, but implementation setup could not be created: " + ex.Message;
            }
        }

        TempData["Success"] ??= "Inquiry and follow-up updated successfully.";
        return RedirectToAction(nameof(Index), null, null, "inquiryReport");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        // Deletion is controlled only by the permission matrix, never by a role name.
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.delete"))
            return RedirectToAction("AccessDenied", "Account");

        var inquiry = await _inquiryService.GetByIdAsync(id);
        if (inquiry == null) return NotFound();

        // A user may delete only an inquiry that belongs to their normal data scope.
        if (!await CanAccessInquiryAsync(inquiry))
            return RedirectToAction("AccessDenied", "Account");

        await _inquiryService.DeleteAsync(id);
        TempData["Success"] = "Inquiry deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(string inquiryId, string? status, string note, DateTime? nextFollowUp, string? licenseNumber, string? billNo, DateTime? billDate, string? closeReason, decimal? amountWithoutGst, decimal? amountWithGst, string? inquiryQuality, string? soldProductName, int? soldLicenses, string? forwardToPartnerId, string? forwardToUserId, bool setupImplementation = false)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.status")) return RedirectToAction("AccessDenied", "Account");
        var canManageFollowup = await _permissionService.HasPermissionAsync(User, "inquiry.followup.manage");
        var inquiry = await _inquiryService.GetByIdAsync(inquiryId);
        if (inquiry == null) return NotFound();
        if (!await CanAccessInquiryAsync(inquiry)) return RedirectToAction("AccessDenied", "Account");

        var postedStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
        var effectiveStatus = string.IsNullOrWhiteSpace(postedStatus) ? inquiry.Status : postedStatus;
        inquiryQuality = string.IsNullOrWhiteSpace(inquiryQuality) ? inquiry.InquiryQuality : inquiryQuality.Trim();

        if (effectiveStatus.Equals("Forwarded to Admin", StringComparison.OrdinalIgnoreCase)
            && !await _permissionService.HasPermissionAsync(User, "inquiry.assign.admin"))
            return RedirectToAction("AccessDenied", "Account");
        if (effectiveStatus.Equals("Forwarded to Partner", StringComparison.OrdinalIgnoreCase))
        {
            if (!await _permissionService.HasPermissionAsync(User, "inquiry.assign.partner")) return RedirectToAction("AccessDenied", "Account");
            var targetPartner = string.IsNullOrWhiteSpace(forwardToPartnerId) ? null : await _userService.GetByIdAsync(forwardToPartnerId);
            if (targetPartner == null || !targetPartner.IsActive || !targetPartner.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select a valid active partner to forward this inquiry.";
                return RedirectToAction(nameof(Index));
            }
        }
        if (effectiveStatus.Equals("Forwarded to User", StringComparison.OrdinalIgnoreCase))
        {
            if (!await _permissionService.HasPermissionAsync(User, "inquiry.assign.user")) return RedirectToAction("AccessDenied", "Account");
            var targetUser = string.IsNullOrWhiteSpace(forwardToUserId) ? null : await _userService.GetByIdAsync(forwardToUserId);
            if (targetUser == null || !targetUser.IsActive || !targetUser.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select a valid active user to forward this inquiry.";
                return RedirectToAction(nameof(Index));
            }
        }

        if (canManageFollowup)
        {
            // Close already has its own "Close reason" field capturing why the
            // inquiry was closed — don't also force a separate Update note
            // just to satisfy this check. Fall back to the close reason so
            // the timeline entry still has meaningful text.
            if (string.IsNullOrWhiteSpace(note) && effectiveStatus.Equals("Close", StringComparison.OrdinalIgnoreCase))
            {
                note = closeReason ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(note))
            {
                TempData["Error"] = "Update note is required before saving status.";
                return RedirectToAction(nameof(Index));
            }

            if (string.Equals(inquiryQuality, "Not Genuine", StringComparison.OrdinalIgnoreCase))
            {
                nextFollowUp = null;
            }
            else if (!string.IsNullOrWhiteSpace(postedStatus)
                && !effectiveStatus.Equals("Sold", StringComparison.OrdinalIgnoreCase)
                && !effectiveStatus.Equals("Close", StringComparison.OrdinalIgnoreCase)
                && !effectiveStatus.StartsWith("Forwarded to ", StringComparison.OrdinalIgnoreCase)
                && !nextFollowUp.HasValue)
            {
                TempData["Error"] = "Action date is required before saving status.";
                return RedirectToAction(nameof(Index));
            }
        }
        else
        {
            // Follow-up fields are protected independently from status. Preserve
            // the current date and generate an audit note for a permitted status change.
            nextFollowUp = inquiry.NextFollowUpDate;
            note = !string.IsNullOrWhiteSpace(postedStatus)
                ? $"Status changed from {inquiry.Status} to {effectiveStatus}."
                : $"Genuine status changed from {(string.IsNullOrWhiteSpace(inquiry.InquiryQuality) ? "Pending" : inquiry.InquiryQuality)} to {(string.IsNullOrWhiteSpace(inquiryQuality) ? "Pending" : inquiryQuality)}.";
        }

        if (postedStatus.Equals("Sold", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(soldProductName))
            {
                TempData["Error"] = "Product name is required for Sold status.";
                return RedirectToAction(nameof(Index));
            }
            if (string.IsNullOrWhiteSpace(licenseNumber) || string.IsNullOrWhiteSpace(billNo) || !billDate.HasValue)
            {
                TempData["Error"] = "License number, bill number and bill date are required for Sold status.";
                return RedirectToAction(nameof(Index));
            }
        }

        // Close reason separate textbox removed from UI — use note as close reason when empty.
        if (postedStatus.Equals("Close", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(closeReason))
        {
            closeReason = string.IsNullOrWhiteSpace(note) ? "Closed" : note.Trim();
        }

        if (setupImplementation && !await _permissionService.HasPermissionAsync(User, "implementation.sold.setup"))
            return RedirectToAction("AccessDenied", "Account");

        await _inquiryService.UpdateStatusAsync(inquiryId, postedStatus, note, CurrentUserId(), User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, CurrentRole(), nextFollowUp, null, null, licenseNumber, billNo, billDate, closeReason, amountWithoutGst, amountWithGst, inquiryQuality, soldProductName, soldLicenses, forwardToPartnerId, forwardToUserId);

        if (postedStatus.Equals("Sold", StringComparison.OrdinalIgnoreCase) && setupImplementation)
        {
            try
            {
                var soldInquiry = await _inquiryService.GetByIdAsync(inquiryId);
                var implementation = soldInquiry == null
                    ? null
                    : await _implementationService.EnsureFromSoldInquiryAsync(soldInquiry, CurrentUserId(), User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, CurrentRole());
                if (implementation != null)
                {
                    TempData["Success"] = "Inquiry marked Sold and implementation setup created from the sold inquiry. Complete assignment and schedule.";
                    if (await _permissionService.HasPermissionAsync(User, "implementation.view"))
                        return RedirectToAction("Details", "Implementation", new { id = implementation.Id });
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Sale saved, but implementation setup could not be created: " + ex.Message;
            }
        }

        TempData["Success"] ??= postedStatus.Equals("Sold", StringComparison.OrdinalIgnoreCase)
            ? "Inquiry marked Sold successfully. Implementation setup was not created."
            : "Inquiry status updated successfully. Result now shows the latest status remark.";
        return RedirectToAction(nameof(Index), null, null, "inquiryReport");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PendingAttention()
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.attention.alert.view")) return Json(new { count = 0, items = Array.Empty<object>() });
        var items = await _inquiryService.GetPendingAttentionAsync(CurrentRole(), CurrentUserId());
        var now = DateTime.Now;
        return Json(new
        {
            count = items.Count,
            items = items.Select(x => new
            {
                x.Id,
                customer = string.IsNullOrWhiteSpace(x.FirmName) ? x.PersonName : x.FirmName,
                name = x.PersonName ?? string.Empty,
                firm = x.FirmName ?? string.Empty,
                city = x.City ?? string.Empty,
                x.Status,
                reason = !x.IsAttended ? $"Forwarded by {x.ForwardedByName}" : "Next action overdue",
                since = (!x.IsAttended ? x.ForwardedDate : x.NextFollowUpDate) ?? x.LastUpdated,
                lastUpdated = x.LastUpdated,
                lastUpdatedText = x.LastUpdated.ToString("dd-MMM-yyyy HH:mm"),
                elapsedMinutes = Math.Max(0, (int)(now - ((!x.IsAttended ? x.ForwardedDate : x.NextFollowUpDate) ?? x.LastUpdated)).TotalMinutes),
                // Change (2026-08-06): surface the customer's mobile number so the popup/flash
                // is actionable on its own (call them) without opening the inquiry first.
                mobile = string.IsNullOrWhiteSpace(x.Mobile1) ? x.Mobile2 : x.Mobile1,
                url = Url.Action("Details", "Inquiry", new { id = x.Id })
            })
        });
    }

    private void ValidateInquiry(InquiryFormViewModel model, bool requireNextAction = true)
    {
        if (string.IsNullOrWhiteSpace(model.FirmName) && string.IsNullOrWhiteSpace(model.Mobile1) && string.IsNullOrWhiteSpace(model.PersonName))
            ModelState.AddModelError(string.Empty, "At least firm name, person name or mobile should be entered.");

        if (model.Status == "Sold")
        {
            if (string.IsNullOrWhiteSpace(model.LicenseNumber)) ModelState.AddModelError(nameof(model.LicenseNumber), "License number is required when status is Sold.");
            if (string.IsNullOrWhiteSpace(model.BillNo)) ModelState.AddModelError(nameof(model.BillNo), "Bill number is required when status is Sold.");
            if (!model.BillDate.HasValue) ModelState.AddModelError(nameof(model.BillDate), "Bill date is required when status is Sold.");
        }

        if (model.Status == "Close" && string.IsNullOrWhiteSpace(model.CloseReason))
            ModelState.AddModelError(nameof(model.CloseReason), "Close reason is required when status is Close.");

        var requiresNextAction = model.Status == "Follow Up"
            || model.Status == "Demo Scheduled"
            || model.Status == "Demo Done"
            || model.Status == "Negotiation";
        if (requireNextAction
            && requiresNextAction
            && !string.Equals(model.InquiryQuality, "Not Genuine", StringComparison.OrdinalIgnoreCase)
            && !model.NextFollowUpDate.HasValue)
        {
            ModelState.AddModelError(nameof(model.NextFollowUpDate), "Next Follow Up date is required for an active inquiry.");
        }
    }


    [HttpGet]
    public async Task<IActionResult> Documents(string id)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.view")) return Unauthorized();
        var docs = await _documents.GetForInquiryAsync(id);
        var sends = await _documents.GetSendLogsAsync(id);
        var docLookup = docs.ToDictionary(d => d.Id, d => d, StringComparer.OrdinalIgnoreCase);
        return Json(new {
            documents = docs.Select(d => new {
                d.Id, d.DocType, d.FileName, d.ContentType, d.FileSize,
                uploadedAt = d.UploadedAt.ToString("dd/MM/yyyy hh:mm tt"),
                d.UploadedByName, d.Notes,
                url = Url.Action("DownloadDocument", "Inquiry", new { id, documentId = d.Id })
            }),
            // Point 2 fix: resolve each send log's DocumentIds back into actual
            // file names + open/download links, so "what exactly did we send"
            // is answered right here — no need to search the Inquiry Pipeline.
            sendLogs = sends.Select(s => new {
                s.Id, s.SendType, s.Channel, s.Status, s.WhatsAppUrl,
                sentAt = s.SentAt.ToString("dd/MM/yyyy hh:mm tt"),
                s.SentByName, s.RecipientEmail, s.RecipientMobile, s.Message,
                sentFiles = (s.DocumentIds ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(docLookup.ContainsKey)
                    .Select(docId => new {
                        id = docId,
                        fileName = docLookup[docId].FileName,
                        docType = docLookup[docId].DocType,
                        url = Url.Action("DownloadDocument", "Inquiry", new { id, documentId = docId })
                    })
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadDocument(string id, string docType, IFormFile file, string? notes)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.edit")
            && !await _permissionService.HasPermissionAsync(User, "inquiry.documents.send"))
            return Unauthorized();
        var inquiry = await _inquiryService.GetByIdAsync(id);
        if (inquiry == null) return NotFound();
        try
        {
            var doc = await _documents.SaveAsync(id, file, docType ?? "Other", CurrentUserId(), CurrentName(), notes);
            return Json(new { ok = true, id = doc.Id, fileName = doc.FileName, docType = doc.DocType });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(string id, string documentId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.edit")
            && !await _permissionService.HasPermissionAsync(User, "inquiry.documents.send"))
            return Unauthorized();
        await _documents.DeleteAsync(id, documentId);
        return Json(new { ok = true });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(string id, string documentId)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.view")) return Unauthorized();
        var docs = await _documents.GetForInquiryAsync(id);
        var doc = docs.FirstOrDefault(x => x.Id.Equals(documentId, StringComparison.OrdinalIgnoreCase));
        var path = await _documents.GetPhysicalPathAsync(id, documentId);
        if (doc == null || path == null) return NotFound();
        return PhysicalFile(path, doc.ContentType, doc.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendDocuments(string id, string sendType, string channel, string[]? documentIds, string? message)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.documents.send")
            && !await _permissionService.HasPermissionAsync(User, "inquiry.edit"))
            return Unauthorized();

        var inquiry = await _inquiryService.GetByIdAsync(id);
        if (inquiry == null) return NotFound();

        var allDocs = await _documents.GetForInquiryAsync(id);
        var selected = (documentIds ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        var usedIds = new List<string>();
        foreach (var d in allDocs)
        {
            if (selected.Count > 0 && !selected.Contains(d.Id)) continue;
            var path = await _documents.GetPhysicalPathAsync(id, d.Id);
            if (path != null) { paths.Add(path); usedIds.Add(d.Id); }
        }

        // Auto-pick by send type if nothing selected
        if (paths.Count == 0)
        {
            var prefer = sendType switch
            {
                "Quotation" => new[] { "Quotation" },
                "PaymentPack" => new[] { "PriceList", "BankDetails", "UpiImage" },
                "Brochure" => new[] { "Brochure" },
                _ => Array.Empty<string>()
            };
            foreach (var d in allDocs.Where(x => prefer.Contains(x.DocType, StringComparer.OrdinalIgnoreCase)))
            {
                var path = await _documents.GetPhysicalPathAsync(id, d.Id);
                if (path != null) { paths.Add(path); usedIds.Add(d.Id); }
            }
        }

        AppUser? sender = null;
        try { sender = await _userService.GetByIdAsync(CurrentUserId()); } catch { }

        var (emailSent, waUrl, status) = await _notifications.SendInquiryDocumentsAsync(
            inquiry, sendType ?? "Quotation", channel ?? "Both", paths, message, sender);

        await _documents.AddSendLogAsync(new InquirySendLog
        {
            InquiryId = id,
            SendType = sendType ?? "Quotation",
            Channel = channel ?? "Both",
            RecipientEmail = inquiry.Email1 ?? "",
            RecipientMobile = inquiry.Mobile1 ?? inquiry.Mobile2 ?? "",
            DocumentIds = string.Join(",", usedIds),
            Message = message ?? "",
            Status = status,
            WhatsAppUrl = waUrl ?? "",
            SentByUserId = CurrentUserId(),
            SentByName = CurrentName()
        });

        return Json(new { ok = true, status, whatsAppUrl = waUrl, emailSent, documentCount = paths.Count });
    }

    private async Task LoadCreateEditBags()
    {
        var activePartners = await _userService.GetPartnersAsync();
        var partnerMasters = await _dealerService.GetAllAsync();
        ViewBag.Partners = activePartners;
        ViewBag.PartnerMargins = activePartners.ToDictionary(
            x => x.Id,
            x => partnerMasters.FirstOrDefault(d => d.Id.Equals(x.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || d.DealerName.Equals(x.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.MarginPercent ?? x.MarginPercent,
            StringComparer.OrdinalIgnoreCase);
        ViewBag.Users = (await _userService.GetActiveUsersAsync()).Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.CanForwardAdmin = await _permissionService.HasPermissionAsync(User, "inquiry.assign.admin");
        ViewBag.CanForwardPartner = await _permissionService.HasPermissionAsync(User, "inquiry.assign.partner");
        ViewBag.CanForwardUser = await _permissionService.HasPermissionAsync(User, "inquiry.assign.user");
        ViewBag.CanSetupImplementationFromSold = await _permissionService.HasPermissionAsync(User, "implementation.sold.setup");
        ViewBag.Products = (await _productService.GetAllAsync()).Where(x => x.IsActive).ToList();
        ViewBag.Dealers = await _dealerService.GetActiveAsync();
        ViewBag.Statuses = InquiryService.StatusOptions;
        ViewBag.QualityOptions = InquiryService.QualityOptions;
        var role = CurrentRole();
        ViewBag.RoleName = role;
        ViewBag.IsSupport = role.Equals("Support", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsSupportHead = role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsPartner = role.Equals("Partner", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsUser = role.Equals("User", StringComparison.OrdinalIgnoreCase);
        ViewBag.IsAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Lightweight remarks-only update for the pipeline modal (AJAX).
    /// Preserves existing status, follow-up date, and quality.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRemarks(string inquiryId, string note)
    {
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.status"))
            return Json(new { success = false, message = "Access denied." });
        if (!await _permissionService.HasPermissionAsync(User, "inquiry.followup.manage")
            && !await _permissionService.HasPermissionAsync(User, "inquiry.status"))
            return Json(new { success = false, message = "You cannot update remarks." });

        var inquiry = await _inquiryService.GetByIdAsync(inquiryId);
        if (inquiry == null) return Json(new { success = false, message = "Inquiry not found." });
        if (!await CanAccessInquiryAsync(inquiry))
            return Json(new { success = false, message = "Access denied for this inquiry." });

        note = (note ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(note))
            return Json(new { success = false, message = "Remarks are required." });

        await _inquiryService.UpdateStatusAsync(
            inquiryId,
            inquiry.Status,
            note,
            CurrentUserId(),
            User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            CurrentRole(),
            inquiry.NextFollowUpDate,
            null, null,
            inquiry.LicenseNumber,
            inquiry.BillNo,
            inquiry.BillDate,
            inquiry.CloseReason,
            inquiry.AmountWithoutGst,
            inquiry.AmountWithGst,
            inquiry.InquiryQuality,
            null, null, null, null);

        return Json(new { success = true, message = "Remarks updated.", note, inquiryId });
    }

    private async Task<bool> CanAccessInquiryAsync(Inquiry inquiry)
    {
        // Use the same data-scope rule as the Inquiry list. This keeps access
        // consistent for Admin, Support, SupportHead, Partner, User, and any
        // future/custom role without hard-coding action rights to a role name.
        var scopedInquiries = await _inquiryService.SearchAsync(
            new InquiryFilterViewModel(),
            CurrentRole(),
            CurrentUserId());

        return scopedInquiries.Any(x => x.Id.Equals(inquiry.Id, StringComparison.OrdinalIgnoreCase));
    }

    private string CurrentRole() => User.FindFirstValue(ClaimTypes.Role) ?? "User";
    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentName() => User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "User";
}
