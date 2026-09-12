namespace ProfitNx.CRM.Services;

/// <summary>
/// Stores the company-wide Yearly Target (Live Dashboard → Yearly Target
/// Planner) that the Admin sets. Kept in a small local JSON file
/// (App_Data/yearly-targets.json) — one value per financial year (key is the
/// FY's start year, e.g. "2026" means 01/04/2026 to 31/03/2027 — same
/// convention as the Dealer "Year Wise Partner Target" grid) — rather
/// than the Google Sheet, since it is a single company-wide planning number
/// and not per-row CRM data.
/// </summary>
public interface IYearlyTargetService
{
    Task<decimal> GetTargetAsync(int year);
    Task SetTargetAsync(int year, decimal amount);
}
