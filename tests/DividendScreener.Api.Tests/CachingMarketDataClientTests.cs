using DividendScreener.Core.MarketData;
using DividendScreener.Infrastructure.MarketData;
using Microsoft.Extensions.Caching.Memory;

namespace DividendScreener.Api.Tests;

public class CachingMarketDataClientTests
{
    private readonly CountingClient _inner = new(new SampleMarketDataClient(TimeProvider.System));
    private readonly CachingMarketDataClient _sut;

    public CachingMarketDataClientTests() =>
        _sut = new CachingMarketDataClient(_inner, new MemoryCache(new MemoryCacheOptions()), new MarketDataOptions());

    [Fact]
    public async Task RepeatedLookups_HitProviderOnce_CaseInsensitively()
    {
        var first = await _sut.GetSnapshotAsync("STDX");
        var second = await _sut.GetSnapshotAsync("stdx");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, _inner.Calls);
    }

    [Fact]
    public async Task UnknownTicker_IsCachedAsNull()
    {
        Assert.Null(await _sut.GetSnapshotAsync("NOPE"));
        Assert.Null(await _sut.GetSnapshotAsync("NOPE"));
        Assert.Equal(1, _inner.Calls);
    }

    [Fact]
    public async Task Failures_AreNotCached()
    {
        var failing = new FailingOnceClient();
        var sut = new CachingMarketDataClient(failing, new MemoryCache(new MemoryCacheOptions()), new MarketDataOptions());

        await Assert.ThrowsAsync<MarketDataException>(() => sut.GetSnapshotAsync("STDX"));
        Assert.NotNull(await sut.GetSnapshotAsync("STDX"));
    }

    private sealed class CountingClient(IMarketDataClient inner) : IMarketDataClient
    {
        public int Calls { get; private set; }

        public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.GetSnapshotAsync(ticker, cancellationToken);
        }
    }

    private sealed class FailingOnceClient : IMarketDataClient
    {
        private readonly SampleMarketDataClient _sample = new(TimeProvider.System);
        private bool _failed;

        public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
        {
            if (_failed)
                return _sample.GetSnapshotAsync(ticker, cancellationToken);
            _failed = true;
            throw new MarketDataException("Transient failure.");
        }
    }
}
