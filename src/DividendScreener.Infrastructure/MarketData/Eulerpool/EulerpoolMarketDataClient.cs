using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace DividendScreener.Infrastructure.MarketData.Eulerpool;

/// <summary>
/// <see cref="IMarketDataClient"/> backed by Eulerpool, which covers European listings.
/// The profile endpoint resolves exchange-suffixed tickers (SAN.PA → FR0000120578); every other call uses the ISIN.
/// One lookup costs five requests, so it is meant to sit behind <see cref="CachingMarketDataClient"/>.
/// </summary>
public sealed class EulerpoolMarketDataClient(
    IHttpClientFactory httpClientFactory,
    EulerpoolOptions options,
    TimeProvider timeProvider,
    ILogger<EulerpoolMarketDataClient> logger) : IMarketDataClient
{
    public const string SourceName = "Eulerpool";

    /// <summary>Eulerpool reports statement amounts in millions of the reporting currency.</summary>
    private const decimal StatementUnit = 1_000_000m;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new MarketDataException(
                $"The Eulerpool API key is not configured. Set the {EulerpoolOptions.ApiKeyConfigKey} environment variable or user secret.");

        var symbol = ticker.Trim().ToUpperInvariant();

        var profile = await GetAsync<EulerpoolProfile>(
            $"equity/profile/{Uri.EscapeDataString(symbol)}", symbol, optional: false, cancellationToken);
        if (profile?.Isin is not { Length: > 0 } isin)
            return null;

        var id = Uri.EscapeDataString(isin);
        var quotesTask = GetAsync<List<EulerpoolQuote>>($"equity/quotes/{id}", symbol, optional: false, cancellationToken);
        var metricsTask = GetAsync<EulerpoolPriceMetrics>($"sentiment/price-metrics/{id}", symbol, optional: false, cancellationToken);
        var cashFlowTask = GetAsync<List<EulerpoolCashFlowRow>>($"equity/cashflowstatement/{id}", symbol, optional: true, cancellationToken);
        var dividendsTask = GetAsync<List<EulerpoolDividend>>($"equity/dividends/{id}", symbol, optional: false, cancellationToken);
        await Task.WhenAll(quotesTask, metricsTask, cashFlowTask, dividendsTask);

        var price = (await quotesTask)?
            .Where(q => q is { Timestamp: not null, Price: > 0 })
            .MaxBy(q => q.Timestamp)?.Price
            ?? throw new MarketDataException($"Eulerpool returned no recent price for {symbol}.");

        // A beta of exactly 0 means "unknown" in practice; no listed equity has zero market sensitivity.
        var beta = (await metricsTask)?.Beta;
        if (beta is null or 0)
            throw new MarketDataException($"Eulerpool has no beta for {symbol}, which the CAPM discount rate requires.");

        var cashFlow = LatestCompleteStatement(await cashFlowTask ?? []);
        var dividends = await dividendsTask ?? [];

        var stock = new Stock
        {
            Ticker = symbol,
            Name = profile.Name ?? symbol,
            Isin = isin,
            Exchange = ExchangeName(symbol),
            Currency = profile.Currency ?? throw new MarketDataException($"Eulerpool returned no currency for {symbol}."),
            CurrentPrice = price,
            Sector = profile.Sector,
        };

        var metrics = new FinancialMetrics
        {
            Beta = beta.Value,
            FreeCashFlow = cashFlow is null ? null : (cashFlow.Fcf ?? cashFlow.NetOperatingCashFlow + cashFlow.Capex) * StatementUnit,
            DividendsPaid = cashFlow?.CashDividendsPaid * StatementUnit,
            // Keyed by ex-dividend date (period) to match the FMP provider; pay date is the fallback.
            DividendHistory = AnnualDividendHistory.FromPayments(
                dividends.Select(d => (d.Period ?? d.PayDate, d.Dividend)), timeProvider.GetUtcNow().Year),
        };

        return new MarketSnapshot(stock, metrics, SourceName, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// The most recent fiscal year whose statement looks complete. Eulerpool publishes the newest year early with
    /// capex and dividends still at 0 (FCF then equals operating cash flow), which would show a 0% payout, so a
    /// row only counts once capex is filled in.
    /// </summary>
    internal static EulerpoolCashFlowRow? LatestCompleteStatement(IEnumerable<EulerpoolCashFlowRow> rows) =>
        rows.Where(r => r is { Capex: not null and not 0 } && (r.Fcf is not null || r.NetOperatingCashFlow is not null))
            .MaxBy(r => r.Year ?? int.MinValue);

    private static readonly Dictionary<string, string> ExchangesBySuffix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PA"] = "Euronext Paris",
        ["AS"] = "Euronext Amsterdam",
        ["BR"] = "Euronext Brussels",
        ["LS"] = "Euronext Lisbon",
        ["IR"] = "Euronext Dublin",
        ["MI"] = "Borsa Italiana",
        ["MC"] = "Bolsa de Madrid",
        ["DE"] = "Xetra",
        ["F"] = "Frankfurt Stock Exchange",
        ["VI"] = "Wiener Börse",
        ["SW"] = "SIX Swiss Exchange",
        ["L"] = "London Stock Exchange",
        ["ST"] = "Nasdaq Stockholm",
        ["SE"] = "Nasdaq Stockholm",
        ["CO"] = "Nasdaq Copenhagen",
        ["HE"] = "Nasdaq Helsinki",
        ["OL"] = "Oslo Børs",
    };

    /// <summary>The profile has no exchange field, so the label comes from the ticker suffix ("SAN.PA" → Euronext Paris).</summary>
    internal static string ExchangeName(string symbol)
    {
        var dot = symbol.LastIndexOf('.');
        if (dot < 0)
            return string.Empty;
        var suffix = symbol[(dot + 1)..];
        return ExchangesBySuffix.GetValueOrDefault(suffix, suffix);
    }

    /// <summary>
    /// Calls one Eulerpool endpoint and translates every failure into <see cref="MarketDataException"/>.
    /// When <paramref name="optional"/> is true, data outside the plan's access rights yields null instead.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, string symbol, bool optional, CancellationToken cancellationToken)
        where T : class
    {
        var endpoint = string.Join('/', path.Split('/')[..2]);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        // Bearer header keeps the key out of URLs and logs entirely.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(EulerpoolOptions.HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MarketDataException("Could not reach Eulerpool.", ex);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MarketDataException("Eulerpool did not respond in time.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return null;
                case HttpStatusCode.TooManyRequests:
                    throw new MarketDataException("Eulerpool rate limit reached. Try again later.");
                case HttpStatusCode.Unauthorized:
                    throw new MarketDataException(
                        $"Eulerpool rejected the API key. Check {EulerpoolOptions.ApiKeyConfigKey}.");
                case HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired:
                    logger.LogInformation("Eulerpool plan does not cover {Endpoint} for {Symbol}", endpoint, symbol);
                    return optional
                        ? null
                        : throw new MarketDataException($"Your Eulerpool plan does not include {endpoint} data for {symbol}.");
                case var status when !response.IsSuccessStatusCode:
                    throw new MarketDataException(
                        $"Eulerpool returned HTTP {(int)status} for {endpoint}{ErrorDetail(body)}.");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var error))
                    throw new MarketDataException($"Eulerpool reported an error for {endpoint}: {error}");

                return doc.Deserialize<T>(JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new MarketDataException($"Eulerpool returned malformed data for {endpoint}.", ex);
            }
        }
    }

    private static string ErrorDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) ? $": {e}" : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
