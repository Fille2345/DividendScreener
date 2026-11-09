namespace DividendScreener.Core.Models;

/// <summary>Dividend per share declared for one fiscal year (sum of interim and final dividends).</summary>
public sealed record AnnualDividend(int FiscalYear, decimal DividendPerShare);

/// <summary>
/// Fundamentals for one stock as of its latest fiscal year.
/// Cash-flow figures are company totals in the reporting currency.
/// </summary>
public sealed record FinancialMetrics
{
    /// <summary>Equity beta against the reference market index.</summary>
    public required decimal Beta { get; init; }

    /// <summary>Free cash flow: operating cash flow minus capital expenditure. Null when the provider has no cash-flow statement.</summary>
    public decimal? FreeCashFlow { get; init; }

    /// <summary>
    /// Total cash dividends paid. Cash-flow statements usually show this as a negative outflow;
    /// either sign is accepted and the magnitude is used. Null when unknown.
    /// </summary>
    public decimal? DividendsPaid { get; init; }

    /// <summary>Annual dividend per share history; the most recent year is treated as D0.</summary>
    public required IReadOnlyList<AnnualDividend> DividendHistory { get; init; }
}
