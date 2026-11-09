using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DividendScreener.Core.MarketData;
using DividendScreener.Infrastructure.MarketData.Eulerpool;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DividendScreener.Api.Tests;

public class DebugEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Profile_PassesUpstreamStatusAndJsonBodyThrough()
    {
        var upstream = new RecordingHandler(HttpStatusCode.NotFound, """{"error":"Security not found"}""");

        var response = await WithEulerpool(upstream).CreateClient().GetAsync("/api/debug/eulerpool/NDA-SE.ST");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://eulerpool.test/api/1/equity/profile/NDA-SE.ST", json.GetProperty("url").GetString());
        Assert.Equal(404, json.GetProperty("statusCode").GetInt32());
        Assert.True(json.GetProperty("apiKeySent").GetBoolean());
        Assert.Equal("Security not found", json.GetProperty("body").GetProperty("error").GetString());
        Assert.Equal("Bearer debug-key", upstream.Authorization);
    }

    [Fact]
    public async Task NonJsonBody_IsReturnedAsText()
    {
        var upstream = new RecordingHandler(HttpStatusCode.BadGateway, "<html>upstream down</html>");

        var json = await WithEulerpool(upstream).CreateClient()
            .GetFromJsonAsync<JsonElement>("/api/debug/eulerpool/SAN.PA");

        Assert.Equal(502, json.GetProperty("statusCode").GetInt32());
        Assert.Equal("<html>upstream down</html>", json.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData("/api/debug/eulerpool/SE0000108656?endpoint=quotes", "https://eulerpool.test/api/1/equity/quotes/SE0000108656")]
    [InlineData("/api/debug/eulerpool/SE0000108656?endpoint=price-metrics", "https://eulerpool.test/api/1/sentiment/price-metrics/SE0000108656")]
    [InlineData("/api/debug/eulerpool/VOLV%20B.ST", "https://eulerpool.test/api/1/equity/profile/VOLV%20B.ST")]
    public async Task OtherEndpointsAndIdentifiers_AreForwarded(string path, string expectedUrl)
    {
        var upstream = new RecordingHandler(HttpStatusCode.OK, "[]");

        await WithEulerpool(upstream).CreateClient().GetAsync(path);

        Assert.Equal(expectedUrl, upstream.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("/api/debug/eulerpool/SAN.PA?endpoint=executives")]
    [InlineData("/api/debug/eulerpool/SAN%2FPA")]
    public async Task InvalidInput_Returns400_WithoutCallingEulerpool(string path)
    {
        var upstream = new RecordingHandler(HttpStatusCode.OK, "{}");

        var response = await WithEulerpool(upstream).CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(upstream.RequestUri);
    }

    [Fact]
    public async Task OutsideDevelopment_EndpointDoesNotExist()
    {
        var client = WithEulerpool(new RecordingHandler(HttpStatusCode.OK, "{}"))
            .WithWebHostBuilder(b => b.UseEnvironment("Production"))
            .CreateClient();

        var response = await client.GetAsync("/api/debug/eulerpool/SAN.PA");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ValuationOfUnknownTicker_Returns404_WithProviderReasons()
    {
        const string reason =
            "Eulerpool does not recognise 'NDA-SE.ST' (HTTP 404 from equity/profile/NDA-SE.ST: Security not found). " +
            "Fallback: Your Financial Modeling Prep plan does not include data for NDA-SE.ST (HTTP 402 from profile?symbol=NDA-SE.ST).";
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IMarketDataClient>(new NotFoundClient(reason))))).CreateClient();

        var response = await client.GetAsync("/api/valuations/NDA-SE.ST");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(reason, problem.GetProperty("detail").GetString());
    }

    private WebApplicationFactory<Program> WithEulerpool(HttpMessageHandler upstream) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton(new EulerpoolDiagnostics(
                new SingleHandlerFactory(upstream), new EulerpoolOptions { ApiKey = "debug-key" })))));

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, body.StartsWith('<') ? "text/html" : "application/json"),
            });
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://eulerpool.test/api/1/") };
    }

    private sealed class NotFoundClient(string reason) : IMarketDataClient
    {
        public Task<MarketSnapshot?> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default) =>
            throw new TickerNotFoundException(reason);
    }
}
