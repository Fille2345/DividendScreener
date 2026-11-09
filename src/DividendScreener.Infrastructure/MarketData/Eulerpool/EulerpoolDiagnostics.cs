using System.Diagnostics;
using System.Net.Http.Headers;

namespace DividendScreener.Infrastructure.MarketData.Eulerpool;

/// <summary>
/// Raw access to Eulerpool for troubleshooting: one request, no parsing, no caching, no valuation.
/// Uses the same named HTTP client (base URL, timeouts, retries) and key as <see cref="EulerpoolMarketDataClient"/>.
/// </summary>
public sealed class EulerpoolDiagnostics(IHttpClientFactory httpClientFactory, EulerpoolOptions options)
{
    /// <summary>The endpoints the debug route may call, by short name. Each takes the identifier as its last segment.</summary>
    public static readonly IReadOnlyDictionary<string, string> Endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["profile"] = "equity/profile",
        ["quotes"] = "equity/quotes",
        ["price-metrics"] = "sentiment/price-metrics",
        ["cashflowstatement"] = "equity/cashflowstatement",
        ["dividends"] = "equity/dividends",
    };

    public bool HasApiKey => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<RawProviderResponse> GetRawAsync(string endpoint, string identifier, CancellationToken cancellationToken)
    {
        var path = $"{Endpoints[endpoint]}/{Uri.EscapeDataString(identifier)}";
        var client = httpClientFactory.CreateClient(EulerpoolOptions.HttpClientName);
        var url = client.BaseAddress is null ? path : new Uri(client.BaseAddress, path).ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (HasApiKey)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new RawProviderResponse(
            url,
            (int)response.StatusCode,
            response.ReasonPhrase,
            response.Content.Headers.ContentType?.ToString(),
            stopwatch.ElapsedMilliseconds,
            HasApiKey,
            body);
    }
}

/// <summary>What the provider sent back, unmodified. <see cref="Url"/> never contains the API key.</summary>
public sealed record RawProviderResponse(
    string Url,
    int StatusCode,
    string? ReasonPhrase,
    string? ContentType,
    long ElapsedMs,
    bool ApiKeySent,
    string Body);
