using ProfitNx.CRM.Models;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Services;

public interface ICommissionService
{
    Task<List<CommissionRow>> GetReportAsync(CommissionFilterViewModel filter);
}
