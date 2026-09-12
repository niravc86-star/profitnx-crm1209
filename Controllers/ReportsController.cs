using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;
using System.Security.Claims;
using System.Text;

namespace ProfitNx.CRM.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly IInquiryService _inquiryService;
    private readonly IUserService _userService;
    private readonly IDealerService _dealerService;
    private readonly IPermissionService _permissionService;
    private readonly IImplementationService _implementationService;

    public ReportsController(IInquiryService inquiryService, IUserService userService, IDealerService dealerService, IPermissionService permissionService, IImplementationService implementationService)
    {
        _inquiryService = inquiryService;
        _userService = userService;
        _dealerService = dealerService;
        _permissionService = permissionService;
        _implementationService = implementationService;
    }

    public async Task<IActionResult> Index(string? reportType, string? fromDate, string? toDate, string? status, string? product, string? version, string? partner, string? userName, string? supportUser, string? soldBy, string? customer, string? source,
        string? implFromDate, string? implToDate, string? implStatus, string? implProduct, string? implExecutive, string? trainingFilter)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        // Permission guard:
        // - reports.source and reports.training require their SPECIFIC permission (no reports.view fallback).
        // - Other report types keep existing behaviour: reports.view OR reports.<type>.
        var requestedType = string.IsNullOrWhiteSpace(reportType) ? "overview" : reportType.ToLowerInvariant();
        var permissionType = requestedType.StartsWith("ai-", StringComparison.OrdinalIgnoreCase) ? "ai" : requestedType;
        var hasGeneral = await _permissionService.HasPermissionAsync(User, "reports.view");
        var hasSpecific = await _permissionService.HasPermissionAsync(User, $"reports.{permissionType}");
        var requiresStrictSpecific = permissionType is "source" or "training";
        if (requiresStrictSpecific)
        {
            if (!hasSpecific)
            {
                return RedirectToAction("AccessDenied", "Account");
            }
        }
        else if (!hasGeneral && !hasSpecific)
        {
            return RedirectToAction("AccessDenied", "Account");
        }
        var canSeeAmount = await _permissionService.HasPermissionAsync(User, "field.reports.amount");
        var currentUser = (await _userService.GetAllUsersAsync()).FirstOrDefault(x => x.Id == userId);
        var inquiries = ApplyReportFilters(await GetVisibleInquiriesAsync(role, userId), fromDate, toDate, status, product, version, partner, userName, supportUser, soldBy, customer, source);
        var allUsers = await _userService.GetAllUsersAsync();
        var partnerIds = allUsers.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).Concat(allUsers.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.PartnerCode)).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userIds = allUsers.Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adminIds = allUsers.Where(x => x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsForwardedToPartner(Inquiry x) => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerId) && partnerIds.Contains(x.ForwardedToPartnerId);
        bool IsAssignedToUser(Inquiry x) => !string.IsNullOrWhiteSpace(x.AssignedUserId) && userIds.Contains(x.AssignedUserId);
        bool IsAssignedToAdmin(Inquiry x) => !string.IsNullOrWhiteSpace(x.AssignedUserId) && adminIds.Contains(x.AssignedUserId);
        bool IsSupportSource(Inquiry x) => x.AttendedByRole.Equals("Support", StringComparison.OrdinalIgnoreCase) || x.InquirySource.Equals("Support Team", StringComparison.OrdinalIgnoreCase);

        ViewBag.RoleName = role;
        ViewBag.ReportTitle = GetReportTitle(role, requestedType);
        ViewBag.ReportSubTitle = GetReportSubTitle(role, requestedType);
        ViewBag.CanSeeAmount = canSeeAmount;
        var targetAmount = await GetTargetForRoleAsync(role, currentUser);
        ViewBag.TargetAmount = targetAmount;
        ViewBag.PendingTarget = Math.Max(0, targetAmount - GetSalesAmount(inquiries, canSeeAmount));
        ViewBag.VisibleInquiries = inquiries;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;
        ViewBag.SelectedStatus = status;
        ViewBag.SelectedProduct = product;
        ViewBag.SelectedVersion = version;
        ViewBag.SelectedPartner = partner;
        ViewBag.SelectedUserName = userName;
        ViewBag.SelectedSupportUser = supportUser;
        ViewBag.SelectedSoldBy = soldBy;
        ViewBag.SelectedCustomer = customer;
        ViewBag.ReportType = string.IsNullOrWhiteSpace(reportType) ? "overview" : reportType;
        ViewBag.PartnerNames = inquiries.Where(IsForwardedToPartner).Select(x => x.ForwardedToPartnerName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        ViewBag.UserNames = allUsers.Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).Select(x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        ViewBag.SupportUserNames = allUsers.Where(x => x.Role.Equals("Support", StringComparison.OrdinalIgnoreCase)).Select(x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        ViewBag.Products = inquiries.Select(x => x.ProductName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        ViewBag.Versions = inquiries.Select(x => x.VersionType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        var total = inquiries.Count;
        var statusFlow = InquiryService.StatusOptions.Select(s => new StatusFlowReportRow
        {
            Status = s,
            Count = inquiries.Count(x => x.Status == s),
            Percent = total == 0 ? 0 : (int)Math.Round((decimal)inquiries.Count(x => x.Status == s) * 100 / total)
        }).ToList();

        var vm = new ReportsDashboardViewModel
        {
            TotalInquiries = total,
            SelfAttended = inquiries.Count(x => !IsForwardedToPartner(x)),
            PartnerForwarded = inquiries.Count(IsForwardedToPartner),
            FollowUps = inquiries.Count(x => x.Status == "Follow Up"),
            DemoScheduled = inquiries.Count(x => x.Status == "Demo Scheduled"),
            DemoDone = inquiries.Count(x => x.Status == "Demo Done"),
            SoldCount = inquiries.Count(x => x.Status == "Sold"),
            ClosedCount = inquiries.Count(x => x.Status == "Close"),
            TotalLicensesSold = inquiries.Where(x => x.Status == "Sold").Sum(x => x.LicensesPurchased),
            TotalSalesAmount = GetSalesAmount(inquiries, canSeeAmount),
            StatusFlow = statusFlow,
            AiInsights = BuildAiInsights(inquiries, targetAmount),
            AiPriorityLeads = BuildAiPriorityLeads(inquiries),
            AiOpportunities = BuildAiOpportunities(inquiries),
            AiProductFocus = BuildAiFocus(inquiries, x => x.ProductName ?? string.Empty),
            AiPartnerFocus = BuildAiFocus(inquiries.Where(x => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerName)).ToList(), x => x.ForwardedToPartnerName),
            ProductWise = BuildProductReport(inquiries, canSeeAmount),
            PartnerWise = BuildPerformance(inquiries.Where(IsForwardedToPartner), x => x.ForwardedToPartnerName, canSeeAmount),
            UserWise = BuildPerformance(inquiries.Where(x => IsAssignedToUser(x) && !IsForwardedToPartner(x)), x => x.AssignedUserName, canSeeAmount),
            SupportWise = BuildPerformance(inquiries.Where(IsSupportSource), x => string.IsNullOrWhiteSpace(x.SupportExecutiveName) ? "Support Team" : x.SupportExecutiveName, false),
            AdminWise = BuildPerformance(inquiries.Where(x => IsAssignedToAdmin(x) || (IsSupportSource(x) && !IsForwardedToPartner(x) && !IsAssignedToUser(x))), x => IsSupportSource(x) && !IsForwardedToPartner(x) && !IsAssignedToUser(x) ? "Admin Team" : (string.IsNullOrWhiteSpace(x.AssignedUserName) ? "Admin" : x.AssignedUserName), canSeeAmount)
        };

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            vm.PartnerWise = role.Equals("Partner", StringComparison.OrdinalIgnoreCase) ? BuildPerformance(inquiries, _ => User.Identity?.Name ?? "Partner", canSeeAmount) : new();
            vm.UserWise = role.Equals("User", StringComparison.OrdinalIgnoreCase) ? BuildPerformance(inquiries, _ => User.Identity?.Name ?? "User", canSeeAmount) : new();
            vm.SupportWise = role.Equals("Support", StringComparison.OrdinalIgnoreCase) ? BuildPerformance(inquiries, _ => User.Identity?.Name ?? "Support", false) : role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase) ? BuildPerformance(inquiries, x => string.IsNullOrWhiteSpace(x.SupportExecutiveName) ? "Unknown Support" : x.SupportExecutiveName, false) : new();
            vm.AdminWise = new();
        }

        if (requestedType == "source")
        {
            vm.SourceWise = BuildSourceReport(inquiries);
            ViewBag.SourceNames = (await GetVisibleInquiriesAsync(role, userId)).Select(x => x.InquirySource).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            ViewBag.SelectedSource = source;
        }

        if (requestedType == "training")
        {
            // Strict check already enforced at action entry for reports.training
            var allImplCases = await _implementationService.GetVisibleCasesAsync(role, userId);
            ViewBag.ImplStatusOptions = allImplCases.Select(x => x.Status).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            ViewBag.ImplProductOptions = allImplCases.Select(x => x.ProductName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            ViewBag.ImplExecutiveOptions = allImplCases.Select(x => x.AssignedMemberName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var implCases = ApplyImplementationFilters(allImplCases, implFromDate, implToDate, implStatus, implProduct, trainingFilter, implExecutive);
            vm.ImplementationCases = implCases.OrderByDescending(x => x.LastUpdated).ToList();
            vm.ImplementationSummary = BuildImplementationSummary(implCases);
            ViewBag.ImplFromDate = implFromDate;
            ViewBag.ImplToDate = implToDate;
            ViewBag.ImplStatus = implStatus;
            ViewBag.ImplProduct = implProduct;
            ViewBag.ImplExecutive = implExecutive;
            ViewBag.TrainingFilter = trainingFilter;
        }

        return View(vm);
    }

    public async Task<IActionResult> ExportExcel(string? reportType, string? fromDate, string? toDate, string? status, string? product, string? version, string? partner, string? userName, string? supportUser, string? soldBy, string? customer, string? source, string? executive)
    {
        var requestedType = string.IsNullOrWhiteSpace(reportType) ? "detailed" : reportType.ToLowerInvariant();
        var permissionType = requestedType.StartsWith("ai-", StringComparison.OrdinalIgnoreCase) ? "ai" : requestedType;
        var hasGeneral = await _permissionService.HasPermissionAsync(User, "reports.view");
        var hasSpecific = await _permissionService.HasPermissionAsync(User, $"reports.{permissionType}");
        var canExport = await _permissionService.HasPermissionAsync(User, "field.reports.export");
        var requiresStrictSpecific = permissionType is "source" or "training";
        if (requiresStrictSpecific)
        {
            if (!hasSpecific || !canExport) return RedirectToAction("AccessDenied", "Account");
        }
        else if ((!hasGeneral && !hasSpecific) || !canExport)
        {
            return RedirectToAction("AccessDenied", "Account");
        }

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var inquiries = ApplyReportFilters(await GetVisibleInquiriesAsync(role, userId), fromDate, toDate, status, product, version, partner, userName, supportUser, soldBy, customer, source);
        var canSeeAmount = await _permissionService.HasPermissionAsync(User, "field.reports.amount");
        var sb = new StringBuilder();
        var type = string.IsNullOrWhiteSpace(reportType) ? "detailed" : reportType.ToLowerInvariant();

        if (type == "source")
        {
            sb.AppendLine(canSeeAmount
                ? "Inquiry Date,Customer,Person,City,Mobile,Source,Product,Version,Status,Assigned/Forwarded To,Sold By,Licenses,Amount Without GST,Next Follow-up"
                : "Inquiry Date,Customer,Person,City,Mobile,Source,Product,Version,Status,Assigned/Forwarded To,Sold By,Licenses,Next Follow-up");
            foreach (var x in inquiries.OrderByDescending(x => x.CreatedDate))
            {
                var assignedTo = !string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) ? x.ForwardedToPartnerName : (!string.IsNullOrWhiteSpace(x.AssignedUserName) ? x.AssignedUserName : x.SupportExecutiveName);
                sb.AppendLine(canSeeAmount
                    ? $"{Csv(x.CreatedDate.ToString("dd-MM-yyyy HH:mm"))},{Csv(x.FirmName)},{Csv(x.PersonName)},{Csv(x.City)},{Csv(x.Mobile1)},{Csv(x.InquirySource)},{Csv(x.ProductName)},{Csv(x.VersionType)},{Csv(x.Status)},{Csv(assignedTo)},{Csv(x.SoldByName)},{x.LicensesPurchased},{x.AmountWithoutGst},{Csv(x.NextFollowUpDate?.ToString("dd-MM-yyyy") ?? "")}"
                    : $"{Csv(x.CreatedDate.ToString("dd-MM-yyyy HH:mm"))},{Csv(x.FirmName)},{Csv(x.PersonName)},{Csv(x.City)},{Csv(x.Mobile1)},{Csv(x.InquirySource)},{Csv(x.ProductName)},{Csv(x.VersionType)},{Csv(x.Status)},{Csv(assignedTo)},{Csv(x.SoldByName)},{x.LicensesPurchased},{Csv(x.NextFollowUpDate?.ToString("dd-MM-yyyy") ?? "")}");
            }
        }
        else if (type == "training")
        {
            var implCases = ApplyImplementationFilters(await _implementationService.GetVisibleCasesAsync(role, userId), fromDate, toDate, status, product, null, executive);
            sb.AppendLine("Sale Date,Customer,Mobile,Product,Assigned Member,Status,Stage,Training Completed,Paused,Reopened Count,Paid Training,Paid Amount,Completion Date");
            foreach (var x in implCases) sb.AppendLine($"{Csv(x.SaleDate.ToString("dd-MM-yyyy"))},{Csv(x.FirmName)},{Csv(x.Mobile)},{Csv(x.ProductName)},{Csv(x.AssignedMemberName)},{Csv(x.Status)},{Csv(x.Stage)},{(x.IsTrainingCompleted ? "Yes" : "No")},{(x.IsPaused ? "Yes" : "No")},{x.ReopenedCount},{(x.IsPaidTraining ? "Yes" : "No")},{x.PaidTrainingAmount},{Csv(x.CompletionDate?.ToString("dd-MM-yyyy") ?? "")}");
        }
        else if (type == "product")
        {
            sb.AppendLine(canSeeAmount ? "Product,Version,Sold,Licenses,Amount Without GST" : "Product,Version,Sold,Licenses");
            foreach (var r in BuildProductReport(inquiries, canSeeAmount)) sb.AppendLine(canSeeAmount ? $"{Csv(r.ProductName)},{Csv(r.VersionType)},{r.SoldCount},{r.TotalLicenses},{r.TotalAmount}" : $"{Csv(r.ProductName)},{Csv(r.VersionType)},{r.SoldCount},{r.TotalLicenses}");
        }
        else if (type == "partner" || type == "user" || type == "support" || type == "admin")
        {
            IEnumerable<Inquiry> sourceInquiries = type switch
            {
                "partner" => inquiries.Where(x => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerId)),
                "user" => inquiries.Where(x => !string.IsNullOrWhiteSpace(x.AssignedUserName) && x.AttendedByRole.Equals("User", StringComparison.OrdinalIgnoreCase)),
                "support" => inquiries.Where(x => !string.IsNullOrWhiteSpace(x.SupportExecutiveName) || x.AttendedByRole.Equals("Support", StringComparison.OrdinalIgnoreCase) || x.InquirySource.Equals("Support Team", StringComparison.OrdinalIgnoreCase)),
                "admin" => inquiries.Where(x => x.AttendedByRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) || ((x.AttendedByRole.Equals("Support", StringComparison.OrdinalIgnoreCase) || x.InquirySource.Equals("Support Team", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrWhiteSpace(x.ForwardedToPartnerId))),
                _ => inquiries
            };
            Func<Inquiry,string> key = type switch
            {
                "partner" => x => x.ForwardedToPartnerName,
                "user" => x => x.AssignedUserName,
                "support" => x => x.SupportExecutiveName,
                "admin" => x => x.AttendedByRole.Equals("Support", StringComparison.OrdinalIgnoreCase) || x.InquirySource.Equals("Support Team", StringComparison.OrdinalIgnoreCase) ? "Admin Team" : (string.IsNullOrWhiteSpace(x.AssignedUserName) ? "Admin" : x.AssignedUserName),
                _ => x => ""
            };
            var rows = BuildPerformance(sourceInquiries, key, canSeeAmount && type != "support");
            sb.AppendLine(canSeeAmount && type != "support" ? "Name,Total,Sold,Follow Ups,Demo Scheduled,Demo Done,Closed,Conversion %,Sales Amount" : "Name,Total,Sold,Follow Ups,Demo Scheduled,Demo Done,Closed,Conversion %");
            foreach (var r in rows) sb.AppendLine(canSeeAmount && type != "support" ? $"{Csv(r.Name)},{r.Total},{r.Sold},{r.FollowUps},{r.DemoScheduled},{r.DemoDone},{r.Closed},{r.ConversionPercent},{r.SalesAmount}" : $"{Csv(r.Name)},{r.Total},{r.Sold},{r.FollowUps},{r.DemoScheduled},{r.DemoDone},{r.Closed},{r.ConversionPercent}");
        }
        else
        {
            sb.AppendLine(canSeeAmount ? "Inquiry Date,Customer,Person,City,Mobile,Product,Version,Status,Sold By,Sold Role,Licenses,Amount Without GST,Amount With GST,Bill No,Bill Date,Partner,Support" : "Inquiry Date,Customer,Person,City,Mobile,Product,Version,Status,Sold By,Sold Role,Licenses,Bill No,Bill Date,Partner,Support");
            foreach (var x in inquiries)
            {
                sb.AppendLine(canSeeAmount
                    ? $"{Csv(x.CreatedDate.ToString("dd-MM-yyyy HH:mm"))},{Csv(x.FirmName)},{Csv(x.PersonName)},{Csv(x.City)},{Csv(x.Mobile1)},{Csv(x.ProductName)},{Csv(x.VersionType)},{Csv(x.Status)},{Csv(x.SoldByName)},{Csv(x.SoldByRole)},{x.LicensesPurchased},{x.AmountWithoutGst},{x.AmountWithGst},{Csv(x.BillNo)},{Csv(x.BillDate?.ToString("dd-MM-yyyy") ?? "")},{Csv(x.ForwardedToPartnerName)},{Csv(x.SupportExecutiveName)}"
                    : $"{Csv(x.CreatedDate.ToString("dd-MM-yyyy HH:mm"))},{Csv(x.FirmName)},{Csv(x.PersonName)},{Csv(x.City)},{Csv(x.Mobile1)},{Csv(x.ProductName)},{Csv(x.VersionType)},{Csv(x.Status)},{Csv(x.SoldByName)},{Csv(x.SoldByRole)},{x.LicensesPurchased},{Csv(x.BillNo)},{Csv(x.BillDate?.ToString("dd-MM-yyyy") ?? "")},{Csv(x.ForwardedToPartnerName)},{Csv(x.SupportExecutiveName)}");
            }
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"profitnx-{type}-report-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    public async Task<IActionResult> ProductDetails(string productName, string versionType, string? fromDate, string? toDate, string? status, string? partner, string? userName, string? supportUser, string? soldBy, string? customer)
    {
        var hasGeneral = await _permissionService.HasPermissionAsync(User, "reports.view");
        var hasProduct = await _permissionService.HasPermissionAsync(User, "reports.product");
        if (!hasGeneral && !hasProduct) return RedirectToAction("AccessDenied", "Account");

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var inquiries = ApplyReportFilters(await GetVisibleInquiriesAsync(role, userId), fromDate, toDate, status, productName, versionType, partner, userName, supportUser, soldBy, customer);
        var vm = new ProductPartyInfoViewModel
        {
            ProductName = productName,
            VersionType = versionType,
            Inquiries = inquiries.Where(x => x.Status == "Sold" && x.ProductName == productName && x.VersionType == versionType).OrderByDescending(x => x.SoldDate).ToList()
        };
        ViewData["Subtitle"] = "Party-wise sold details.";
        return View(vm);
    }


    private static List<Inquiry> ApplyReportFilters(List<Inquiry> inquiries, string? fromDate, string? toDate, string? status, string? product, string? version, string? partner, string? userName, string? supportUser, string? soldBy, string? customer, string? source = null)
    {
        if (DateTime.TryParse(fromDate, out var fd)) inquiries = inquiries.Where(x => x.CreatedDate.Date >= fd.Date || (x.SoldDate.HasValue && x.SoldDate.Value.Date >= fd.Date) || (x.BillDate.HasValue && x.BillDate.Value.Date >= fd.Date)).ToList();
        if (DateTime.TryParse(toDate, out var td)) inquiries = inquiries.Where(x => x.CreatedDate.Date <= td.Date || (x.SoldDate.HasValue && x.SoldDate.Value.Date <= td.Date) || (x.BillDate.HasValue && x.BillDate.Value.Date <= td.Date)).ToList();
        if (!string.IsNullOrWhiteSpace(status)) inquiries = inquiries.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(product)) inquiries = inquiries.Where(x => (x.ProductName ?? string.Empty).Equals(product, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(version)) inquiries = inquiries.Where(x => (x.VersionType ?? string.Empty).Equals(version, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(partner)) inquiries = inquiries.Where(x => (x.ForwardedToPartnerName ?? string.Empty).Equals(partner, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(userName)) inquiries = inquiries.Where(x => (x.AssignedUserName ?? string.Empty).Equals(userName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(supportUser)) inquiries = inquiries.Where(x => (x.SupportExecutiveName ?? string.Empty).Equals(supportUser, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(soldBy)) inquiries = inquiries.Where(x => (x.SoldByName ?? string.Empty).Contains(soldBy, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(customer)) inquiries = inquiries.Where(x => (x.FirmName ?? string.Empty).Contains(customer, StringComparison.OrdinalIgnoreCase) || (x.PersonName ?? string.Empty).Contains(customer, StringComparison.OrdinalIgnoreCase) || (x.Mobile1 ?? string.Empty).Contains(customer, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(source)) inquiries = inquiries.Where(x => (x.InquirySource ?? string.Empty).Equals(source, StringComparison.OrdinalIgnoreCase)).ToList();
        return inquiries;
    }

    private static List<ImplementationCase> ApplyImplementationFilters(List<ImplementationCase> cases, string? fromDate, string? toDate, string? status, string? product, string? trainingFilter, string? executive = null)
    {
        if (DateTime.TryParse(fromDate, out var fd)) cases = cases.Where(x => x.SaleDate.Date >= fd.Date || (x.CompletionDate.HasValue && x.CompletionDate.Value.Date >= fd.Date)).ToList();
        if (DateTime.TryParse(toDate, out var td)) cases = cases.Where(x => x.SaleDate.Date <= td.Date || (x.CompletionDate.HasValue && x.CompletionDate.Value.Date <= td.Date)).ToList();
        if (!string.IsNullOrWhiteSpace(status)) cases = cases.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(product)) cases = cases.Where(x => (x.ProductName ?? string.Empty).Equals(product, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(executive)) cases = cases.Where(x => (x.AssignedMemberName ?? string.Empty).Equals(executive, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(trainingFilter))
        {
            cases = trainingFilter.ToLowerInvariant() switch
            {
                "completed" => cases.Where(x => x.IsTrainingCompleted).ToList(),
                "paused" => cases.Where(x => x.IsPaused).ToList(),
                "reopened" => cases.Where(x => x.ReopenedCount > 0).ToList(),
                "paid" => cases.Where(x => x.IsPaidTraining).ToList(),
                "pending" => cases.Where(x => !x.IsTrainingCompleted && !x.IsPaused).ToList(),
                _ => cases
            };
        }
        return cases;
    }

    private async Task<decimal> GetTargetForRoleAsync(string role, AppUser? user)
    {
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase) || user == null) return 0;
        if (!role.Equals("Partner", StringComparison.OrdinalIgnoreCase)) return user.TargetAmount;
        var dealers = await _dealerService.GetAllAsync();
        var dealer = dealers.FirstOrDefault(d => d.Id.Equals(user.PartnerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.DealerName.Equals(user.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || d.ContactPerson.Equals(user.FullName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        return dealer?.GetTargetForYear(DateTime.Today.Year)?.Amount ?? 0;
    }
    private static decimal GetSalesAmount(List<Inquiry> inquiries, bool canSeeAmount) => canSeeAmount ? inquiries.Where(x => x.Status == "Sold").Sum(x => x.AmountWithoutGst) : 0;

    private static List<AiInsightItem> BuildAiInsights(List<Inquiry> inquiries, decimal targetAmount)
    {
        var total = inquiries.Count;
        var sold = inquiries.Count(x => x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase));
        var active = inquiries.Where(x => !x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)).ToList();
        var overdue = active.Count(x => x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date < DateTime.Today);
        var unattended = active.Count(x => !x.IsAttended);
        var missingAction = active.Count(x => !x.NextFollowUpDate.HasValue && !x.InquiryQuality.Equals("Not Genuine", StringComparison.OrdinalIgnoreCase));
        var conversion = total == 0 ? 0 : (int)Math.Round(sold * 100m / total);
        var sales = inquiries.Where(x => x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)).Sum(x => x.AmountWithoutGst);
        var topProduct = inquiries.Where(x => x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.ProductName))
            .GroupBy(x => x.ProductName!).OrderByDescending(x => x.Sum(y => Math.Max(1, y.LicensesPurchased))).FirstOrDefault();
        var partnerDeals = inquiries.Count(x => x.PriceType.Equals("Partner", StringComparison.OrdinalIgnoreCase));
        var insights = new List<AiInsightItem>
        {
            new() { Icon="bi-graph-up-arrow", Title="Conversion Signal", Insight=$"Current conversion is {conversion}% ({sold} sold from {total} inquiries).", RecommendedAction=conversion < 20 ? "Prioritize genuine Demo Done and Negotiation leads before creating new follow-ups." : "Keep the same follow-up discipline and review high-value pending deals.", Severity=conversion < 20 ? "warning" : "success", Score=conversion },
            new() { Icon="bi-alarm", Title="Attention Queue", Insight=$"{overdue} follow-ups are overdue and {unattended} forwarded inquiries are not yet attended.", RecommendedAction="Attend forwarded inquiries first, then clear the oldest overdue next actions.", Severity=(overdue+unattended)>0 ? "danger" : "success", Score=overdue+unattended },
            new() { Icon="bi-calendar2-check", Title="Next Action Quality", Insight=$"{missingAction} active inquiries do not have a next action date.", RecommendedAction="Add a dated next step to every active genuine inquiry.", Severity=missingAction>0 ? "warning" : "success", Score=missingAction },
            new() { Icon="bi-bullseye", Title="Target Signal", Insight=targetAmount>0 ? $"Sales achievement is {Math.Min(100, (int)Math.Round(sales*100/targetAmount))}% of the configured target." : "No sales target is configured for this login.", RecommendedAction=targetAmount>0 ? "Work the highest-value genuine inquiries until the target gap closes." : "Configure a target to make the insight measurable.", Severity=targetAmount>0 && sales<targetAmount ? "info" : "success", Score=targetAmount<=0?0:(int)Math.Min(100,Math.Round(sales*100/targetAmount)) },
            new() { Icon="bi-box-seam", Title="Product Momentum", Insight=topProduct==null ? "No sold product trend is available yet." : $"{topProduct.Key} is the current top product with {topProduct.Sum(x=>Math.Max(1,x.LicensesPurchased))} license(s).", RecommendedAction=topProduct==null ? "Complete sold records with product and license quantity." : "Use the winning product pattern in similar customer segments.", Severity="info", Score=topProduct?.Count()??0 },
            new() { Icon="bi-people", Title="Partner Pricing Audit", Insight=$"{partnerDeals} sold inquiries used partner pricing and recorded applied margin.", RecommendedAction="Review unusually high margins or deals missing a recorded price type.", Severity="info", Score=partnerDeals }
        };
        return insights;
    }

    private static List<AiPriorityLeadRow> BuildAiPriorityLeads(List<Inquiry> inquiries)
    {
        return inquiries
            .Where(x => !x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)
                && !x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)
                && !x.InquiryQuality.Equals("Not Genuine", StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                var today = DateTime.Today;
                var daysOpen = Math.Max(0, (today - x.CreatedDate.Date).Days);
                var score = 0;
                var reasons = new List<string>();
                if (!x.IsAttended) { score += 40; reasons.Add("forwarded but not attended"); }
                if (x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date < today)
                {
                    var overdueDays = Math.Max(1, (today - x.NextFollowUpDate.Value.Date).Days);
                    score += 38 + Math.Min(20, overdueDays);
                    reasons.Add($"follow-up overdue by {overdueDays} day(s)");
                }
                else if (!x.NextFollowUpDate.HasValue) { score += 28; reasons.Add("next action date missing"); }
                if (x.Status.Equals("Negotiation", StringComparison.OrdinalIgnoreCase)) { score += 24; reasons.Add("deal is in negotiation"); }
                else if (x.Status.Equals("Demo Done", StringComparison.OrdinalIgnoreCase)) { score += 20; reasons.Add("demo completed"); }
                else if (x.Status.Equals("Demo Scheduled", StringComparison.OrdinalIgnoreCase)) { score += 12; reasons.Add("demo is scheduled"); }
                if (x.InquiryQuality.Equals("Genuine", StringComparison.OrdinalIgnoreCase)) { score += 14; reasons.Add("genuine inquiry"); }
                score += Math.Min(12, daysOpen / 7);
                var priority = score >= 75 ? "Critical" : score >= 50 ? "High" : score >= 30 ? "Medium" : "Low";
                return new AiPriorityLeadRow
                {
                    InquiryId = x.Id,
                    Customer = string.IsNullOrWhiteSpace(x.FirmName) ? x.PersonName : x.FirmName,
                    Mobile = x.Mobile1,
                    Product = string.IsNullOrWhiteSpace(x.ProductName) ? "Not selected" : x.ProductName,
                    Status = x.Status,
                    Owner = ResolveOwner(x),
                    NextActionDate = x.NextFollowUpDate,
                    DaysOpen = daysOpen,
                    PriorityScore = score,
                    Priority = priority,
                    Reason = reasons.Count == 0 ? "Active inquiry requires routine follow-up" : string.Join(", ", reasons)
                };
            })
            .OrderByDescending(x => x.PriorityScore)
            .ThenBy(x => x.NextActionDate ?? DateTime.MaxValue)
            .Take(100)
            .ToList();
    }

    private static List<AiOpportunityRow> BuildAiOpportunities(List<Inquiry> inquiries)
    {
        return inquiries
            .Where(x => !x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)
                && !x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)
                && !x.InquiryQuality.Equals("Not Genuine", StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                var score = x.Status switch
                {
                    "Negotiation" => 78,
                    "Demo Done" => 66,
                    "Demo Scheduled" => 52,
                    "Follow Up" => 38,
                    "Forwarded to Partner" or "Forwarded to User" or "Forwarded to Admin" => 32,
                    _ => 22
                };
                if (x.InquiryQuality.Equals("Genuine", StringComparison.OrdinalIgnoreCase)) score += 14;
                if (!string.IsNullOrWhiteSpace(x.ProductName)) score += 5;
                if (x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date >= DateTime.Today) score += 4;
                if (x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date < DateTime.Today) score -= 8;
                score = Math.Clamp(score, 0, 100);
                var action = x.Status switch
                {
                    "Negotiation" => "Confirm commercial objections, decision maker and a dated closure commitment.",
                    "Demo Done" => "Send proposal today and schedule a decision follow-up.",
                    "Demo Scheduled" => "Confirm attendees, demo objective and success criteria before the demo.",
                    "Follow Up" => "Replace generic follow-up with one clear customer decision or next step.",
                    _ => "Validate need, product fit and book the next action date."
                };
                return new AiOpportunityRow
                {
                    InquiryId = x.Id,
                    Customer = string.IsNullOrWhiteSpace(x.FirmName) ? x.PersonName : x.FirmName,
                    Product = string.IsNullOrWhiteSpace(x.ProductName) ? "Not selected" : x.ProductName,
                    Stage = x.Status,
                    Quality = string.IsNullOrWhiteSpace(x.InquiryQuality) ? "Pending" : x.InquiryQuality,
                    Owner = ResolveOwner(x),
                    ConversionScore = score,
                    RecommendedAction = action,
                    LastUpdated = x.LastUpdated
                };
            })
            .OrderByDescending(x => x.ConversionScore)
            .ThenByDescending(x => x.LastUpdated)
            .Take(100)
            .ToList();
    }

    private static List<AiFocusRow> BuildAiFocus(List<Inquiry> inquiries, Func<Inquiry, string> keySelector)
    {
        return inquiries
            .Where(x => !string.IsNullOrWhiteSpace(keySelector(x)))
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var total = g.Count();
                var sold = g.Count(x => x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase));
                var activeRows = g.Where(x => !x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)).ToList();
                var overdue = activeRows.Count(x => x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date < DateTime.Today);
                var genuineHot = activeRows.Count(x => x.InquiryQuality.Equals("Genuine", StringComparison.OrdinalIgnoreCase) && (x.Status == "Demo Done" || x.Status == "Negotiation"));
                var conversion = total == 0 ? 0 : (int)Math.Round(sold * 100m / total);
                var score = Math.Clamp((genuineHot * 18) + (activeRows.Count * 4) + Math.Max(0, 35 - conversion) - (overdue * 3), 0, 100);
                var action = overdue > 0
                    ? $"Clear {overdue} overdue follow-up(s), then work the genuine Demo Done/Negotiation leads."
                    : genuineHot > 0
                        ? $"Prioritize {genuineHot} genuine high-intent lead(s) for closure."
                        : conversion < 20 && activeRows.Count > 0
                            ? "Review lead quality and strengthen demo-to-proposal follow-up."
                            : "Maintain the current conversion pattern and repeat the winning process.";
                return new AiFocusRow
                {
                    Name = g.Key,
                    Total = total,
                    Sold = sold,
                    Active = activeRows.Count,
                    Overdue = overdue,
                    ConversionPercent = conversion,
                    OpportunityScore = score,
                    RecommendedAction = action
                };
            })
            .OrderByDescending(x => x.OpportunityScore)
            .ThenByDescending(x => x.Active)
            .Take(30)
            .ToList();
    }

    private static string ResolveOwner(Inquiry x)
    {
        if (!string.IsNullOrWhiteSpace(x.ForwardedToPartnerName)) return x.ForwardedToPartnerName;
        if (!string.IsNullOrWhiteSpace(x.AssignedUserName)) return x.AssignedUserName;
        if (!string.IsNullOrWhiteSpace(x.SupportExecutiveName)) return x.SupportExecutiveName;
        return "Unassigned";
    }

    private static List<ProductReportRow> BuildProductReport(List<Inquiry> inquiries, bool canSeeAmount)
    {
        return inquiries.Where(x => x.Status == "Sold").GroupBy(x => new { Product = string.IsNullOrWhiteSpace(x.ProductName) ? "Unknown" : x.ProductName, Version = string.IsNullOrWhiteSpace(x.VersionType) ? "-" : x.VersionType })
            .Select(g => new ProductReportRow { ProductName = g.Key.Product, VersionType = g.Key.Version, SoldCount = g.Count(), TotalLicenses = g.Sum(x => x.LicensesPurchased), TotalAmount = canSeeAmount ? g.Sum(x => x.AmountWithoutGst) : 0 })
            .OrderByDescending(x => x.TotalLicenses).ThenBy(x => x.ProductName).ToList();
    }

    private static List<SourceReportRow> BuildSourceReport(List<Inquiry> inquiries)
    {
        return inquiries
            .GroupBy(x => string.IsNullOrWhiteSpace(x.InquirySource) ? "Not Specified" : x.InquirySource, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SourceReportRow
            {
                Source = g.Key,
                TotalGiven = g.Count(),
                Forwarded = g.Count(IsForwardedRow),
                ForwardedToSummary = BuildForwardedToSummary(g),
                Sold = g.Count(x => x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase)),
                Pending = g.Count(x => !x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase)),
                FollowUpPending = g.Count(x => x.Status.Equals("Follow Up", StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => x.TotalGiven).ThenBy(x => x.Source).ToList();
    }

    private static bool IsForwardedRow(Inquiry x) => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerId) || !string.IsNullOrWhiteSpace(x.ForwardedToUserId) || x.Status.StartsWith("Forwarded", StringComparison.OrdinalIgnoreCase);

    private static string BuildForwardedToSummary(IEnumerable<Inquiry> rows)
    {
        var names = rows.Where(IsForwardedRow)
            .Select(x => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) ? x.ForwardedToPartnerName : x.ForwardedToUserName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();
        if (names.Count == 0) return "-";
        var top = names.Take(3).Select(g => g.Count() > 1 ? $"{g.Key} ({g.Count()})" : g.Key);
        var summary = string.Join(", ", top);
        return names.Count > 3 ? $"{summary} +{names.Count - 3} more" : summary;
    }

    private static ImplementationReportSummary BuildImplementationSummary(List<ImplementationCase> cases) => new()
    {
        Total = cases.Count,
        Completed = cases.Count(x => x.IsTrainingCompleted),
        Paused = cases.Count(x => x.IsPaused),
        Reopened = cases.Count(x => x.ReopenedCount > 0),
        PaidTraining = cases.Count(x => x.IsPaidTraining),
        Pending = cases.Count(x => !x.IsTrainingCompleted && !x.IsPaused)
    };

    private static List<PerformanceReportRow> BuildPerformance(IEnumerable<Inquiry> inquiries, Func<Inquiry, string> keySelector, bool includeAmount)
    {
        return inquiries.GroupBy(keySelector).Select(g => new PerformanceReportRow
        {
            Name = string.IsNullOrWhiteSpace(g.Key) ? "Unassigned" : g.Key,
            Total = g.Count(), Sold = g.Count(x => x.Status == "Sold"), FollowUps = g.Count(x => x.Status == "Follow Up"), DemoScheduled = g.Count(x => x.Status == "Demo Scheduled"), DemoDone = g.Count(x => x.Status == "Demo Done"), Closed = g.Count(x => x.Status == "Close"), SalesAmount = includeAmount ? g.Where(x => x.Status == "Sold").Sum(x => x.AmountWithoutGst) : 0
        }).OrderByDescending(x => x.Sold).ThenByDescending(x => x.Total).ToList();
    }

    private async Task<List<Inquiry>> GetVisibleInquiriesAsync(string role, string userId)
        => await _inquiryService.GetVisibleForRoleAsync(role, userId);

    private static string GetReportTitle(string role, string reportType) => reportType switch
    {
        "ai" => "AI Business Summary",
        "ai-followup" => "AI Follow-up Priority",
        "ai-conversion" => "AI Conversion Coach",
        "ai-focus" => "AI Growth Focus",
        _ => role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase) ? "Support Team Inquiry Status" : role.Equals("Support", StringComparison.OrdinalIgnoreCase) ? "My Submitted Inquiry Status" : role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Performance Command Center" : "My Performance Report"
    };
    private static string GetReportSubTitle(string role, string reportType) => reportType switch
    {
        "ai" => "Free rule-based CRM signals generated locally from the records visible to your login.",
        "ai-followup" => "A priority queue based on overdue actions, attendance, stage, quality and inquiry age.",
        "ai-conversion" => "High-intent opportunities ranked with practical next-step recommendations.",
        "ai-focus" => "Product and partner areas where focused follow-up can improve CRM results.",
        _ => role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase) ? "Support team, status and sold-by monitoring." : role.Equals("Support", StringComparison.OrdinalIgnoreCase) ? "Status of inquiries submitted by your login." : role.Equals("Partner", StringComparison.OrdinalIgnoreCase) ? "Forwarded inquiry sales, licenses and target progress." : role.Equals("User", StringComparison.OrdinalIgnoreCase) ? "Assigned inquiry sales, licenses and target progress." : "Complete CRM insights across partner, user, support, product, stock and pipeline."
    };
    private static string Csv(string? value) { value ??= string.Empty; return "\"" + value.Replace("\"", "\"\"") + "\""; }
}
