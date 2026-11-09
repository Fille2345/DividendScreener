using DividendScreener.Core.MarketData;
using Microsoft.Extensions.Caching.Memory;

namespace DividendScreener.Infrastructure.MarketData;

/// <summary>
/// Caches snapshots per ticker so repeated valuations (e.g. tweaking assumptions in the UI)
/// do not spend the provider's request quota. Unknown tickers (null or <see cref="TickerNotFoundException"/>)
/// are cached too, for a shorter time and with their reason. Other failures are not cached.
/// </summary>
public sealed class CachingMarketDataClient(IMarketDataClient inner, IMemoryCache cache, MarketDataOptions options)
    : IMarketDataClient
{
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromMinutes(5);

    public async Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var key = "market-snapshot:" + ticker.ToUpperInvariant();
        if (cache.TryGetValue(key, out object? cached))
        {
            return cached switch
            {
                TickerNotFoundException notFound => throw new TickerNotFoundException(notFound.Message, notFound),
                MarketSnapshot snapshot => snapshot,
                _ => null,
            };
        }

        try
        {
            var snapshot = await inner.GetSnapshotAsync(ticker, cancellationToken);
            cache.Set(key, snapshot, snapshot is null ? NotFoundTtl : options.CacheDuration);
            return snapshot;
        }
        catch (TickerNotFoundException ex)
        {
            cache.Set(key, ex, NotFoundTtl);
            throw;
        }
    }
}
