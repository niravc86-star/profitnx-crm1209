using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;
using System.Security.Claims;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class ImplementationController : Controller
{
    private readonly IImplementationService _implementationService;
    private readonly IPermissionService _permissionService;
    private readonly IUserService _userService;

    public ImplementationController(IImplementationService implementationService, IPermissionService permissionService, IUserService userService)
    {
        _implementationService = implementationService;
        _permissionService = permissionService;
        _userService = userService;
    }

    public async Task<IActionResult> Index(string? search, string? status, string? memberId, string? risk)
    {
        if (!await Has("implementation.view")) return AccessDenied();
        var cases = await _implementationService.GetVisibleCasesAsync(CurrentRole(), CurrentUserId());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            cases = cases.Where(x => x.FirmName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.PersonName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Mobile.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.City.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.LicenseNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(status)) cases = cases.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(memberId)) cases = cases.Where(x => x.AssignedMemberId.Equals(memberId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(risk)) cases = cases.Where(x => x.AiRiskLevel.Equals(risk, StringComparison.OrdinalIgnoreCase)).ToList();

        var upcoming = await _implementationService.GetUpcomingForUserAsync(CurrentRole(), CurrentUserId(), DateTime.Now, DateTime.Now.AddDays(7));
        var rated = cases.Where(x => x.FeedbackRating > 0).ToList();
        var vm = new ImplementationDashboardViewModel
        {
            Cases = cases,
            UpcomingSchedules = upcoming,
            TotalCases = cases.Count,
            AwaitingAssignment = cases.Count(x => string.IsNullOrWhiteSpace(x.AssignedMemberId) && !x.IsTrainingCompleted),
            InProgress = cases.Count(x => !x.IsTrainingCompleted && x.ProgressPercent > 0),
            Completed = cases.Count(x => x.IsTrainingCompleted),
            AtRisk = cases.Count(x => x.AiRiskLevel is "High" or "Critical"),
            AverageRating = rated.Count == 0 ? 0 : Math.Round((decimal)rated.Average(x => x.FeedbackRating), 1),
            ExecutivePerformance = rated.Where(x => !string.IsNullOrWhiteSpace(x.AssignedMemberId))
                .GroupBy(x => new { x.AssignedMemberId, x.AssignedMemberName })
                .Select(g =>
                {
                    var average = Math.Round((decimal)g.Average(x => x.FeedbackRating), 1);
                    return new ImplementationExecutivePerformance
                    {
                        UserId = g.Key.AssignedMemberId,
                        UserName = g.Key.AssignedMemberName,
                        FeedbackCount = g.Count(),
                        AverageRating = average,
                        Level = average >= 4m ? "Pro" : average >= 3m ? "Intermediate" : "Beginner"
                    };
                }).OrderByDescending(x => x.AverageRating).ThenByDescending(x => x.FeedbackCount).ToList()
        };
        await LoadViewBagsAsync();
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.MemberId = memberId;
        ViewBag.Risk = risk;
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create(string? inquiryId)
    {
        if (!await Has("implementation.create")) return AccessDenied();
        await LoadViewBagsAsync();
        return View("Edit", new ImplementationFormViewModel { InquiryId = inquiryId ?? string.Empty, SaleDate = DateTime.Today, LicenseCount = 1, Priority = "Normal", TrainingRequired = true, ServiceType = "New Implementation" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImplementationFormViewModel model)
    {
        if (!await Has("implementation.create")) return AccessDenied();
        if (!await Has("implementation.assign"))
        {
            model.SupportHeadId = string.Empty;
            model.AssignedMemberId = string.Empty;
        }
        if ((model.IsPaidTraining || string.Equals(model.ServiceType, "Paid Training", StringComparison.OrdinalIgnoreCase))
            && !await Has("implementation.paidtraining"))
            ModelState.AddModelError(nameof(model.ServiceType), "You do not have permission to create paid training records.");
        if (!ModelState.IsValid) { await LoadViewBagsAsync(); return View("Edit", model); }
        var item = await _implementationService.CreateAsync(model, CurrentUserId(), CurrentName(), CurrentRole());
        TempData["Success"] = "Implementation / training record created and relevant users notified.";
        return RedirectToAction(nameof(Details), new { id = item.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await Has("implementation.edit")) return AccessDenied();
        var item = await GetAccessibleAsync(id);
        if (item == null) return AccessDenied();
        await LoadViewBagsAsync();
        return View(new ImplementationFormViewModel
        {
            Id = item.Id, InquiryId = item.InquiryId, FirmName = item.FirmName, PersonName = item.PersonName,
            Mobile = item.Mobile, Email = item.Email, City = item.City, ProductName = item.ProductName,
            LicenseCount = item.LicenseCount, LicenseNumber = item.LicenseNumber, PinNumber = item.PinNumber,
            BillNumber = item.BillNumber, SaleDate = item.SaleDate, Priority = item.Priority,
            SupportHeadId = item.SupportHeadId, AssignedMemberId = item.AssignedMemberId,
            CustomerContacted = item.CustomerContacted, PreferredStartDate = item.PreferredStartDate,
            PreferredTime = item.PreferredTime, PlannedDays = item.PlannedDays, Notes = item.LastProgressNote,
            ServiceType = item.ServiceType, IsPaidTraining = item.IsPaidTraining, PaidTrainingAmount = item.PaidTrainingAmount,
            PaymentReference = item.PaymentReference, TrainingRequired = item.TrainingRequired,
            ManualProvided = item.ManualProvided, IsManualPaid = item.IsManualPaid,
            ManualAmount = item.ManualAmount, ManualPaymentReference = item.ManualPaymentReference
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ImplementationFormViewModel model)
    {
        if (!await Has("implementation.edit")) return AccessDenied();
        if (await GetAccessibleAsync(model.Id) == null) return AccessDenied();
        if ((model.IsPaidTraining || string.Equals(model.ServiceType, "Paid Training", StringComparison.OrdinalIgnoreCase))
            && !await Has("implementation.paidtraining"))
            ModelState.AddModelError(nameof(model.ServiceType), "You do not have permission to maintain paid training records.");
        if (!ModelState.IsValid) { await LoadViewBagsAsync(); return View(model); }
        await _implementationService.UpdateAsync(model, CurrentUserId(), CurrentName());
        TempData["Success"] = "Implementation and training workflow updated. Customer master details remained locked.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        if (!await Has("implementation.view")) return AccessDenied();
        var item = await GetAccessibleAsync(id);
        if (item == null) return AccessDenied();
        ViewBag.Schedules = await _implementationService.GetSchedulesAsync(id);
        ViewBag.Activities = await _implementationService.GetActivitiesAsync(id);
        await LoadViewBagsAsync();
        var actionAllowed = IsActionAllowed(item);
        ViewBag.IsTransferredHistoryView = item.IsTransferredHistoryView;
        ViewBag.CanEdit = actionAllowed && await Has("implementation.edit");
        ViewBag.CanAssign = actionAllowed && await Has("implementation.assign");
        ViewBag.CanSchedule = actionAllowed && await Has("implementation.schedule");
        ViewBag.CanProgress = actionAllowed && await Has("implementation.progress");
        ViewBag.CanTransfer = actionAllowed && await Has("implementation.transfer");
        ViewBag.CanComplete = actionAllowed && await Has("implementation.complete");
        ViewBag.CanReopen = actionAllowed && await Has("implementation.reopen");
        ViewBag.CanCloseNoTraining = actionAllowed && await Has("implementation.close.notrequired");
        ViewBag.CanPause = actionAllowed && await Has("implementation.pause");
        ViewBag.CanDelete = await Has("implementation.delete");
        ViewBag.CanViewFeedback = await Has("implementation.feedback.view");
        ViewBag.CanViewPin = await Has("field.implementation.pin");
        ViewBag.CanPaidTraining = await Has("implementation.paidtraining");
        ViewBag.CanViewAi = await Has("implementation.ai.view");
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(string caseId, string supportHeadId, string assignedMemberId, string note)
    {
        if (!await Has("implementation.assign") || !IsActionAllowed(await GetAccessibleAsync(caseId))) return AccessDenied();
        try
        {
            await _implementationService.AssignAsync(caseId, supportHeadId, assignedMemberId, note, CurrentUserId(), CurrentName());
            TempData["Success"] = "Support member assigned. CRM, email and WhatsApp channel records were generated as configured.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSchedule(ImplementationScheduleViewModel model)
    {
        if (!await Has("implementation.schedule") || !IsActionAllowed(await GetAccessibleAsync(model.CaseId))) return AccessDenied();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id = model.CaseId });
        }
        try
        {
            await _implementationService.SaveScheduleAsync(model, CurrentUserId(), CurrentName());
            TempData["Success"] = "Training day schedule saved and relevant users notified.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = model.CaseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProgress(ImplementationProgressViewModel model)
    {
        if (!await Has("implementation.progress") || !IsActionAllowed(await GetAccessibleAsync(model.CaseId))) return AccessDenied();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id = model.CaseId });
        }
        try
        {
            await _implementationService.UpdateProgressAsync(model, CurrentUserId(), CurrentName());
            TempData["Success"] = "Day-wise covered points and progress saved.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = model.CaseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestOtp(string caseId, string purpose)
    {
        var permission = string.Equals(purpose, "Reopen", StringComparison.OrdinalIgnoreCase) ? "implementation.reopen" : "implementation.transfer";
        var item = await GetAccessibleAsync(caseId);
        if (!await Has(permission) || !IsActionAllowed(item)) return AccessDenied();
        try
        {
            await _implementationService.RequestOtpAsync(caseId, purpose, CurrentUserId(), CurrentName());
            TempData["Success"] = $"{purpose} OTP sent to active Admin and Support Head users. OTP is valid for 10 minutes.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(string caseId, string newMemberId, string reason, string otp)
    {
        if (!await Has("implementation.transfer") || !IsActionAllowed(await GetAccessibleAsync(caseId))) return AccessDenied();
        try
        {
            await _implementationService.TransferAsync(caseId, newMemberId, reason, otp, CurrentUserId(), CurrentName());
            TempData["Success"] = "Implementation responsibility transferred with OTP approval and full audit history.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(string caseId, string remarks, string otp)
    {
        if (!await Has("implementation.reopen") || !IsActionAllowed(await GetAccessibleAsync(caseId))) return AccessDenied();
        try
        {
            await _implementationService.ReopenAsync(caseId, remarks, otp, CurrentUserId(), CurrentName());
            TempData["Success"] = "Implementation / training record re-opened with OTP approval.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseWithoutTraining(string caseId, string remarks)
    {
        if (!await Has("implementation.close.notrequired") || !IsActionAllowed(await GetAccessibleAsync(caseId))) return AccessDenied();
        try
        {
            await _implementationService.CloseWithoutTrainingAsync(caseId, remarks, CurrentUserId(), CurrentName());
            TempData["Success"] = "Record closed as training not required, with remarks saved in history.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(string caseId, string reason)
    {
        if (!await Has("implementation.pause") || !IsActionAllowed(await GetAccessibleAsync(caseId))) return AccessDenied();
        try
        {
            await _implementationService.PauseAsync(caseId, reason, CurrentUserId(), CurrentName());
            TempData["Success"] = "Training paused. You can resume it anytime from this page.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(string caseId, string remarks)
    {
        if (!await Has("implementation.pause") || !IsActionAllowed(await GetAccessibleAsync(caseId))) return AccessDenied();
        try
        {
            await _implementationService.ResumeAsync(caseId, remarks, CurrentUserId(), CurrentName());
            TempData["Success"] = "Training resumed.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(ImplementationCompletionViewModel model)
    {
        if (!await Has("implementation.complete") || !IsActionAllowed(await GetAccessibleAsync(model.CaseId))) return AccessDenied();
        try
        {
            var selectedTopics = ImplementationTrainingTopicCatalog.Normalize(model.TrainingTopics);
            if (selectedTopics.Count == 0 && string.IsNullOrWhiteSpace(model.OtherPointsCovered))
                throw new InvalidOperationException("Select at least one covered training point or enter Other Points Covered before completing training.");

            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            await _implementationService.CompleteAsync(model.CaseId, selectedTopics, model.OtherPointsCovered, CurrentUserId(), CurrentName(), baseUrl);
            TempData["Success"] = "Training completed. Selected topics and other covered points were saved and included in the customer feedback email.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = model.CaseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string caseId)
    {
        if (!await Has("implementation.delete") || await GetAccessibleAsync(caseId) == null) return AccessDenied();
        await _implementationService.DeleteAsync(caseId);
        TempData["Success"] = "Onboarding record and its schedule/activity history deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CustomerByLicense(string licenseNumber)
    {
        if (!await Has("implementation.create") && !await Has("implementation.edit")) return Json(new { found = false });
        var item = await _implementationService.FindCustomerByLicenseAsync(licenseNumber);
        if (item == null) return Json(new { found = false });
        return Json(new
        {
            found = true,
            inquiryId = item.InquiryId,
            firmName = item.FirmName,
            personName = item.PersonName,
            mobile = item.Mobile,
            email = item.Email,
            city = item.City,
            productName = item.ProductName,
            licenseCount = item.LicenseCount,
            billNumber = item.BillNumber,
            saleDate = item.SaleDate.ToString("yyyy-MM-dd"),
            // ADDED (2026-08-18, per request): when adding a new training record
            // for an existing customer, the PIN Number should come through
            // automatically from their existing record instead of being
            // retyped by hand.
            pinNumber = item.PinNumber
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingReminders()
    {
        if (!await Has("implementation.reminder.view")) return Json(new { count = 0, items = Array.Empty<object>() });
        var now = DateTime.Now;
        var schedules = await _implementationService.GetUpcomingForUserAsync(CurrentRole(), CurrentUserId(), now.AddMinutes(-1), now.AddMinutes(16));
        var cases = await _implementationService.GetVisibleCasesAsync(CurrentRole(), CurrentUserId());
        var lookup = cases.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var items = schedules.Where(x => lookup.ContainsKey(x.CaseId)).Select(x => new
        {
            id = x.Id,
            caseId = x.CaseId,
            customer = lookup[x.CaseId].FirmName,
            license = lookup[x.CaseId].LicenseNumber,
            day = x.DayNumber,
            stage = x.Stage,
            time = $"{x.ScheduleDate:dd/MM/yyyy} {x.StartTime}",
            topic = x.TopicPlan,
            url = Url.Action(nameof(Details), "Implementation", new { id = x.CaseId })
        }).ToList();
        return Json(new { count = items.Count, items });
    }

    private async Task LoadViewBagsAsync()
    {
        var users = await _userService.GetActiveUsersAsync();
        ViewBag.SupportHeads = users.Where(x => x.Role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.FullName).ToList();
        ViewBag.SupportMembers = users.Where(x => x.Role.Equals("Support", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.FullName).ToList();
        ViewBag.CanCreate = await Has("implementation.create");
        ViewBag.CanAssign = await Has("implementation.assign");
        ViewBag.CanViewCustomer = await Has("field.implementation.customer");
        ViewBag.CanViewLicense = await Has("field.implementation.license");
        ViewBag.CanViewPin = await Has("field.implementation.pin");
        ViewBag.CanPaidTraining = await Has("implementation.paidtraining");
    }

    private async Task<ImplementationCase?> GetAccessibleAsync(string id)
    {
        var item = await _implementationService.GetByIdAsync(id);
        if (item == null) return null;
        if (await Has("implementation.view.all")) return item;
        var visible = await _implementationService.GetVisibleCasesAsync(CurrentRole(), CurrentUserId());
        return visible.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsActionAllowed(ImplementationCase? item)
    {
        if (item == null || item.IsTransferredHistoryView) return false;
        var role = CurrentRole();
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase) || role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase)) return true;
        if (role.Equals("Support", StringComparison.OrdinalIgnoreCase)) return item.AssignedMemberId.Equals(CurrentUserId(), StringComparison.OrdinalIgnoreCase);
        return item.CreatedByUserId.Equals(CurrentUserId(), StringComparison.OrdinalIgnoreCase) || item.SoldByUserId.Equals(CurrentUserId(), StringComparison.OrdinalIgnoreCase);
    }

    private Task<bool> Has(string key) => _permissionService.HasPermissionAsync(User, key);
    private IActionResult AccessDenied() => RedirectToAction("AccessDenied", "Account");
    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentRole() => User.FindFirstValue(ClaimTypes.Role) ?? "User";
    private string CurrentName() => User.Identity?.Name ?? "User";
}
