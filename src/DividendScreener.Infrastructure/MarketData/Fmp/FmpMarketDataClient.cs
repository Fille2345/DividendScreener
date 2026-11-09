using System.Globalization;
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

    /// <summary>A 10-year CAGR needs 11 annual data points.</summary>
    internal const int HistoryYears = 11;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new MarketDataException(
                $"The FMP API key is not configured. Set the {FmpOptions.ApiKeyConfigKey} environment variable or user secret.");

        var symbol = ticker.Trim().ToUpperInvariant();
        var query = "symbol=" + Uri.EscapeDataString(symbol);

        var profile = (await GetAsync<List<FmpProfile>>($"profile?{query}", symbol, optional: false, cancellationToken))
            ?.FirstOrDefault();
        if (profile is null)
            return null;

        if (profile.Price is not > 0)
            throw new MarketDataException($"FMP returned no current price for {symbol}.");
        // FMP reports 0 when it has no beta; a true beta of exactly zero does not occur for a listed equity.
        if (profile.Beta is null or 0)
            throw new MarketDataException($"FMP has no beta for {symbol}, which the CAPM discount rate requires.");

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

    /// <summary>
    /// Sums per-payment dividends into calendar years of the ex-dividend date and returns up to
    /// <see cref="HistoryYears"/> complete years, oldest first.
    /// </summary>
    /// <remarks>
    /// The current year is excluded because it is incomplete. Years inside the window with no payment are
    /// reported as 0 (e.g. the 2020 suspensions by many European companies), so a lapsed dividend shows as
    /// a cut instead of an old D0. The earliest year in FMP's data is dropped when it falls inside the window,
    /// because the data may start mid-year and understate that year's total.
    /// </remarks>
    internal static IReadOnlyList<AnnualDividend> ToAnnualHistory(IEnumerable<FmpDividend> payments, int currentYear)
    {
        var byYear = new Dictionary<int, decimal>();
        foreach (var p in payments)
        {
            var amount = p.AdjDividend ?? p.Dividend;
            if (amount is not > 0 ||
                !DateOnly.TryParseExact(p.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                date.Year >= currentYear)
                continue;

            byYear[date.Year] = byYear.GetValueOrDefault(date.Year) + amount.Value;
        }

        if (byYear.Count == 0)
            return [];

        var lastFullYear = currentYear - 1;
        var windowStart = lastFullYear - HistoryYears + 1;
        var earliest = byYear.Keys.Min();
        var firstYear = earliest > windowStart ? earliest + 1 : windowStart;

        var history = new List<AnnualDividend>();
        for (var year = firstYear; year <= lastFullYear; year++)
            history.Add(new AnnualDividend(year, byYear.GetValueOrDefault(year)));
        return history;
    }

    private static decimal? FreeCashFlowOf(FmpCashFlowStatement s) =>
        s.FreeCashFlow ?? (s.OperatingCashFlow + s.CapitalExpenditure); // capex is reported as a negative number

    /// <summary>Prefers dividends to common shareholders; FMP sometimes reports 0 there and fills only the net figure.</summary>
    private static decimal? DividendsPaidOf(FmpCashFlowStatement s)
    {
        decimal?[] candidates = [s.CommonDividendsPaid, s.NetDividendsPaid, s.DividendsPaid];
        return candidates.FirstOrDefault(v => v is not null and not 0) ?? candidates.FirstOrDefault(v => v is not null);
    }

    /// <summary>
    /// Calls one FMP endpoint and translates every failure into <see cref="MarketDataException"/>.
    /// When <paramref name="optional"/> is true, data the plan does not cover (HTTP 402) yields null instead.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, string symbol, bool optional, CancellationToken cancellationToken)
        where T : class
    {
        var endpoint = path[..path.IndexOf('?')];
        var client = httpClientFactory.CreateClient(FmpOptions.HttpClientName);
        // The key goes in the query string, which FMP supports on every plan. HttpClientFactory logs redact query strings.
        var requestUri = $"{path}&apikey={Uri.EscapeDataString(options.ApiKey!)}";

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MarketDataException("Could not reach Financial Modeling Prep.", ex);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MarketDataException("Financial Modeling Prep did not respond in time.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return null;
                case HttpStatusCode.TooManyRequests:
                    throw RateLimited();
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw KeyRejected();
                case HttpStatusCode.PaymentRequired:
                    logger.LogInformation("FMP plan does not cover {Endpoint} for {Symbol}", endpoint, symbol);
                    return optional ? null : throw NotInPlan(symbol);
                case var status when !response.IsSuccessStatusCode:
                    throw new MarketDataException($"Financial Modeling Prep returned HTTP {(int)status} for {endpoint}.");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                // FMP also reports errors as HTTP 200 with {"Error Message": "..."}.
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Error Message", out var error))
                {
                    var message = error.GetString() ?? string.Empty;
                    if (message.Contains("Limit Reach", StringComparison.OrdinalIgnoreCase))
                        throw RateLimited();
                    if (message.Contains("API KEY", StringComparison.OrdinalIgnoreCase))
                        throw KeyRejected();
                    if (message.Contains("Premium", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("subscription", StringComparison.OrdinalIgnoreCase))
                        return optional ? null : throw NotInPlan(symbol);
                    throw new MarketDataException($"Financial Modeling Prep reported an error for {endpoint}: {message}");
                }

                return doc.Deserialize<T>(JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new MarketDataException($"Financial Modeling Prep returned malformed data for {endpoint}.", ex);
            }
        }
    }

    private static MarketDataException RateLimited() =>
        new("Financial Modeling Prep rate limit reached. Try again later; the free plan allows 250 requests a day.");

    private static MarketDataException KeyRejected() =>
        new($"Financial Modeling Prep rejected the API key. Check {FmpOptions.ApiKeyConfigKey}.");

    private static MarketDataException NotInPlan(string symbol) =>
        new($"Your Financial Modeling Prep plan does not include data for {symbol}.");
}
