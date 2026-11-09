using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;

namespace DividendScreener.Infrastructure.MarketData;

/// <summary>
/// Offline provider with one fictitious company, for development and demos without an API key.
/// Every response is tagged with <see cref="SourceName"/> so it can never pass for real market data.
/// </summary>
public sealed class SampleMarketDataClient(TimeProvider timeProvider) : IMarketDataClient
{
    public const string SourceName = "Sample data (fictitious)";
    public const string SampleTicker = "STDX";

    private static readonly Stock SampleStock = new()
    {
        Ticker = SampleTicker,
        Name = "Standard Dividend SA (sample)",
        Isin = "XX0000000000",
        Exchange = "Sample Exchange",
        Currency = "EUR",
        CurrentPrice = 50.00m,
        Sector = "Consumer Staples",
    };

    // Dividend grows 4% a year from 2.00 in 2014, matching the unit-test fixture.
    private static readonly FinancialMetrics SampleMetrics = new()
    {
        Beta = 0.85m,
        FreeCashFlow = 8_000_000_000m,
        DividendsPaid = -4_400_000_000m,
        DividendHistory = Enumerable.Range(0, 11)
            .Select(i => new AnnualDividend(2014 + i, decimal.Round(2m * Pow(1.04m, i), 4)))
            .ToArray(),
    };

    public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var snapshot = string.Equals(ticker, SampleTicker, StringComparison.OrdinalIgnoreCase)
            ? new MarketSnapshot(SampleStock, SampleMetrics, SourceName, timeProvider.GetUtcNow())
            : null;

        return Task.FromResult(snapshot);
    }

    private static decimal Pow(decimal x, int n)
    {
        var result = 1m;
        for (var i = 0; i < n; i++)
            result *= x;
        return result;
    }
}
