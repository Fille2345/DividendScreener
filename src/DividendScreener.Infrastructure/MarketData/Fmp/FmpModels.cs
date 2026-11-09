namespace DividendScreener.Infrastructure.MarketData.Fmp;

// Response shapes from FMP's /stable API. Every field is nullable because FMP omits or nulls
// fields freely, especially for non-US listings. Only the fields we use are mapped.

/// <summary>GET /stable/profile?symbol=…</summary>
internal sealed record FmpProfile(
    string? Symbol,
    decimal? Price,
    decimal? Beta,
    string? CompanyName,
    string? Currency,
    string? Isin,
    string? Exchange,
    string? ExchangeFullName,
    string? Sector);

/// <summary>GET /stable/cash-flow-statement?symbol=…&amp;period=annual</summary>
internal sealed record FmpCashFlowStatement(
    string? Date,
    string? ReportedCurrency,
    decimal? FreeCashFlow,
    decimal? OperatingCashFlow,
    decimal? CapitalExpenditure,
    decimal? CommonDividendsPaid,
    decimal? NetDividendsPaid,
    // Name used by the legacy v3 API; kept so either response shape parses.
    decimal? DividendsPaid);

/// <summary>GET /stable/dividends?symbol=… — one row per payment, keyed by ex-dividend date.</summary>
internal sealed record FmpDividend(string? Date, decimal? Dividend, decimal? AdjDividend);
