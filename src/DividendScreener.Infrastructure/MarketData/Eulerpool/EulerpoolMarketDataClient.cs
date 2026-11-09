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

        var profilePath = $"equity/profile/{Uri.EscapeDataString(symbol)}";
        var profile = await GetAsync<EulerpoolProfile>(
            profilePath, symbol, OnNotFound.TickerNotFound, optionalInPlan: false, cancellationToken);
        if (profile?.Isin is not { Length: > 0 } isin)
            throw new TickerNotFoundException(
                $"Eulerpool returned a profile without an ISIN for '{symbol}' (HTTP 200 from {profilePath}).");

        var id = Uri.EscapeDataString(isin);
        var quotesTask = GetAsync<List<EulerpoolQuote>>(
            $"equity/quotes/{id}", symbol, OnNotFound.Throw, optionalInPlan: false, cancellationToken);
        var metricsTask = GetAsync<EulerpoolPriceMetrics>(
            $"sentiment/price-metrics/{id}", symbol, OnNotFound.Throw, optionalInPlan: false, cancellationToken);
        // Statements are optional: without them only the FCF payout ratio is missing.
        var cashFlowTask = GetAsync<List<EulerpoolCashFlowRow>>(
            $"equity/cashflowstatement/{id}", symbol, OnNotFound.ReturnNull, optionalInPlan: true, cancellationToken);
        // 404 here means "no dividends on record", which the valuation reports on its own.
        var dividendsTask = GetAsync<List<EulerpoolDividend>>(
            $"equity/dividends/{id}", symbol, OnNotFound.ReturnNull, optionalInPlan: false, cancellationToken);
        await Task.WhenAll(quotesTask, metricsTask, cashFlowTask, dividendsTask);

        var price = (await quotesTask)?
            .Where(q => q is { Timestamp: not null, Price: > 0 })
            .MaxBy(q => q.Timestamp)?.Price
            ?? throw new MarketDataException($"Eulerpool returned no recent price for {symbol} (equity/quotes/{isin}).");

        // A beta of exactly 0 means "unknown" in practice; no listed equity has zero market sensitivity.
        var beta = (await metricsTask)?.Beta;
        if (beta is null or 0)
            throw new MarketDataException(
                $"Eulerpool has no beta for {symbol} (sentiment/price-metrics/{isin}), which the CAPM discount rate requires.");

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

    /// <summary>What a 404 means for a given call.</summary>
    private enum OnNotFound
    {
        /// <summary>The ticker itself is unknown (profile lookup).</summary>
        TickerNotFound,

        /// <summary>The data is simply absent; return null.</summary>
        ReturnNull,

        /// <summary>The security was found but this required data is missing; a provider failure.</summary>
        Throw,
    }

    private const int LoggedBodyLength = 500;

    /// <summary>
    /// Calls one Eulerpool endpoint, logs the exact URL, status and timing, and translates every failure into a
    /// <see cref="MarketDataException"/> whose message names the HTTP status, the path and Eulerpool's own error text.
    /// When <paramref name="optionalInPlan"/> is true, data outside the plan's access rights yields null instead.
    /// </summary>
    private async Task<T?> GetAsync<T>(
        string path, string symbol, OnNotFound onNotFound, bool optionalInPlan, CancellationToken cancellationToken)
        where T : class
    {
        var client = httpClientFactory.CreateClient(EulerpoolOptions.HttpClientName);
        var url = client.BaseAddress is null ? path : new Uri(client.BaseAddress, path).ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        // Bearer header keeps the key out of URLs, so the URL is safe to log.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var started = timeProvider.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Eulerpool GET {Url} failed: {Error}", url, ex.Message);
            throw new MarketDataException($"Could not reach Eulerpool ({path}): {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Eulerpool GET {Url} timed out", url);
            throw new MarketDataException($"Eulerpool did not respond in time ({path}).", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;
            var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            var where = $"HTTP {status} from {path}{ErrorDetail(body)}";

            if (response.IsSuccessStatusCode)
                logger.LogInformation("Eulerpool GET {Url} -> {StatusCode} ({ElapsedMs} ms, {Bytes} bytes)",
                    url, status, elapsedMs, body.Length);
            else
                logger.LogWarning("Eulerpool GET {Url} -> {StatusCode} ({ElapsedMs} ms). Body: {Body}",
                    url, status, elapsedMs, Truncate(body));

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return onNotFound switch
                    {
                        OnNotFound.ReturnNull => null,
                        OnNotFound.TickerNotFound => throw new TickerNotFoundException(
                            $"Eulerpool does not recognise '{symbol}' ({where})."),
                        _ => throw new MarketDataException($"Eulerpool has no data for {symbol} ({where})."),
                    };
                case HttpStatusCode.TooManyRequests:
                    throw new MarketDataException($"Eulerpool rate limit reached ({where}). Try again later.");
                case HttpStatusCode.Unauthorized:
                    throw new MarketDataException(
                        $"Eulerpool rejected the API key ({where}). Check {EulerpoolOptions.ApiKeyConfigKey}.");
                case HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired:
                    return optionalInPlan
                        ? null
                        : throw new MarketDataException($"Your Eulerpool plan does not include this data for {symbol} ({where}).");
                case var _ when !response.IsSuccessStatusCode:
                    throw new MarketDataException($"Eulerpool request failed ({where}).");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var error))
                    throw new MarketDataException($"Eulerpool reported an error for {symbol}: {error} (HTTP {status} from {path}).");

                return doc.Deserialize<T>(JsonOptions);
            }
            catch (JsonException ex)
            {
                // ex.Message names the JSON path and position, e.g. "could not be converted to System.Decimal. Path: $.beta".
                logger.LogWarning("Eulerpool GET {Url} returned JSON that could not be parsed as {Type}: {Error}. Body: {Body}",
                    url, typeof(T).Name, ex.Message, Truncate(body));
                throw new MarketDataException(
                    $"Eulerpool returned malformed data for {symbol} (HTTP {status} from {path}): {ex.Message}", ex);
            }
        }
    }

    private static string Truncate(string body) =>
        body.Length <= LoggedBodyLength ? body : body[..LoggedBodyLength] + "…";

    private static string ErrorDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e)
                ? $": {e}"
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
