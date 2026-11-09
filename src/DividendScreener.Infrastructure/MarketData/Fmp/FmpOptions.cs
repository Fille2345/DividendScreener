namespace DividendScreener.Infrastructure.MarketData.Fmp;

public sealed class FmpOptions
{
    /// <summary>Configuration key holding the API key, from an environment variable or .NET user secrets.</summary>
    public const string ApiKeyConfigKey = "FMP_API_KEY";

    public const string HttpClientName = "fmp";

    public string BaseUrl { get; set; } = "https://financialmodelingprep.com/stable/";

    /// <summary>Never set in appsettings.json. Read from <see cref="ApiKeyConfigKey"/>.</summary>
    public string? ApiKey { get; set; }
}
