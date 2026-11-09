namespace DividendScreener.Infrastructure.MarketData;

/// <summary>Bound from the "MarketData" configuration section.</summary>
public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>"Fmp" (Financial Modeling Prep, needs FMP_API_KEY) or "Sample" (offline, one fictitious company).</summary>
    public string Provider { get; set; } = "Fmp";

    /// <summary>How long a fetched snapshot is reused before asking the provider again.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(15);
}
