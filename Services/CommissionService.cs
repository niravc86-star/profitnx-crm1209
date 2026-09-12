using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public class CommissionService : ICommissionService
{
    private readonly IInquiryService _inquiryService;
    private readonly IUserService _userService;

    public CommissionService(IInquiryService inquiryService, IUserService userService)
    {
        _inquiryService = inquiryService;
        _userService = userService;
    }

    public async Task<List<CommissionRow>> GetReportAsync(CommissionFilterViewModel filter)
    {
        var inquiries = await _inquiryService.GetAllAsync();
        var users = await _userService.GetAllUsersAsync();
        var sold = inquiries.Where(x => x.Status.Equals("Sold", StringComparison.OrdinalIgnoreCase));
        // Use BillDate when available for filtering; otherwise fall back to CreatedDate — ensure inclusive date matches
        if (filter.FromDate.HasValue) sold = sold.Where(x => ((x.BillDate ?? x.CreatedDate).Date) >= filter.FromDate.Value.Date);
        if (filter.ToDate.HasValue) sold = sold.Where(x => ((x.BillDate ?? x.CreatedDate).Date) <= filter.ToDate.Value.Date);
        if (!string.IsNullOrWhiteSpace(filter.PartnerId)) sold = sold.Where(x => x.ForwardedToPartnerId == filter.PartnerId);
        if (!string.IsNullOrWhiteSpace(filter.UserId)) sold = sold.Where(x => x.AssignedUserId == filter.UserId);

        return sold.Select(x => {
            var partner = users.FirstOrDefault(u => u.Id == x.ForwardedToPartnerId || u.FullName.Equals(x.ForwardedToPartnerName, StringComparison.OrdinalIgnoreCase));
            var owner = partner ?? users.FirstOrDefault(u => u.Id == x.AssignedUserId);
            var partnerMargin = x.AppliedMarginPercent > 0 ? x.AppliedMarginPercent : partner?.MarginPercent ?? 0;
            var margin = owner?.MarginPercent ?? 0;
            var grossPartnerBase = x.UnitPriceWithoutGst > 0 ? x.UnitPriceWithoutGst * Math.Max(1, x.LicensesPurchased) : x.AmountWithoutGst;
            var partnerMarginAmount = x.PriceType.Equals("Partner", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0, Math.Round(grossPartnerBase - x.AmountWithoutGst, 2)) : 0m;
            var afterPartner = x.AmountWithoutGst;
            var marginAmount = Math.Round(afterPartner * margin / 100m, 2);
            return new CommissionRow
            {
                InquiryId = x.Id,
                InquiryDate = x.CreatedDate,
                BillDate = x.BillDate,
                BillNo = x.BillNo,
                LicenseNumber = x.LicenseNumber,
                FirmName = x.FirmName,
                City = x.City,
                ProductName = x.ProductName ?? string.Empty,
                VersionType = x.VersionType,
                PartnerName = string.IsNullOrWhiteSpace(x.ForwardedToPartnerName) ? "Self" : x.ForwardedToPartnerName,
                AssignedUserName = x.AssignedUserName,
                AmountWithoutGst = x.AmountWithoutGst,
                PartnerMarginPercent = partnerMargin,
                PartnerMarginAmount = partnerMarginAmount,
                AfterPartnerMarginAmount = afterPartner,
                MarginPercent = margin,
                CommissionAmount = marginAmount,
                AfterLessMarginAmount = afterPartner - marginAmount,
                AmountWithGst = x.AmountWithGst,
                Remarks = x.StatusReason,
                Status = x.Status
            };
        }).OrderByDescending(x => x.BillDate ?? x.InquiryDate).ToList();
    }
}
