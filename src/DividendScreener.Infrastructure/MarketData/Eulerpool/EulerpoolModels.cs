namespace DividendScreener.Infrastructure.MarketData.Eulerpool;

// Response shapes from Eulerpool's /api/1 API (OpenAPI spec: https://api.eulerpool.com/api/1/documentation/yaml).
// Only the fields we use are mapped; all are nullable because coverage varies by security.

/// <summary>GET /equity/profile/{identifier}. Accepts exchange-suffixed tickers such as SAN.PA and resolves them to an ISIN.</summary>
internal sealed record EulerpoolProfile(string? Isin, string? Name, string? Currency, string? Sector, string? Country);

/// <summary>GET /equity/quotes/{isin}: daily closes for the last year. Timestamp is milliseconds since 1970.</summary>
internal sealed record EulerpoolQuote(long? Timestamp, decimal? Price);

/// <summary>GET /sentiment/price-metrics/{identifier}. Other fields in this response are strings, beta is a number.</summary>
internal sealed record EulerpoolPriceMetrics(string? Symbol, decimal? Beta);

/// <summary>GET /equity/cashflowstatement/{identifier}: one row per fiscal year. Amounts are in millions.</summary>
internal sealed record EulerpoolCashFlowRow(
    string? Period,
    int? Year,
    decimal? NetOperatingCashFlow,
    decimal? Capex,
    decimal? Fcf,
    decimal? CashDividendsPaid);

/// <summary>GET /equity/dividends/{identifier}: one row per payment. Period is the ex-dividend date.</summary>
internal sealed record EulerpoolDividend(string? PayDate, string? Period, decimal? Dividend);
