namespace DividendScreener.Infrastructure.MarketData;

/// <summary>Bound from the "MarketData" configuration section.</summary>
public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>Which provider to use. Only "Sample" exists so far; a real provider is the next step.</summary>
    public string Provider { get; set; } = "Sample";

    /// <summary>How long a fetched snapshot is reused before asking the provider again.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(15);
}
