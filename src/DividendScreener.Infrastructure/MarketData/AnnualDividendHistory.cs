using System.Globalization;
using DividendScreener.Core.Models;

namespace DividendScreener.Infrastructure.MarketData;

/// <summary>Turns per-payment dividend rows from any provider into the annual history the valuation uses.</summary>
internal static class AnnualDividendHistory
{
    /// <summary>A 10-year CAGR needs 11 annual data points.</summary>
    public const int Years = 11;

    /// <summary>
    /// Sums payments per calendar year of their date and returns up to <see cref="Years"/> complete years, oldest first.
    /// </summary>
    /// <remarks>
    /// The current year is excluded because it is incomplete. Years inside the window with no payment are
    /// reported as 0 (e.g. the 2020 suspensions by many European companies), so a lapsed dividend shows as
    /// a cut instead of an old D0. The earliest year in the data is dropped when it falls inside the window,
    /// because the data may start mid-year and understate that year's total.
    /// Dates are ISO-8601; anything after the "yyyy-MM-dd" prefix (a time component) is ignored.
    /// </remarks>
    public static IReadOnlyList<AnnualDividend> FromPayments(
        IEnumerable<(string? Date, decimal? Amount)> payments, int currentYear)
    {
        var byYear = new Dictionary<int, decimal>();
        foreach (var (dateText, amount) in payments)
        {
            if (amount is not > 0 || dateText is not { Length: >= 10 } ||
                !DateOnly.TryParseExact(dateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) ||
                date.Year >= currentYear)
                continue;

            byYear[date.Year] = byYear.GetValueOrDefault(date.Year) + amount.Value;
        }

        if (byYear.Count == 0)
            return [];

        var lastFullYear = currentYear - 1;
        var windowStart = lastFullYear - Years + 1;
        var earliest = byYear.Keys.Min();
        var firstYear = earliest > windowStart ? earliest + 1 : windowStart;

        var history = new List<AnnualDividend>();
        for (var year = firstYear; year <= lastFullYear; year++)
            history.Add(new AnnualDividend(year, byYear.GetValueOrDefault(year)));
        return history;
    }
}
