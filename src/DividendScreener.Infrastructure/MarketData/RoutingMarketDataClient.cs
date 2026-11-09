using DividendScreener.Core.MarketData;
using Microsoft.Extensions.Logging;

namespace DividendScreener.Infrastructure.MarketData;

/// <summary>
/// Picks a provider by ticker format and falls back to the other one.
/// Exchange-suffixed tickers (SAN.PA, NDA-SE.ST) go to <paramref name="europeanClient"/> first;
/// plain tickers (KO, AAPL) go to <paramref name="usClient"/> first.
/// </summary>
/// <remarks>
/// The first snapshot wins. When neither provider returns one, the error carries every attempt's reason
/// (HTTP status, path, provider error text or JSON parsing error), primary first:
/// <list type="bullet">
/// <item>The primary does not know the ticker → <see cref="TickerNotFoundException"/> (404), whatever the fallback
/// said, since the primary is authoritative for its own ticker format.</item>
/// <item>The primary failed → <see cref="MarketDataException"/> (502).</item>
/// </list>
/// Class shares such as BRK.B contain a dot too; they reach the US provider through the fallback.
/// </remarks>
public sealed class RoutingMarketDataClient(
    IMarketDataClient usClient,
    IMarketDataClient europeanClient,
    ILogger<RoutingMarketDataClient> logger) : IMarketDataClient
{
    public async Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var (primary, secondary) = IsExchangeSuffixed(ticker) ? (europeanClient, usClient) : (usClient, europeanClient);

        var primaryOutcome = await TryAsync(primary, ticker, cancellationToken);
        if (primaryOutcome.Snapshot is not null)
            return primaryOutcome.Snapshot;
        logger.LogWarning("Primary provider had no data for {Ticker}, trying fallback: {Reason}", ticker, primaryOutcome.Reason);

        var fallbackOutcome = await TryAsync(secondary, ticker, cancellationToken);
        if (fallbackOutcome.Snapshot is not null)
            return fallbackOutcome.Snapshot;
        logger.LogWarning("Fallback provider had no data for {Ticker}: {Reason}", ticker, fallbackOutcome.Reason);

        var detail = $"{primaryOutcome.Reason} Fallback: {fallbackOutcome.Reason}";
        throw primaryOutcome.Error is null or TickerNotFoundException
            ? new TickerNotFoundException(detail, primaryOutcome.Error)
            : new MarketDataException(detail, primaryOutcome.Error);
    }

    internal static bool IsExchangeSuffixed(string ticker) => ticker.Contains('.');

    private static async Task<Outcome> TryAsync(IMarketDataClient client, string ticker, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await client.GetSnapshotAsync(ticker, cancellationToken);
            return snapshot is not null
                ? new Outcome(snapshot, null, string.Empty)
                : new Outcome(null, null, $"{client.GetType().Name} has no data for '{ticker}'.");
        }
        catch (MarketDataException ex)
        {
            return new Outcome(null, ex, ex.Message);
        }
    }

    private sealed record Outcome(MarketSnapshot? Snapshot, MarketDataException? Error, string Reason);
}
