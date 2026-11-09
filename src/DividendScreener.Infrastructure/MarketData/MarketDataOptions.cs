namespace DividendScreener.Infrastructure.MarketData;

/// <summary>Bound from the "MarketData" configuration section.</summary>
public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>
    /// "Router" (FMP for plain tickers, Eulerpool for exchange-suffixed ones, each falling back to the other),
    /// "Fmp" (needs FMP_API_KEY), "Eulerpool" (needs EULERPOOL_API_KEY) or "Sample" (offline, one fictitious company).
    /// </summary>
    public string Provider { get; set; } = "Router";

    /// <summary>How long a fetched snapshot is reused before asking the provider again.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(15);
}
