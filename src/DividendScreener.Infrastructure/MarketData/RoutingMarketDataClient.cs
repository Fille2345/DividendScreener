using DividendScreener.Core.MarketData;
using Microsoft.Extensions.Logging;

namespace DividendScreener.Infrastructure.MarketData;

/// <summary>
/// Picks a provider by ticker format and falls back to the other one.
/// Exchange-suffixed tickers (SAN.PA, NDA.ST) go to <paramref name="europeanClient"/> first;
/// plain tickers (KO, AAPL) go to <paramref name="usClient"/> first.
/// </summary>
/// <remarks>
/// Outcomes, primary first:
/// <list type="bullet">
/// <item>Primary returns data → that data.</item>
/// <item>Primary fails or does not know the ticker → the secondary is tried.</item>
/// <item>Neither knows the ticker → null (404).</item>
/// <item>Primary failed and the secondary does not know the ticker → the primary's error.</item>
/// <item>Primary does not know the ticker and the secondary fails → null; the primary is authoritative for its own
/// format, and the secondary's error (typically "not in plan") would only mislead. The error is logged.</item>
/// <item>Both fail → one <see cref="MarketDataException"/> carrying both reasons.</item>
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

        MarketDataException? primaryError = null;
        try
        {
            var snapshot = await primary.GetSnapshotAsync(ticker, cancellationToken);
            if (snapshot is not null)
                return snapshot;
        }
        catch (MarketDataException ex)
        {
            primaryError = ex;
            logger.LogWarning("Primary provider failed for {Ticker}, trying fallback: {Reason}", ticker, ex.Message);
        }

        try
        {
            var snapshot = await secondary.GetSnapshotAsync(ticker, cancellationToken);
            if (snapshot is not null)
                return snapshot;
        }
        catch (MarketDataException ex)
        {
            if (primaryError is null)
            {
                logger.LogInformation("Fallback provider failed for {Ticker} after primary found nothing: {Reason}",
                    ticker, ex.Message);
                return null;
            }

            throw new MarketDataException($"{primaryError.Message} Fallback also failed: {ex.Message}", primaryError);
        }

        if (primaryError is not null)
            throw primaryError;
        return null;
    }

    internal static bool IsExchangeSuffixed(string ticker) => ticker.Contains('.');
}
