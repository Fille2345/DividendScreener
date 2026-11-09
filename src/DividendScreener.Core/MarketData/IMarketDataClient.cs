using DividendScreener.Core.Models;

namespace DividendScreener.Core.MarketData;

/// <summary>
/// Source of live inputs for a valuation. Implementations wrap a specific data provider;
/// the rest of the system only depends on this interface.
/// </summary>
public interface IMarketDataClient
{
    /// <summary>
    /// Fetches the current price, beta, latest annual free cash flow and dividends paid,
    /// and up to ten years of annual dividends per share.
    /// </summary>
    /// <returns>The snapshot, or null when the provider does not know the ticker.</returns>
    /// <exception cref="MarketDataException">The provider failed or returned unusable data.</exception>
    Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default);
}

/// <summary>Everything the valuation needs about one stock, as reported by a provider at one point in time.</summary>
public sealed record MarketSnapshot(Stock Stock, FinancialMetrics Metrics, string Source, DateTimeOffset RetrievedAt);

/// <summary>A data provider was unreachable, rejected the request, or returned data that cannot be used.</summary>
public sealed class MarketDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);
