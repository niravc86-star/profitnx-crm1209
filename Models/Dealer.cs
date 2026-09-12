using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitNx.CRM.Models;

/// <summary>Raw shape stored in Dealer.TargetRowsJson (values arrive as strings from the Year Wise Partner Target grid).</summary>
public class DealerTargetRowRaw
{
    [JsonPropertyName("year")] public string? Year { get; set; }
    [JsonPropertyName("amount")] public string? Amount { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
}

/// <summary>Strongly typed year-wise target row for a partner.</summary>
public class DealerTargetRow
{
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class Dealer
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DealerName { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal MarginPercent { get; set; }
    public decimal YearlyTargetAmount { get; set; }
    public int TargetYear { get; set; } = DateTime.Today.Year;
    public DateTime? TargetFromDate { get; set; }
    public DateTime? TargetToDate { get; set; }
    public string TargetRowsJson { get; set; } = string.Empty;
    public DateTime? JoiningDate { get; set; }
    public bool GivesPss { get; set; }
    public bool GivesApi { get; set; }
    public DateTime? PssApiChangeDate { get; set; }
    public string PssApiRemark { get; set; } = string.Empty;
    public DateTime? LeftDate { get; set; }
    public string LeftRemark { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Parses TargetRowsJson (the "Year Wise Partner Target" rows) into typed rows.
    /// Falls back to the legacy single Year/Amount/From/To fields when no rows are stored yet.
    /// </summary>
    public List<DealerTargetRow> GetTargetRows()
    {
        var rows = new List<DealerTargetRow>();
        if (!string.IsNullOrWhiteSpace(TargetRowsJson))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<List<DealerTargetRowRaw>>(TargetRowsJson);
                if (raw != null)
                {
                    foreach (var r in raw)
                    {
                        int.TryParse(r.Year, out var year);
                        decimal.TryParse(r.Amount, out var amount);
                        DateTime.TryParse(r.From, out var from);
                        DateTime.TryParse(r.To, out var to);
                        if (year <= 0 && amount <= 0 && string.IsNullOrWhiteSpace(r.From) && string.IsNullOrWhiteSpace(r.To)) continue;
                        rows.Add(new DealerTargetRow
                        {
                            Year = year > 0 ? year : DateTime.Today.Year,
                            Amount = amount,
                            FromDate = string.IsNullOrWhiteSpace(r.From) ? null : from,
                            ToDate = string.IsNullOrWhiteSpace(r.To) ? null : to
                        });
                    }
                }
            }
            catch { /* ignore malformed rows */ }
        }

        if (rows.Count == 0 && (TargetYear > 0 || YearlyTargetAmount > 0 || TargetFromDate.HasValue || TargetToDate.HasValue))
        {
            rows.Add(new DealerTargetRow { Year = TargetYear > 0 ? TargetYear : DateTime.Today.Year, Amount = YearlyTargetAmount, FromDate = TargetFromDate, ToDate = TargetToDate });
        }

        return rows.OrderBy(x => x.Year).ToList();
    }

    /// <summary>
    /// Resolves the target row that applies for the given calendar year: an exact year match first,
    /// otherwise a row whose From/To date range covers the year, otherwise the most recent past row,
    /// otherwise the earliest future row (so a partner joining mid-year still shows a sensible target).
    /// </summary>
    public DealerTargetRow? GetTargetForYear(int year)
    {
        var rows = GetTargetRows();
        if (rows.Count == 0) return null;

        var exact = rows.FirstOrDefault(x => x.Year == year);
        if (exact != null) return exact;

        var byRange = rows.FirstOrDefault(x => x.FromDate.HasValue && x.ToDate.HasValue && x.FromDate.Value.Year <= year && x.ToDate.Value.Year >= year);
        if (byRange != null) return byRange;

        var pastMostRecent = rows.Where(x => x.Year < year).OrderByDescending(x => x.Year).FirstOrDefault();
        if (pastMostRecent != null) return pastMostRecent;

        return rows.OrderBy(x => x.Year).FirstOrDefault();
    }
}
