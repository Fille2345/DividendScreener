using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using DividendScreener.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;

namespace DividendScreener.Infrastructure.Tests;

public class RoutingMarketDataClientTests
{
    private readonly ScriptedClient _us = new("US");
    private readonly ScriptedClient _eu = new("EU");
    private readonly RoutingMarketDataClient _sut;

    public RoutingMarketDataClientTests() =>
        _sut = new RoutingMarketDataClient(_us, _eu, NullLogger<RoutingMarketDataClient>.Instance);

    // ---------- Routing ----------

    [Theory]
    [InlineData("SAN.PA")]
    [InlineData("BN.PA")]
    [InlineData("NDA.SE")]
    [InlineData("NDA-SE.ST")]
    [InlineData("san.pa")]
    public async Task SuffixedTicker_GoesToEuropeanProviderOnly(string ticker)
    {
        var snapshot = await _sut.GetSnapshotAsync(ticker);

        Assert.Equal("EU", snapshot!.Source);
        Assert.Equal([ticker], _eu.Calls);
        Assert.Empty(_us.Calls);
    }

    [Theory]
    [InlineData("KO")]
    [InlineData("AAPL")]
    [InlineData("FR0000120578")] // a bare ISIN has no dot either
    public async Task PlainTicker_GoesToUsProviderOnly(string ticker)
    {
        var snapshot = await _sut.GetSnapshotAsync(ticker);

        Assert.Equal("US", snapshot!.Source);
        Assert.Equal([ticker], _us.Calls);
        Assert.Empty(_eu.Calls);
    }

    // ---------- Fallback ----------

    [Fact]
    public async Task PrimaryThrows_FallsBackToSecondary()
    {
        _us.Throws("Your Financial Modeling Prep plan does not include data for KO.");

        var snapshot = await _sut.GetSnapshotAsync("KO");

        Assert.Equal("EU", snapshot!.Source);
        Assert.Single(_us.Calls);
        Assert.Single(_eu.Calls);
    }

    [Fact]
    public async Task PrimaryNotFound_FallsBackToSecondary()
    {
        _eu.ReturnsNull(); // e.g. BRK.B is routed to the European provider, which does not know it

        var snapshot = await _sut.GetSnapshotAsync("BRK.B");

        Assert.Equal("US", snapshot!.Source);
    }

    [Fact]
    public async Task NeitherKnowsTicker_Throws404_WithBothReasons()
    {
        _eu.ThrowsNotFound("Eulerpool does not recognise 'NOPE.PA' (HTTP 404 from equity/profile/NOPE.PA: Security not found).");
        _us.ReturnsNull();

        var ex = await Assert.ThrowsAsync<TickerNotFoundException>(() => _sut.GetSnapshotAsync("NOPE.PA"));

        Assert.Equal(
            "Eulerpool does not recognise 'NOPE.PA' (HTTP 404 from equity/profile/NOPE.PA: Security not found). " +
            "Fallback: ScriptedClient has no data for 'NOPE.PA'.",
            ex.Message);
        Assert.Single(_eu.Calls);
        Assert.Single(_us.Calls);
    }

    [Fact]
    public async Task PrimaryNotFound_SecondaryFails_Throws404_ThatStillShowsTheFallbackError()
    {
        // The case behind "No market data found" for NDA-SE.ST: both reasons must now reach the caller.
        var primary = _eu.ThrowsNotFound("Eulerpool does not recognise 'NDA-SE.ST' (HTTP 404 from equity/profile/NDA-SE.ST: Security not found).");
        _us.Throws("Your Financial Modeling Prep plan does not include data for NDA-SE.ST (HTTP 402 from profile?symbol=NDA-SE.ST).");

        var ex = await Assert.ThrowsAsync<TickerNotFoundException>(() => _sut.GetSnapshotAsync("NDA-SE.ST"));

        Assert.Contains("HTTP 404 from equity/profile/NDA-SE.ST: Security not found", ex.Message);
        Assert.Contains("Fallback: Your Financial Modeling Prep plan does not include data for NDA-SE.ST (HTTP 402", ex.Message);
        Assert.Same(primary, ex.InnerException);
    }

    [Fact]
    public async Task PrimaryFails_SecondaryNotFound_Throws502_WithBothReasons()
    {
        var primary = _eu.Throws("Eulerpool rate limit reached (HTTP 429 from equity/quotes/SE0000108656).");
        _us.ThrowsNotFound("Financial Modeling Prep does not recognise 'ERIC-B.ST' (empty or 404 response from profile?symbol=ERIC-B.ST).");

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => _sut.GetSnapshotAsync("ERIC-B.ST"));

        Assert.IsNotType<TickerNotFoundException>(ex);
        Assert.Contains("HTTP 429 from equity/quotes/SE0000108656", ex.Message);
        Assert.Contains("Fallback: Financial Modeling Prep does not recognise 'ERIC-B.ST'", ex.Message);
        Assert.Same(primary, ex.InnerException);
    }

    [Fact]
    public async Task BothFail_Throws502_WithBothReasons()
    {
        var primaryError = _eu.Throws("Eulerpool rejected the API key (HTTP 401 from equity/profile/SAN.PA).");
        _us.Throws("Financial Modeling Prep rate limit reached (HTTP 429 from profile?symbol=SAN.PA).");

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => _sut.GetSnapshotAsync("SAN.PA"));

        Assert.IsNotType<TickerNotFoundException>(ex);
        Assert.Equal(
            "Eulerpool rejected the API key (HTTP 401 from equity/profile/SAN.PA). " +
            "Fallback: Financial Modeling Prep rate limit reached (HTTP 429 from profile?symbol=SAN.PA).",
            ex.Message);
        Assert.Same(primaryError, ex.InnerException);
    }

    [Fact]
    public async Task Cancellation_IsNotTreatedAsProviderFailure()
    {
        _eu.ThrowsException(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.GetSnapshotAsync("SAN.PA"));
        Assert.Empty(_us.Calls);
    }

    [Fact]
    public async Task UnexpectedException_IsNotSwallowed()
    {
        _us.ThrowsException(new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetSnapshotAsync("KO"));
        Assert.Empty(_eu.Calls);
    }

    /// <summary>Returns a snapshot tagged with its name by default; can be scripted to return null or throw.</summary>
    private sealed class ScriptedClient(string name) : IMarketDataClient
    {
        private Func<string, MarketSnapshot?> _behavior = ticker => Snapshot(ticker, name);

        public List<string> Calls { get; } = [];

        public void ReturnsNull() => _behavior = _ => null;

        public MarketDataException Throws(string message)
        {
            var ex = new MarketDataException(message);
            _behavior = _ => throw ex;
            return ex;
        }

        public TickerNotFoundException ThrowsNotFound(string message)
        {
            var ex = new TickerNotFoundException(message);
            _behavior = _ => throw ex;
            return ex;
        }

        public void ThrowsException(Exception ex) => _behavior = _ => throw ex;

        public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
        {
            Calls.Add(ticker);
            return Task.FromResult(_behavior(ticker));
        }

        private static MarketSnapshot Snapshot(string ticker, string source) => new(
            new Stock { Ticker = ticker, Name = ticker, Isin = "", Exchange = "", Currency = "EUR", CurrentPrice = 10m },
            new FinancialMetrics { Beta = 1m, DividendHistory = [new AnnualDividend(2025, 1m)] },
            source,
            DateTimeOffset.UnixEpoch);
    }
}
