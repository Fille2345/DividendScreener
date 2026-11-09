using System.Net;
using System.Text.Json;
using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace DividendScreener.Infrastructure.MarketData.Fmp;

/// <summary>
/// <see cref="IMarketDataClient"/> backed by Financial Modeling Prep's /stable API.
/// One lookup costs three requests (profile, cash-flow statement, dividends), so it is meant to sit
/// behind <see cref="CachingMarketDataClient"/>.
/// </summary>
public sealed class FmpMarketDataClient(
    IHttpClientFactory httpClientFactory,
    FmpOptions options,
    TimeProvider timeProvider,
    ILogger<FmpMarketDataClient> logger) : IMarketDataClient
{
    public const string SourceName = "Financial Modeling Prep";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new MarketDataException(
                $"The FMP API key is not configured. Set the {FmpOptions.ApiKeyConfigKey} environment variable or user secret.");

        var symbol = ticker.Trim().ToUpperInvariant();
        var query = "symbol=" + Uri.EscapeDataString(symbol);

        var profilePath = $"profile?{query}";
        var profile = (await GetAsync<List<FmpProfile>>(profilePath, symbol, optional: false, cancellationToken))
            ?.FirstOrDefault()
            ?? throw new TickerNotFoundException(
                $"Financial Modeling Prep does not recognise '{symbol}' (empty or 404 response from {profilePath}).");

        if (profile.Price is not > 0)
            throw new MarketDataException($"FMP returned no current price for {symbol} ({profilePath}).");
        // FMP reports 0 when it has no beta; a true beta of exactly zero does not occur for a listed equity.
        if (profile.Beta is null or 0)
            throw new MarketDataException($"FMP has no beta for {symbol} ({profilePath}), which the CAPM discount rate requires.");

        // The statement is optional: without it the valuation still runs, only the FCF payout ratio is missing.
        var cashFlowTask = GetAsync<List<FmpCashFlowStatement>>(
            $"cash-flow-statement?{query}&period=annual&limit=1", symbol, optional: true, cancellationToken);
        var dividendsTask = GetAsync<List<FmpDividend>>($"dividends?{query}", symbol, optional: false, cancellationToken);
        await Task.WhenAll(cashFlowTask, dividendsTask);

        var cashFlow = (await cashFlowTask)?
            .Where(s => s.Date is not null)
            .MaxBy(s => s.Date, StringComparer.Ordinal); // ISO dates sort lexically

        var stock = new Stock
        {
            Ticker = profile.Symbol ?? symbol,
            Name = profile.CompanyName ?? symbol,
            Isin = profile.Isin ?? string.Empty,
            Exchange = profile.ExchangeFullName ?? profile.Exchange ?? string.Empty,
            Currency = profile.Currency ?? throw new MarketDataException($"FMP returned no trading currency for {symbol}."),
            CurrentPrice = profile.Price.Value,
            Sector = profile.Sector,
        };

        var metrics = new FinancialMetrics
        {
            Beta = profile.Beta.Value,
            FreeCashFlow = cashFlow is null ? null : FreeCashFlowOf(cashFlow),
            DividendsPaid = cashFlow is null ? null : DividendsPaidOf(cashFlow),
            DividendHistory = ToAnnualHistory(await dividendsTask ?? [], timeProvider.GetUtcNow().Year),
        };

        return new MarketSnapshot(stock, metrics, SourceName, timeProvider.GetUtcNow());
    }

    /// <summary>Annual history from FMP payment rows, keyed by ex-dividend date, preferring split-adjusted amounts.</summary>
    internal static IReadOnlyList<AnnualDividend> ToAnnualHistory(IEnumerable<FmpDividend> payments, int currentYear) =>
        AnnualDividendHistory.FromPayments(payments.Select(p => (p.Date, p.AdjDividend ?? p.Dividend)), currentYear);

    private static decimal? FreeCashFlowOf(FmpCashFlowStatement s) =>
        s.FreeCashFlow ?? (s.OperatingCashFlow + s.CapitalExpenditure); // capex is reported as a negative number

    /// <summary>Prefers dividends to common shareholders; FMP sometimes reports 0 there and fills only the net figure.</summary>
    private static decimal? DividendsPaidOf(FmpCashFlowStatement s)
    {
        decimal?[] candidates = [s.CommonDividendsPaid, s.NetDividendsPaid, s.DividendsPaid];
        return candidates.FirstOrDefault(v => v is not null and not 0) ?? candidates.FirstOrDefault(v => v is not null);
    }

    private const int LoggedBodyLength = 500;

    /// <summary>
    /// Calls one FMP endpoint, logs the path (never the key), status and timing, and translates every failure into a
    /// <see cref="MarketDataException"/> whose message names the HTTP status and path.
    /// When <paramref name="optional"/> is true, data the plan does not cover (HTTP 402) yields null instead.
    /// A 404 yields null; the caller decides what that means.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, string symbol, bool optional, CancellationToken cancellationToken)
        where T : class
    {
        var client = httpClientFactory.CreateClient(FmpOptions.HttpClientName);
        // Logged URL: the key is appended only to the request itself, never to what gets logged.
        var url = client.BaseAddress is null ? path : new Uri(client.BaseAddress, path).ToString();
        var requestUri = $"{path}&apikey={Uri.EscapeDataString(options.ApiKey!)}";

        var started = timeProvider.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("FMP GET {Url} failed: {Error}", url, ex.Message);
            throw new MarketDataException($"Could not reach Financial Modeling Prep ({path}): {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("FMP GET {Url} timed out", url);
            throw new MarketDataException($"Financial Modeling Prep did not respond in time ({path}).", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;
            var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            var where = $"HTTP {status} from {path}";

            if (response.IsSuccessStatusCode)
                logger.LogInformation("FMP GET {Url} -> {StatusCode} ({ElapsedMs} ms, {Bytes} bytes)",
                    url, status, elapsedMs, body.Length);
            else
                logger.LogWarning("FMP GET {Url} -> {StatusCode} ({ElapsedMs} ms). Body: {Body}",
                    url, status, elapsedMs, Truncate(body));

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return null;
                case HttpStatusCode.TooManyRequests:
                    throw RateLimited(where);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw KeyRejected(where);
                case HttpStatusCode.PaymentRequired:
                    return optional ? null : throw NotInPlan(symbol, where);
                case var _ when !response.IsSuccessStatusCode:
                    throw new MarketDataException($"Financial Modeling Prep request failed ({where}).");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                // FMP also reports errors as HTTP 200 with {"Error Message": "..."}.
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Error Message", out var error))
                {
                    var message = error.GetString() ?? string.Empty;
                    var detail = $"{where}: {message}";
                    if (message.Contains("Limit Reach", StringComparison.OrdinalIgnoreCase))
                        throw RateLimited(detail);
                    if (message.Contains("API KEY", StringComparison.OrdinalIgnoreCase))
                        throw KeyRejected(detail);
                    if (message.Contains("Premium", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("subscription", StringComparison.OrdinalIgnoreCase))
                        return optional ? null : throw NotInPlan(symbol, detail);
                    throw new MarketDataException($"Financial Modeling Prep reported an error ({detail}).");
                }

                return doc.Deserialize<T>(JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning("FMP GET {Url} returned JSON that could not be parsed as {Type}: {Error}. Body: {Body}",
                    url, typeof(T).Name, ex.Message, Truncate(body));
                throw new MarketDataException(
                    $"Financial Modeling Prep returned malformed data for {symbol} ({where}): {ex.Message}", ex);
            }
        }
    }

    private static string Truncate(string body) =>
        body.Length <= LoggedBodyLength ? body : body[..LoggedBodyLength] + "…";

    private static MarketDataException RateLimited(string where) =>
        new($"Financial Modeling Prep rate limit reached ({where}). Try again later; the free plan allows 250 requests a day.");

    private static MarketDataException KeyRejected(string where) =>
        new($"Financial Modeling Prep rejected the API key ({where}). Check {FmpOptions.ApiKeyConfigKey}.");

    private static MarketDataException NotInPlan(string symbol, string where) =>
        new($"Your Financial Modeling Prep plan does not include data for {symbol} ({where}).");
}
