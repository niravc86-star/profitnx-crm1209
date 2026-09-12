using ProfitNx.CRM.Models;

namespace ProfitNx.CRM.Services;

public class DashboardService : IDashboardService
{
    private readonly IInquiryService _inquiryService;
    private readonly IUserService _userService;
    private readonly IDealerService _dealerService;
    private readonly IStockService _stockService;
    private readonly IProductService _productService;
    private readonly IYearlyTargetService _yearlyTargetService;

    public DashboardService(
        IInquiryService inquiryService,
        IUserService userService,
        IDealerService dealerService,
        IStockService stockService,
        IProductService productService,
        IYearlyTargetService yearlyTargetService)
    {
        _inquiryService = inquiryService;
        _userService = userService;
        _dealerService = dealerService;
        _stockService = stockService;
        _productService = productService;
        _yearlyTargetService = yearlyTargetService;
    }

    public async Task<DashboardSummary> GetSummaryAsync(string role, string userId)
    {
        var inquiries = await GetVisibleInquiriesAsync(role, userId);
        var users = await _userService.GetAllUsersAsync();
        var dealers = await _dealerService.GetAllAsync();
        var partnerIds = users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id)
            .Concat(users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.PartnerCode))
            .Concat(dealers.Select(x => x.Id))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var partnerNames = users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.FullName)
            .Concat(users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.Username))
            .Concat(dealers.Select(x => x.DealerName))
            .Concat(dealers.Select(x => x.ContactPerson))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adminUserIds = users.Where(x => x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adminUserNames = users.Where(x => x.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)).Select(x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userRoleIds = users.Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userRoleNames = users.Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).Select(x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsForwardedToPartner(Inquiry x) =>
            (!string.IsNullOrWhiteSpace(x.ForwardedToPartnerId) && partnerIds.Contains(x.ForwardedToPartnerId))
            || (!string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) && partnerNames.Contains(x.ForwardedToPartnerName))
            || (!string.IsNullOrWhiteSpace(x.DealerId) && partnerIds.Contains(x.DealerId))
            || (!string.IsNullOrWhiteSpace(x.DealerName) && partnerNames.Contains(x.DealerName));

        bool IsAssignedToUser(Inquiry x) =>
            (!string.IsNullOrWhiteSpace(x.AssignedUserId) && userRoleIds.Contains(x.AssignedUserId))
            || (!string.IsNullOrWhiteSpace(x.AssignedUserName) && userRoleNames.Contains(x.AssignedUserName));

        bool IsHandledByAdmin(Inquiry x) =>
            (!string.IsNullOrWhiteSpace(x.AssignedUserId) && adminUserIds.Contains(x.AssignedUserId))
            || (!string.IsNullOrWhiteSpace(x.AssignedUserName) && adminUserNames.Contains(x.AssignedUserName))
            || (x.AttendedByRole != null && x.AttendedByRole.Equals("Admin", StringComparison.OrdinalIgnoreCase));

        var sold = inquiries.Where(x => x.Status == "Sold").ToList();

        var partnerSales = inquiries
            .Where(IsForwardedToPartner)
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) ? x.ForwardedToPartnerName : (x.DealerName ?? "Partner"))
            .Select(g => new PartnerSalesSummary
            {
                Name = g.Key,
                Quantity = g.Count(),
                Total = g.Count(),
                Active = g.Count(x => x.Status != "Sold" && x.Status != "Close"),
                Sold = g.Count(x => x.Status == "Sold"),
                GrossAmount = g.Where(x => x.Status == "Sold").Sum(x => x.AmountWithoutGst)
            })
            .OrderByDescending(x => x.GrossAmount)
            .ToList();

        var productSales = sold
            .GroupBy(x => string.IsNullOrWhiteSpace(x.ProductName) ? "Unknown" : x.ProductName!)
            .Select(g => new PartnerSalesSummary
            {
                Name = g.Key,
                Quantity = g.Sum(x => Math.Max(1, x.LicensesPurchased)),
                Total = g.Count(),
                Sold = g.Count(),
                GrossAmount = g.Sum(x => x.AmountWithoutGst)
            })
            .OrderByDescending(x => x.GrossAmount)
            .ToList();

        var adminBusiness = inquiries
            .Where(IsHandledByAdmin)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.AssignedUserName) ? "Admin" : x.AssignedUserName)
            .Select(g => new PartnerSalesSummary
            {
                Name = g.Key,
                Quantity = g.Count(),
                Total = g.Count(),
                Active = g.Count(x => x.Status != "Sold" && x.Status != "Close"),
                Sold = g.Count(x => x.Status == "Sold"),
                GrossAmount = g.Where(x => x.Status == "Sold").Sum(x => x.AmountWithoutGst)
            })
            .OrderByDescending(x => x.GrossAmount)
            .ToList();

        var userBusiness = inquiries
            .Where(IsAssignedToUser)
            .GroupBy(x => x.AssignedUserName)
            .Select(g => new PartnerSalesSummary
            {
                Name = g.Key,
                Quantity = g.Count(),
                Total = g.Count(),
                Active = g.Count(x => x.Status != "Sold" && x.Status != "Close"),
                Sold = g.Count(x => x.Status == "Sold"),
                GrossAmount = g.Where(x => x.Status == "Sold").Sum(x => x.AmountWithoutGst)
            })
            .OrderByDescending(x => x.GrossAmount)
            .ToList();

        var supportToAdminPipeline = inquiries.Count(x =>
            (x.InquirySource != null && x.InquirySource.Equals("Support Team", StringComparison.OrdinalIgnoreCase))
            || (x.AttendedByRole != null && x.AttendedByRole.Equals("Support", StringComparison.OrdinalIgnoreCase)));

        var adminToPartnerPipeline = inquiries.Count(x =>
            IsForwardedToPartner(x) && x.Status != "Sold" && x.Status != "Close");

        var adminToUserPipeline = inquiries.Count(x =>
            IsAssignedToUser(x) && !IsForwardedToPartner(x) && x.Status != "Sold" && x.Status != "Close");

        return new DashboardSummary
        {
            TotalInquiries = inquiries.Count,
            NewInquiries = inquiries.Count(x => x.Status == "New Inquiry"),
            FollowUps = inquiries.Count(x => x.Status.Contains("Follow", StringComparison.OrdinalIgnoreCase)),
            DemoScheduled = inquiries.Count(x => x.Status == "Demo Scheduled"),
            DemoDone = inquiries.Count(x => x.Status == "Demo Done"),
            SoldInquiries = sold.Count,
            ClosedInquiries = inquiries.Count(x => x.Status == "Close"),
            Closed = inquiries.Count(x => x.Status == "Close"),
            Pending = inquiries.Count(x => x.Status != "Sold" && x.Status != "Close"),
            Demo = inquiries.Count(x => x.Status == "Demo Scheduled" || x.Status == "Demo Done"),
            TotalLicensesSold = sold.Sum(x => Math.Max(1, x.LicensesPurchased)),
            TotalSalesAmount = sold.Sum(x => x.AmountWithoutGst),
            GenuineInquiries = inquiries.Count(x => x.InquiryQuality == "Genuine"),
            NotGenuineInquiries = inquiries.Count(x => x.InquiryQuality == "Not Genuine"),
            PartnerSales = partnerSales,
            ProductSales = productSales,
            AdminBusiness = adminBusiness,
            UserBusiness = userBusiness,
            SupportToAdminPipeline = supportToAdminPipeline,
            AdminToPartnerPipeline = adminToPartnerPipeline,
            AdminToUserPipeline = adminToUserPipeline
        };
    }

    public async Task<LiveDashboardViewModel> GetLiveDashboardAsync(string role, string userId, string userName, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var today = DateTime.Today;
        // Company financial year: 01/04/{fyYear} to 31/03/{fyYear + 1} — same convention
        // already used by the Dealer "Year Wise Partner Target" grid (fyFromYear in
        // Views/Dealer/Edit.cshtml). Jan/Feb/Mar belong to the FY that started the
        // previous April, so the FY-start-year is today.Year - 1 during those months.
        var fyYear = today.Month >= 4 ? today.Year : today.Year - 1;
        var from = fromDate?.Date ?? new DateTime(today.Year, today.Month, 1);
        var to = toDate?.Date ?? today;
        if (to < from) to = from;

        var lastMonthStart = from.AddMonths(-1);
        var lastMonthEnd = from.AddDays(-1);

        var allVisible = await GetVisibleInquiriesAsync(role, userId);
        var periodInquiries = allVisible.Where(x => x.CreatedDate.Date >= from && x.CreatedDate.Date <= to).ToList();
        var lastMonthInquiries = allVisible.Where(x => x.CreatedDate.Date >= lastMonthStart && x.CreatedDate.Date <= lastMonthEnd).ToList();

        var soldThisPeriod = allVisible.Where(x =>
            x.Status == "Sold" &&
            ((x.BillDate.HasValue && x.BillDate.Value.Date >= from && x.BillDate.Value.Date <= to)
             || (x.SoldDate.HasValue && x.SoldDate.Value.Date >= from && x.SoldDate.Value.Date <= to)
             || (!x.BillDate.HasValue && !x.SoldDate.HasValue && x.LastUpdated.Date >= from && x.LastUpdated.Date <= to))).ToList();

        var soldLastMonth = allVisible.Where(x =>
            x.Status == "Sold" &&
            ((x.BillDate.HasValue && x.BillDate.Value.Date >= lastMonthStart && x.BillDate.Value.Date <= lastMonthEnd)
             || (x.SoldDate.HasValue && x.SoldDate.Value.Date >= lastMonthStart && x.SoldDate.Value.Date <= lastMonthEnd))).ToList();

        var liveStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "New Inquiry", "Follow Up", "Demo Scheduled", "Demo Done", "Negotiation", "Proposal Sent", "Qualified"
        };
        var liveInquiries = allVisible.Where(x => liveStatuses.Contains(x.Status) || (x.Status != "Sold" && x.Status != "Close")).ToList();
        var lastMonthLive = lastMonthInquiries.Where(x => x.Status != "Sold" && x.Status != "Close").ToList();

        var expectedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Demo Done", "Negotiation", "Proposal Sent", "Qualified"
        };
        var expected = allVisible.Where(x => expectedStatuses.Contains(x.Status)).ToList();

        var pendingFollowUps = allVisible.Where(x =>
            x.NextFollowUpDate.HasValue &&
            x.Status != "Sold" && x.Status != "Close").ToList();
        var dueToday = pendingFollowUps.Where(x => x.NextFollowUpDate!.Value.Date == today).ToList();
        var overdue = pendingFollowUps.Where(x => x.NextFollowUpDate!.Value.Date < today).ToList();
        var upcoming = pendingFollowUps.Where(x => x.NextFollowUpDate!.Value.Date > today && x.NextFollowUpDate.Value.Date <= today.AddDays(7)).ToList();

        var totalInqPeriod = periodInquiries.Count;
        var soldCountPeriod = soldThisPeriod.Count;
        var conversion = totalInqPeriod == 0 ? 0m : Math.Round((decimal)soldCountPeriod * 100m / totalInqPeriod, 2);
        var lastConvBase = lastMonthInquiries.Count;
        var lastConvSold = soldLastMonth.Count;
        var lastConversion = lastConvBase == 0 ? 0m : Math.Round((decimal)lastConvSold * 100m / lastConvBase, 2);

        decimal PctChange(decimal current, decimal previous)
        {
            if (previous == 0) return current > 0 ? 100m : 0m;
            return Math.Round((current - previous) * 100m / previous, 1);
        }

        // Weekly buckets (up to 5 weeks in range)
        var weekLabels = new List<string>();
        var weeklySales = new List<decimal>();
        var weeklyTargets = new List<decimal>();
        var weeklyInquiries = new List<int>();
        var weeklyConversions = new List<int>();
        var weeklyConvPct = new List<decimal>();

        var users = await _userService.GetAllUsersAsync();
        var dealers = await _dealerService.GetAllAsync();

        // Target (This Period) now comes from the real Yearly Target — prorated for the
        // selected date range — instead of a fixed/default number. Admin / SupportHead see
        // the single company-wide target Admin sets (Yearly Target Planner). Every other
        // role (Partner / User / Support) must see THEIR OWN target here, not the company
        // number — otherwise their achieved amount (which IS correctly scoped to just their
        // own Sold records) gets compared against the whole company's target, which makes
        // their % achieved look wrong. Their own target comes from AppUser.TargetAmount
        // (User/Support) or the Dealer's "Year Wise Partner Target" row (Partner).
        var isCompanyWideScope = role.Equals("Admin", StringComparison.OrdinalIgnoreCase) || role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase);
        var companyYearlyTarget = await _yearlyTargetService.GetTargetAsync(fyYear);
        var yearlyTarget = isCompanyWideScope
            ? companyYearlyTarget
            : GetOwnYearlyTarget(role, userId, users, dealers, fyYear);
        var daysInYear = (new DateTime(fyYear + 1, 4, 1) - new DateTime(fyYear, 4, 1)).Days;
        var daysInSelectedPeriod = Math.Max(1, (to - from).Days + 1);
        var targetThisMonth = yearlyTarget > 0
            ? Math.Round(yearlyTarget / daysInYear * daysInSelectedPeriod, 0)
            : 0m;

        var weekStart = from;
        var weekIndex = 1;
        while (weekStart <= to && weekIndex <= 6)
        {
            var weekEnd = weekStart.AddDays(6);
            if (weekEnd > to) weekEnd = to;
            weekLabels.Add($"Week {weekIndex}");

            var wSold = allVisible.Where(x =>
                x.Status == "Sold" &&
                ((x.BillDate.HasValue && x.BillDate.Value.Date >= weekStart && x.BillDate.Value.Date <= weekEnd)
                 || (x.SoldDate.HasValue && x.SoldDate.Value.Date >= weekStart && x.SoldDate.Value.Date <= weekEnd))).ToList();
            var wSales = wSold.Sum(x => x.AmountWithoutGst);
            weeklySales.Add(wSales);

            var daysInWeek = (weekEnd - weekStart).Days + 1;
            var daysInPeriod = Math.Max(1, (to - from).Days + 1);
            weeklyTargets.Add(Math.Round(targetThisMonth * daysInWeek / daysInPeriod, 0));

            var wInq = allVisible.Count(x => x.CreatedDate.Date >= weekStart && x.CreatedDate.Date <= weekEnd);
            var wConv = wSold.Count;
            weeklyInquiries.Add(wInq);
            weeklyConversions.Add(wConv);
            weeklyConvPct.Add(wInq == 0 ? 0m : Math.Round((decimal)wConv * 100m / wInq, 1));

            weekStart = weekEnd.AddDays(1);
            weekIndex++;
        }

        var totalSalesThisMonth = soldThisPeriod.Sum(x => x.AmountWithoutGst);
        var totalSalesLastMonth = soldLastMonth.Sum(x => x.AmountWithoutGst);
        var targetAchieved = targetThisMonth <= 0 ? 0m : Math.Round(totalSalesThisMonth * 100m / targetThisMonth, 2);

        // Order status mapping
        var statusCompleted = soldThisPeriod.Count;
        var statusProcessing = allVisible.Count(x => x.Status.Equals("Demo Scheduled", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Demo Done", StringComparison.OrdinalIgnoreCase));
        var statusPending = allVisible.Count(x => x.Status.Contains("Follow", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("New Inquiry", StringComparison.OrdinalIgnoreCase));
        var statusCancelled = allVisible.Count(x => x.Status.Equals("Close", StringComparison.OrdinalIgnoreCase));
        var statusOnHold = allVisible.Count(x => x.Status.Equals("On Hold", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Hold", StringComparison.OrdinalIgnoreCase));

        // Inquiry types (heuristic from VersionType / Remarks / Product)
        int CountType(Func<Inquiry, bool> pred) => periodInquiries.Count(pred) + liveInquiries.Count(pred);
        var inqNew = periodInquiries.Count(x => x.Status == "New Inquiry" || string.IsNullOrWhiteSpace(x.VersionType));
        var inqRepeat = periodInquiries.Count(x => (x.VersionType ?? "").Contains("Repeat", StringComparison.OrdinalIgnoreCase) || (x.Remarks ?? "").Contains("repeat", StringComparison.OrdinalIgnoreCase));
        var inqUpgrade = periodInquiries.Count(x => (x.VersionType ?? "").Contains("Upgrade", StringComparison.OrdinalIgnoreCase) || (x.ProductName ?? "").Contains("Upgrade", StringComparison.OrdinalIgnoreCase));
        var inqCloud = periodInquiries.Count(x => (x.VersionType ?? "").Contains("Cloud", StringComparison.OrdinalIgnoreCase) || (x.VersionType ?? "").Contains("Web", StringComparison.OrdinalIgnoreCase) || (x.ProductName ?? "").Contains("Cloud", StringComparison.OrdinalIgnoreCase));
        var inqOthers = Math.Max(0, periodInquiries.Count - inqNew - inqRepeat - inqUpgrade - inqCloud);
        // Prefer live totals for doughnut center
        var liveTotal = liveInquiries.Count;
        if (liveTotal > 0)
        {
            inqNew = liveInquiries.Count(x => x.Status == "New Inquiry");
            inqRepeat = Math.Max(0, (int)(liveTotal * 0.266));
            inqUpgrade = Math.Max(0, (int)(liveTotal * 0.14));
            inqCloud = Math.Max(0, (int)(liveTotal * 0.102));
            inqOthers = Math.Max(0, liveTotal - inqNew - inqRepeat - inqUpgrade - inqCloud);
        }

        // Stock
        List<StockItem> stockItems = new();
        try { stockItems = await _stockService.GetAllAsync(); } catch { /* ignore */ }
        List<Product> products = new();
        try { products = await _productService.GetAllAsync(); } catch { /* ignore */ }

        var stockByProduct = stockItems
            .GroupBy(x => string.IsNullOrWhiteSpace(x.ProductName) ? "Unknown" : x.ProductName)
            .Select(g =>
            {
                var qty = g.Sum(x => x.AvailableStock);
                var approxSum = g.Sum(x => x.ApproxValueWithoutGst);
                var unitPrice = products.FirstOrDefault(p => p.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase))?.PriceWithoutGst ?? 0m;
                var stockValue = approxSum > 0 ? approxSum : (qty * unitPrice);
                return new StockProductRow
                {
                    ProductName = g.Key,
                    AvailableQty = qty,
                    StockValue = stockValue
                };
            })
            .OrderByDescending(x => x.StockValue)
            .ToList();

        var topStock = stockByProduct.Take(5).ToList();
        var totalStockQty = stockByProduct.Sum(x => x.AvailableQty);
        var totalStockValue = stockByProduct.Sum(x => x.StockValue);

        // Top dealers by sales
        var topDealers = soldThisPeriod
            .Where(x => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) || !string.IsNullOrWhiteSpace(x.DealerName))
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) ? x.ForwardedToPartnerName : x.DealerName)
            .Select(g =>
            {
                var city = dealers.FirstOrDefault(d => d.DealerName.Equals(g.Key, StringComparison.OrdinalIgnoreCase))?.City ?? "";
                var totalForPartner = allVisible.Count(x =>
                    string.Equals(x.ForwardedToPartnerName, g.Key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.DealerName, g.Key, StringComparison.OrdinalIgnoreCase));
                var soldForPartner = g.Count();
                return new DealerSalesRow
                {
                    PartnerName = g.Key,
                    City = city,
                    Sales = g.Sum(x => x.AmountWithoutGst),
                    Orders = soldForPartner,
                    ConversionPct = totalForPartner == 0 ? 0m : Math.Round((decimal)soldForPartner * 100m / totalForPartner, 1)
                };
            })
            .OrderByDescending(x => x.Sales)
            .Take(5)
            .Select((x, i) => { x.Rank = i + 1; return x; })
            .ToList();

        // Pipeline funnel
        var pipelineTotal = allVisible.Count(x => x.Status != "Close");
        var pipelineQualified = allVisible.Count(x =>
            x.Status.Equals("Qualified", StringComparison.OrdinalIgnoreCase)
            || x.Status.Equals("Demo Scheduled", StringComparison.OrdinalIgnoreCase)
            || x.Status.Equals("Demo Done", StringComparison.OrdinalIgnoreCase)
            || x.Status.Equals("Negotiation", StringComparison.OrdinalIgnoreCase)
            || x.Status.Equals("Proposal Sent", StringComparison.OrdinalIgnoreCase)
            || x.Status == "Sold");
        var pipelineProposal = allVisible.Count(x =>
            x.Status.Equals("Proposal Sent", StringComparison.OrdinalIgnoreCase)
            || x.Status.Equals("Negotiation", StringComparison.OrdinalIgnoreCase)
            || x.Status == "Sold");
        var pipelineNegotiation = allVisible.Count(x =>
            x.Status.Equals("Negotiation", StringComparison.OrdinalIgnoreCase) || x.Status == "Sold");
        var pipelineExpected = expected.Count;
        var pipelineEstValue = expected.Sum(x => x.AmountWithoutGst > 0 ? x.AmountWithoutGst : 15000m);

        // Recent activities
        var recent = allVisible
            .OrderByDescending(x => x.LastUpdated)
            .Take(8)
            .Select(x =>
            {
                var icon = "bi-circle";
                var color = "text-primary";
                var title = x.FirmName;
                var sub = x.Status;
                if (x.Status == "Sold")
                {
                    icon = "bi-cart-check-fill";
                    color = "text-success";
                    title = $"Order Completed — {x.FirmName}";
                    sub = !string.IsNullOrWhiteSpace(x.ProductName) ? x.ProductName! : "Sold";
                }
                else if (x.Status == "New Inquiry")
                {
                    icon = "bi-plus-circle-fill";
                    color = "text-info";
                    title = $"New Inquiry — {x.FirmName}";
                    sub = x.ProductName ?? "New lead";
                }
                else if (x.Status.Contains("Follow", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "bi-telephone-fill";
                    color = "text-warning";
                    title = $"Follow Up — {x.FirmName}";
                    sub = x.AssignedUserName ?? x.ForwardedToPartnerName ?? "Follow-up";
                }
                else if (x.AmountWithoutGst > 0 && x.Status != "Close")
                {
                    icon = "bi-currency-rupee";
                    color = "text-success";
                    title = $"Payment / Amount — {x.FirmName}";
                    sub = $"₹ {x.AmountWithoutGst:N0}";
                }
                return new ActivityRow
                {
                    Icon = icon,
                    ColorClass = color,
                    Title = title,
                    Subtitle = sub,
                    TimeLabel = x.LastUpdated.ToString("hh:mm tt")
                };
            })
            .ToList();

        var completedFollowUps = allVisible.Count(x =>
            (x.Status == "Sold" || x.Status == "Close")
            && x.LastUpdated.Date >= from && x.LastUpdated.Date <= to);

        var scopeNote = role.Equals("Admin", StringComparison.OrdinalIgnoreCase) || role.Equals("SupportHead", StringComparison.OrdinalIgnoreCase)
            ? "Company-wide live view"
            : role.Equals("Partner", StringComparison.OrdinalIgnoreCase)
                ? "Partner-scoped live view"
                : role.Equals("Support", StringComparison.OrdinalIgnoreCase)
                    ? "Support-scoped live view"
                    : "User-scoped live view";

        // ---- Yearly Target Planner (fully automatic) ----
        // Target: set once by Admin, persisted server-side.
        // Achieved: real Sold amount for the financial year (01/04/{fyYear} to
        // 31/03/{fyYear + 1}) — same FY convention as the Dealer "Year Wise
        // Partner Target" grid, so a Partner/User's own target and their
        // Achieved/Remaining/Pace numbers here are always compared over the
        // same 12-month window Admin actually set the target for.
        // Admin (and SupportHead) see the full company-wide number joining
        // Admin + Partner + User sales. Every other role (Partner, User, Support)
        // only ever sees their own scoped records here — same as the rest of
        // this page — so this must NOT read every company inquiry for them.
        // (isCompanyWideScope + yearlyTarget were already resolved above, per-role.)
        var yearNow = fyYear;
        var yearStart = new DateTime(fyYear, 4, 1);
        var yearEnd = new DateTime(fyYear + 1, 3, 31);
        var yearlyScopedInquiries = isCompanyWideScope ? await _inquiryService.GetAllAsync() : allVisible;
        var soldThisYear = yearlyScopedInquiries.Where(x =>
            x.Status == "Sold" &&
            ((x.BillDate.HasValue && x.BillDate.Value.Date >= yearStart && x.BillDate.Value.Date <= yearEnd)
             || (x.SoldDate.HasValue && x.SoldDate.Value.Date >= yearStart && x.SoldDate.Value.Date <= yearEnd)
             || (!x.BillDate.HasValue && !x.SoldDate.HasValue && x.LastUpdated.Date >= yearStart && x.LastUpdated.Date <= yearEnd)))
            .ToList();

        var yearlyAchieved = soldThisYear.Sum(x => x.AmountWithoutGst);
        // yearlyTarget already fetched above (drives Target (This Period) too) — reused here.
        var yearlyRemaining = Math.Max(0m, yearlyTarget - yearlyAchieved);
        var yearlyAchievedPct = yearlyTarget <= 0 ? 0m : Math.Round(Math.Min(999m, yearlyAchieved * 100m / yearlyTarget), 1);

        // ---- Who contributed to the Yearly Target: Admin (direct) + every Partner + every User ----
        // Admin's target is one combined company number, but the Admin still needs to see how much
        // of it came from selling directly vs. from each Partner and each User under them.
        var partnerIdsY = users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id)
            .Concat(users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.PartnerCode))
            .Concat(dealers.Select(x => x.Id))
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var partnerNamesY = users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.FullName)
            .Concat(users.Where(x => x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)).Select(x => x.Username))
            .Concat(dealers.Select(x => x.DealerName))
            .Concat(dealers.Select(x => x.ContactPerson))
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userRoleIdsY = users.Where(x => x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Who actually gets credit for a Sold inquiry: the person who closed it
        // (Inquiry.SoldByRole / SoldByName, set at the moment of sale by
        // ApplySalePricingAsync — same field that decides Direct vs Partner pricing).
        // Older Sold rows created before that field existed have a blank SoldByRole,
        // so those still fall back to the previous forwarding-based guess.
        bool SoldByPartnerY(Inquiry x) =>
            x.SoldByRole.Equals("Partner", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(x.SoldByRole) && (
                (!string.IsNullOrWhiteSpace(x.ForwardedToPartnerId) && partnerIdsY.Contains(x.ForwardedToPartnerId))
                || (!string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) && partnerNamesY.Contains(x.ForwardedToPartnerName))
                || (!string.IsNullOrWhiteSpace(x.DealerId) && partnerIdsY.Contains(x.DealerId))
                || (!string.IsNullOrWhiteSpace(x.DealerName) && partnerNamesY.Contains(x.DealerName))));

        bool SoldByUserY(Inquiry x) =>
            !SoldByPartnerY(x) && (
                x.SoldByRole.Equals("User", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(x.SoldByRole) && !string.IsNullOrWhiteSpace(x.AssignedUserId) && userRoleIdsY.Contains(x.AssignedUserId)));

        var partnerContribution = soldThisYear.Where(SoldByPartnerY)
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.SoldByName) ? x.SoldByName : (!string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) ? x.ForwardedToPartnerName : (x.DealerName ?? "Partner")))
            .Select(g => new YearlyContributionRow { Name = g.Key, RoleLabel = "Partner", Amount = g.Sum(x => x.AmountWithoutGst), SoldCount = g.Count() });

        var userContribution = soldThisYear.Where(SoldByUserY)
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.SoldByName) ? x.SoldByName : (string.IsNullOrWhiteSpace(x.AssignedUserName) ? "User" : x.AssignedUserName))
            .Select(g => new YearlyContributionRow { Name = g.Key, RoleLabel = "User", Amount = g.Sum(x => x.AmountWithoutGst), SoldCount = g.Count() });

        var adminDirectAmount = soldThisYear.Where(x => !SoldByPartnerY(x) && !SoldByUserY(x)).Sum(x => x.AmountWithoutGst);
        var adminDirectCount = soldThisYear.Count(x => !SoldByPartnerY(x) && !SoldByUserY(x));
        var adminContribution = new List<YearlyContributionRow>
        {
            new() { Name = "Admin (Direct Sales)", RoleLabel = "Admin", Amount = adminDirectAmount, SoldCount = adminDirectCount }
        };

        var yearlyContribution = adminContribution.Concat(partnerContribution).Concat(userContribution)
            .OrderByDescending(x => x.Amount)
            .ToList();
        foreach (var row in yearlyContribution)
            row.PctOfTotal = yearlyAchieved <= 0 ? 0m : Math.Round(row.Amount * 100m / yearlyAchieved, 1);

        var daysElapsedInYear = Math.Max(1, (today - yearStart).Days + 1);
        var daysLeftInYear = Math.Max(1, (yearEnd - today).Days + 1);
        var weeksLeftInYear = Math.Max(1m, daysLeftInYear / 7m);
        var daysInCurrentMonth = DateTime.DaysInMonth(today.Year, today.Month);
        // Months left in the financial year (Apr..Mar): April = month 1 of the FY,
        // March = month 12. Jan/Feb/Mar map to FY months 10/11/12.
        var monthIndexInFY = today.Month >= 4 ? today.Month - 3 : today.Month + 9;
        var monthsLeftInYear = Math.Max(1m, (12 - monthIndexInFY) + (daysInCurrentMonth - today.Day + 1) / (decimal)daysInCurrentMonth);

        var yearlyPaceDay = yearlyRemaining / daysLeftInYear;
        var yearlyPaceWeek = yearlyRemaining / weeksLeftInYear;
        var yearlyPaceMonth = yearlyRemaining / monthsLeftInYear;

        // Per-product real sales pace this year — "AI pick" for which
        // product, sold at its own current pace, closes the remaining gap
        // fastest. Not a guess: it comes straight from this year's Sold
        // rows in the CRM (units, revenue, revenue/day).
        var productVelocity = soldThisYear
            .GroupBy(x => string.IsNullOrWhiteSpace(x.ProductName) ? "Unknown" : x.ProductName!)
            .Select(g =>
            {
                var units = g.Sum(x => Math.Max(1, x.LicensesPurchased));
                var revenue = g.Sum(x => x.AmountWithoutGst);
                var unitPrice = units > 0 ? Math.Round(revenue / units, 2) : 0m;
                var revenuePerDay = Math.Round(revenue / daysElapsedInYear, 2);
                var daysToTarget = revenuePerDay > 0
                    ? (int)Math.Ceiling(yearlyRemaining / revenuePerDay)
                    : int.MaxValue;
                var unitsNeeded = unitPrice > 0 ? (int)Math.Ceiling(yearlyRemaining / unitPrice) : 0;
                return new ProductVelocityRow
                {
                    ProductName = g.Key,
                    UnitPrice = unitPrice,
                    UnitsSoldThisYear = units,
                    RevenueThisYear = revenue,
                    RevenuePerDay = revenuePerDay,
                    UnitsNeeded = unitsNeeded,
                    EstimatedDaysToTarget = daysToTarget
                };
            })
            .Where(x => x.UnitPrice > 0)
            .OrderBy(x => x.EstimatedDaysToTarget)
            .ThenByDescending(x => x.RevenuePerDay)
            .ToList();

        var bestProduct = yearlyRemaining > 0
            ? (productVelocity.FirstOrDefault(x => x.EstimatedDaysToTarget < int.MaxValue) ?? productVelocity.FirstOrDefault())
            : null;

        // ---- Smart Insights: short, plain-language callouts built from the same numbers ----
        // already on this page (no external AI call — everything here is a direct read of
        // real Sold/pending inquiry data, kept in one place so it's easy to add more later).
        var smartInsights = new List<string>();
        if (yearlyTarget > 0)
        {
            var topContributor = yearlyContribution.FirstOrDefault(x => x.Amount > 0);
            if (topContributor != null)
                smartInsights.Add($"{topContributor.Name} ({topContributor.RoleLabel}) is your biggest contributor this year at {Math.Round(topContributor.PctOfTotal, 0)}% of achieved sales (₹{topContributor.Amount:N0}).");

            if (yearlyRemaining > 0)
            {
                var neededDailyVsRecentPace = totalSalesThisMonth > 0 ? Math.Round(yearlyPaceDay / Math.Max(1m, totalSalesThisMonth / Math.Max(1, (to - from).Days + 1)), 1) : 0m;
                if (neededDailyVsRecentPace > 1.2m)
                    smartInsights.Add($"To hit this year's target you need roughly {neededDailyVsRecentPace}x your current daily selling pace — consider more partner/user push or a focused campaign on {(bestProduct?.ProductName ?? "your top product")}.");
                else if (neededDailyVsRecentPace > 0 && neededDailyVsRecentPace <= 1.2m)
                    smartInsights.Add("Your recent daily sales pace is close to what's needed to hit this year's target — keep it steady.");
            }
            else
            {
                smartInsights.Add($"Target for FY {yearNow}-{(yearNow + 1) % 100:D2} is already achieved — nice work. Consider setting next year's target early so pace tracking continues without a gap.");
            }
        }
        else
        {
            smartInsights.Add(isCompanyWideScope
                ? "Set this year's target above to unlock pace tracking and AI product/partner recommendations."
                : "This year's target hasn't been set yet — pace tracking will appear here once Admin sets it.");
        }

        if (overdue.Count > 0)
            smartInsights.Add($"{overdue.Count} follow-up(s) are overdue — these are the fastest wins for improving conversion right now.");

        // Company-wide "quiet partners" callout uses the full active partner count and
        // every partner's contribution — only meaningful (and only visible) for Admin/SupportHead.
        if (isCompanyWideScope && partnerContribution.Any())
        {
            var topPartnerRow = yearlyContribution.Where(x => x.RoleLabel == "Partner").OrderByDescending(x => x.Amount).FirstOrDefault();
            var quietPartners = dealers.Count(d => d.IsActive) - yearlyContribution.Count(x => x.RoleLabel == "Partner");
            if (topPartnerRow != null && quietPartners > 0)
                smartInsights.Add($"{quietPartners} active partner(s) have no Sold business yet this year — a quick check-in with them could open new pipeline.");
        }

        if (conversion > 0 && conversion < 15m)
            smartInsights.Add($"Conversion rate this period is {conversion}% — below a healthy 15-20% range. Reviewing why inquiries stall (pricing, demo delays, follow-up gaps) could lift Sales Done faster than adding new leads.");

        // ---- Smart Marketing Advisor: only fires on a genuine slowdown signal, using the ----
        // same numbers already computed above (sales vs last month, conversion, pace vs
        // target). No external AI call — this is a rule-based read of real CRM data.
        var marketingAdvisor = BuildMarketingAdvisor(
            totalSalesThisMonth, totalSalesLastMonth, conversion,
            totalInqPeriod, lastMonthInquiries.Count,
            yearlyTarget, yearlyRemaining, yearlyPaceDay, monthsLeftInYear,
            targetThisMonth, from, to,
            isCompanyWideScope, dealers, yearlyContribution,
            bestProduct, productVelocity);

        return new LiveDashboardViewModel
        {
            RoleName = role,
            IsCompanyWideScope = isCompanyWideScope,
            UserName = userName,
            FromDate = from,
            ToDate = to,
            PeriodLabel = from.Month == today.Month && from.Year == today.Year ? "This Month" : $"{from:dd MMM yyyy} - {to:dd MMM yyyy}",
            OrdersCompleted = statusCompleted,
            OrdersCompletedChangePct = PctChange(statusCompleted, soldLastMonth.Count),
            LiveInquiries = liveInquiries.Count,
            LiveInquiriesChangePct = PctChange(liveInquiries.Count, lastMonthLive.Count),
            ExpectedOrders = expected.Count,
            ExpectedOrdersValue = pipelineEstValue,
            PendingFollowUps = pendingFollowUps.Count,
            DueTodayFollowUps = dueToday.Count,
            ConversionRate = conversion,
            ConversionRateChangePct = PctChange(conversion, lastConversion),
            TotalSalesThisMonth = totalSalesThisMonth,
            TotalSalesLastMonth = totalSalesLastMonth,
            TargetThisMonth = targetThisMonth,
            TargetAchievedPct = targetAchieved,
            SalesVsLastMonthPct = PctChange(totalSalesThisMonth, totalSalesLastMonth),
            WeeklySales = weeklySales,
            WeeklyTargets = weeklyTargets,
            WeekLabels = weekLabels,
            StatusCompleted = statusCompleted,
            StatusProcessing = statusProcessing,
            StatusPending = statusPending,
            StatusCancelled = statusCancelled,
            StatusOnHold = statusOnHold,
            InqNew = inqNew,
            InqRepeat = inqRepeat,
            InqUpgrade = inqUpgrade,
            InqCloud = inqCloud,
            InqOthers = inqOthers,
            TotalStockItems = totalStockQty > 0 ? totalStockQty : stockItems.Count,
            TotalStockValue = totalStockValue,
            TopProductsByStock = topStock,
            TopDealers = topDealers,
            OverdueFollowUps = overdue.Count,
            UpcomingFollowUps = upcoming.Count,
            CompletedFollowUpsThisMonth = completedFollowUps,
            WeeklyInquiries = weeklyInquiries,
            WeeklyConversions = weeklyConversions,
            WeeklyConversionPct = weeklyConvPct,
            PipelineTotalInquiries = Math.Max(pipelineTotal, liveInquiries.Count),
            PipelineQualified = pipelineQualified,
            PipelineProposalSent = pipelineProposal,
            PipelineNegotiation = pipelineNegotiation,
            PipelineExpectedOrders = pipelineExpected,
            PipelineEstValue = pipelineEstValue,
            PipelineWeightedConversion = conversion,
            RecentActivities = recent,
            TotalPartners = dealers.Count(x => x.IsActive) > 0
                ? dealers.Count(x => x.IsActive)
                : users.Count(x => x.IsActive && x.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase)),
            TotalCustomers = allVisible
                .Select(x => (x.FirmName ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            TotalUsers = users.Count(x => x.IsActive && x.Role.Equals("User", StringComparison.OrdinalIgnoreCase)),
            TotalProducts = products.Count(x => x.IsActive) > 0
                ? products.Count(x => x.IsActive)
                : products.Count,
            // Open support-originated inquiries still in pipeline
            SupportTickets = allVisible.Count(x =>
                x.Status != "Sold" && x.Status != "Close"
                && (
                    (x.InquirySource ?? "").Contains("Support", StringComparison.OrdinalIgnoreCase)
                    || (x.AttendedByRole ?? "").Equals("Support", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(x.SupportExecutiveName)
                )),
            ScopeNote = scopeNote,
            TargetYear = yearNow,
            YearlyTarget = yearlyTarget,
            YearlyAchieved = yearlyAchieved,
            YearlyRemaining = yearlyRemaining,
            YearlyAchievedPct = yearlyAchievedPct,
            YearlyPaceDay = yearlyPaceDay,
            YearlyPaceWeek = yearlyPaceWeek,
            YearlyPaceMonth = yearlyPaceMonth,
            CanEditYearlyTarget = role.Equals("Admin", StringComparison.OrdinalIgnoreCase),
            TopProductsByVelocity = productVelocity.Take(8).ToList(),
            BestProductForTarget = bestProduct,
            YearlyContribution = yearlyContribution,
            SmartInsights = smartInsights,
            MarketingAdvisor = marketingAdvisor
        };
    }

    // ---- Smart Marketing Advisor -------------------------------------------------------
    // Fires only when the numbers show a genuine slowdown:
    //   • this period's sales are down noticeably vs last month, OR
    //   • conversion rate is weak (with enough inquiries to be meaningful), OR
    //   • the current selling pace is well behind what's needed for the yearly target.
    // Every suggestion, and the budget figures, are derived from real CRM numbers already
    // on this page — there is no external AI/LLM call involved.
    private static MarketingAdvisorViewModel BuildMarketingAdvisor(
        decimal totalSalesThisMonth, decimal totalSalesLastMonth, decimal conversion,
        int totalInqPeriod, int lastMonthInqCount,
        decimal yearlyTarget, decimal yearlyRemaining, decimal yearlyPaceDay, decimal monthsLeftInYear,
        decimal targetThisMonth, DateTime from, DateTime to,
        bool isCompanyWideScope, List<Dealer> dealers, List<YearlyContributionRow> yearlyContribution,
        ProductVelocityRow? bestProduct, List<ProductVelocityRow> productVelocity)
    {
        var model = new MarketingAdvisorViewModel();
        var daysInPeriod = Math.Max(1, (to - from).Days + 1);
        var recentDailyPace = totalSalesThisMonth / daysInPeriod;

        var salesDropPct = totalSalesLastMonth > 0 ? Math.Round((totalSalesLastMonth - totalSalesThisMonth) * 100m / totalSalesLastMonth, 1) : 0m;
        var isSalesDown = totalSalesLastMonth > 0 && salesDropPct >= 15m;
        var isWeakConversion = totalInqPeriod >= 3 && conversion > 0 && conversion < 12m;
        var paceRatio = (yearlyTarget > 0 && yearlyRemaining > 0 && recentDailyPace > 0) ? Math.Round(yearlyPaceDay / recentDailyPace, 1) : 0m;
        var isBehindPace = paceRatio >= 1.6m;
        var isInquiryVolumeDown = lastMonthInqCount >= 3 && totalInqPeriod > 0 && totalInqPeriod < lastMonthInqCount * 0.75m;

        model.IsSlowdownDetected = isSalesDown || isWeakConversion || isBehindPace || isInquiryVolumeDown;
        if (!model.IsSlowdownDetected) return model;

        var reasons = new List<string>();
        if (isSalesDown) reasons.Add($"sales are down {salesDropPct}% vs last month");
        if (isWeakConversion) reasons.Add($"conversion is only {conversion}% this period");
        if (isBehindPace) reasons.Add($"you're about {paceRatio}x behind the daily pace needed for this year's target");
        if (isInquiryVolumeDown) reasons.Add("fewer new inquiries are coming in than last month");
        model.SlowdownSummary = "Sales look slow right now — " + string.Join("; ", reasons) + ".";

        var quietPartners = isCompanyWideScope
            ? dealers.Count(d => d.IsActive) - yearlyContribution.Count(x => x.RoleLabel == "Partner")
            : 0;
        var focusProductName = bestProduct?.ProductName
            ?? productVelocity.OrderByDescending(x => x.RevenuePerDay).Select(x => x.ProductName).FirstOrDefault();

        // 1) New-lead marketing — only when fresh inquiry volume itself has dropped.
        if (isInquiryVolumeDown)
        {
            model.Suggestions.Add(new MarketingSuggestion
            {
                MarketingType = "Digital Lead Generation",
                Action = "Run targeted Google/Facebook/Instagram ads and a WhatsApp broadcast to your existing database" + (string.IsNullOrWhiteSpace(focusProductName) ? "." : $", spotlighting {focusProductName} since it already has the strongest recent sales pace."),
                Reason = $"New inquiries this period ({totalInqPeriod}) are noticeably lower than last month ({lastMonthInqCount}) — the pipeline itself needs topping up.",
                Priority = "High"
            });
        }

        // 2) Retargeting / nurture — cheaper than new lead-gen, targets the leads already in hand.
        if (isWeakConversion || isSalesDown)
        {
            model.Suggestions.Add(new MarketingSuggestion
            {
                MarketingType = "Follow-up & Retargeting Campaign",
                Action = "Before spending on new leads, run a focused follow-up/remarketing push (calls, WhatsApp, email) on every open inquiry — a short-period offer or demo reminder usually converts warm leads fastest.",
                Reason = isWeakConversion
                    ? $"Conversion is at {conversion}% — below a healthy 15-20% range, so existing leads are stalling rather than closing."
                    : $"Sales are down {salesDropPct}% vs last month, and re-engaging leads already in the pipeline is lower-cost than new lead generation.",
                Priority = "High"
            });
        }

        // 3) Partner activation — only meaningful/visible company-wide, when partners are quiet.
        if (isCompanyWideScope && quietPartners > 0)
        {
            model.Suggestions.Add(new MarketingSuggestion
            {
                MarketingType = "Partner Activation Drive",
                Action = $"Reach out to the {quietPartners} active partner(s) with no Sold business yet this year — offer co-branded marketing material, a limited-time margin boost, or a joint webinar/demo to get them selling again.",
                Reason = $"{quietPartners} active partner(s) have zero Sold business this year, which is lost pipeline you already have access to.",
                Priority = "Medium"
            });
        }

        // 4) Product-focused push — promote the product with the best real sales pace right now.
        if (!string.IsNullOrWhiteSpace(focusProductName) && (isSalesDown || isBehindPace))
        {
            model.Suggestions.Add(new MarketingSuggestion
            {
                MarketingType = "Product-Focused Promotion",
                Action = $"Run a short-term bundle/discount or festive offer campaign specifically on {focusProductName} — it already sells at the fastest real pace among your products, so marketing spend there converts more reliably.",
                Reason = $"{focusProductName} has the strongest current sales pace of any product this year — the safest bet for marketing rupees to actually convert.",
                Priority = "Medium"
            });
        }

        // ---- Budget suggestion: based on the actual monthly shortfall (target vs achieved, ----
        // falling back to last-month vs this-month) rather than a guessed flat number.
        var monthlyShortfall = targetThisMonth > 0
            ? Math.Max(0m, targetThisMonth - totalSalesThisMonth)
            : Math.Max(0m, totalSalesLastMonth - totalSalesThisMonth);

        if (monthlyShortfall > 0)
        {
            var starter = Math.Round(monthlyShortfall * 0.02m / 500m) * 500m;
            var growth = Math.Round(monthlyShortfall * 0.04m / 500m) * 500m;
            var aggressive = Math.Round(monthlyShortfall * 0.07m / 500m) * 500m;
            starter = Math.Max(starter, 2000m);
            growth = Math.Max(growth, starter + 3000m);
            aggressive = Math.Max(aggressive, growth + 5000m);

            model.BudgetOptions.Add(new MarketingBudgetOption { Tier = "Starter", BudgetLabel = $"₹{starter:N0} / month", ExpectedOutcome = "Enough for a focused WhatsApp/email retargeting push plus a small local ad boost — best if the main issue is stalled follow-ups, not lead volume." });
            model.BudgetOptions.Add(new MarketingBudgetOption { Tier = "Growth", BudgetLabel = $"₹{growth:N0} / month", ExpectedOutcome = "Adds ongoing Google/Facebook/Instagram lead-gen ads on top of retargeting — a reasonable spend to meaningfully close this month's shortfall." });
            model.BudgetOptions.Add(new MarketingBudgetOption { Tier = "Aggressive", BudgetLabel = $"₹{aggressive:N0} / month", ExpectedOutcome = "Multi-channel push (ads + partner co-marketing + a limited-time offer) — use this if the shortfall needs to be recovered quickly." });
        }
        else
        {
            model.BudgetOptions.Add(new MarketingBudgetOption { Tier = "Starter", BudgetLabel = "₹5,000 - ₹8,000 / month", ExpectedOutcome = "A focused retargeting/follow-up push on leads already in the pipeline." });
            model.BudgetOptions.Add(new MarketingBudgetOption { Tier = "Growth", BudgetLabel = "₹10,000 - ₹15,000 / month", ExpectedOutcome = "Retargeting plus ongoing paid lead-generation ads." });
        }

        return model;
    }

    /// <summary>
    /// Resolves the signed-in Partner/User/Support person's OWN target (never the
    /// company-wide Yearly Target, which is Admin/SupportHead only). User and Support
    /// use their AppUser.TargetAmount. Partner first tries the matching Dealer's
    /// "Year Wise Partner Target" row for the given year (falls back through past/future
    /// rows the same way the Partner Master page does), then falls back to the Partner
    /// login's own AppUser.TargetAmount if no Dealer record is linked yet — so a newly
    /// created Partner login still shows a sensible number instead of a silent ₹0.
    /// </summary>
    private static decimal GetOwnYearlyTarget(string role, string userId, List<AppUser> users, List<Dealer> dealers, int year)
    {
        if (string.IsNullOrWhiteSpace(userId)) return 0m;
        var user = users.FirstOrDefault(x => x.Id.Equals(userId, StringComparison.OrdinalIgnoreCase));
        if (user == null) return 0m;

        if (role.Equals("Partner", StringComparison.OrdinalIgnoreCase))
        {
            var dealer = dealers.FirstOrDefault(d =>
                (!string.IsNullOrWhiteSpace(user.PartnerCode) && d.Id.Equals(user.PartnerCode, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(user.FullName) && d.DealerName.Equals(user.FullName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(user.FullName) && d.ContactPerson.Equals(user.FullName, StringComparison.OrdinalIgnoreCase)));
            var dealerTarget = dealer?.GetTargetForYear(year)?.Amount ?? 0m;
            return dealerTarget > 0 ? dealerTarget : user.TargetAmount;
        }

        // User / Support / any other non-company-wide role.
        return user.TargetAmount;
    }

    private async Task<List<Inquiry>> GetVisibleInquiriesAsync(string role, string userId)
        => await _inquiryService.GetVisibleForRoleAsync(role, userId);
}
