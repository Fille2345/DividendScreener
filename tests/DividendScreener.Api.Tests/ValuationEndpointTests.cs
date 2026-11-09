using System.Net;
using System.Net.Http.Json;
using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DividendScreener.Api.Tests;

public class ValuationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ValuationEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task SampleTicker_ReturnsValuationWithDefaultAssumptions()
    {
        var response = await _factory.CreateClient().GetAsync("/api/valuations/stdx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StockValuationResponse>();
        Assert.NotNull(body);
        Assert.Equal("STDX", body.Valuation.Ticker);
        Assert.Contains("Sample", body.Source);
        // Defaults from appsettings.json: Rf 2.7%, Rm 8%, sample beta 0.85 → 0.027 + 0.85 × 0.053
        Assert.Equal(0.07205m, body.Valuation.RequiredReturn);
        Assert.NotNull(body.Valuation.IntrinsicValue);
    }

    [Fact]
    public async Task QueryOverrides_AreAppliedToTheValuation()
    {
        var response = await _factory.CreateClient()
            .GetAsync("/api/valuations/STDX?riskFreeRate=0.03&marketReturn=0.09&maxGrowthRate=0.02&costBasis=40");

        var body = await response.Content.ReadFromJsonAsync<StockValuationResponse>();
        Assert.NotNull(body);
        Assert.Equal(0.03m, body.Market.RiskFreeRate);
        Assert.Equal(0.081m, body.Valuation.RequiredReturn); // 0.03 + 0.85 × 0.06
        Assert.Equal(0.02m, body.Valuation.GrowthRate);
        Assert.NotNull(body.Valuation.YieldOnCost);
    }

    [Fact]
    public async Task UnknownTicker_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/valuations/NOPE.PA");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/valuations/BAD%20TICKER")]
    [InlineData("/api/valuations/STDX?costBasis=0")]
    [InlineData("/api/valuations/STDX?riskFreeRate=3")] // percent instead of fraction
    [InlineData("/api/valuations/STDX?marketReturn=-1")]
    public async Task InvalidInput_Returns400(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProviderFailure_Returns502()
    {
        var client = WithMarketData(new ThrowingMarketDataClient()).CreateClient();

        var response = await client.GetAsync("/api/valuations/SAN.PA");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task SnapshotWithoutDividends_Returns422()
    {
        var snapshot = new MarketSnapshot(
            new Stock { Ticker = "NODIV", Name = "No Dividend", Isin = "X", Exchange = "X", Currency = "EUR", CurrentPrice = 10m },
            new FinancialMetrics { Beta = 1m, FreeCashFlow = 1m, DividendsPaid = 0m, DividendHistory = [] },
            "Test", DateTimeOffset.UnixEpoch);
        var client = WithMarketData(new FixedMarketDataClient(snapshot)).CreateClient();

        var response = await client.GetAsync("/api/valuations/NODIV");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private WebApplicationFactory<Program> WithMarketData(IMarketDataClient client) =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton(client))));

    private sealed class ThrowingMarketDataClient : IMarketDataClient
    {
        public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
            => throw new MarketDataException("Provider is down.");
    }

    private sealed class FixedMarketDataClient(MarketSnapshot snapshot) : IMarketDataClient
    {
        public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)
            => Task.FromResult<MarketSnapshot?>(snapshot);
    }
}
