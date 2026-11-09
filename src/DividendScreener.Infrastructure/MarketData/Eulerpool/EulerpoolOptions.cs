namespace DividendScreener.Infrastructure.MarketData.Eulerpool;

public sealed class EulerpoolOptions
{
    /// <summary>Configuration key holding the API key, from an environment variable or .NET user secrets.</summary>
    public const string ApiKeyConfigKey = "EULERPOOL_API_KEY";

    public const string HttpClientName = "eulerpool";

    public string BaseUrl { get; set; } = "https://api.eulerpool.com/api/1/";

    /// <summary>Never set in appsettings.json. Read from <see cref="ApiKeyConfigKey"/>.</summary>
    public string? ApiKey { get; set; }
}
