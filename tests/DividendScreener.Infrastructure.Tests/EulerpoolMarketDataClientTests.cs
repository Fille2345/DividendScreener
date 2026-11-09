using System.Net;
using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using DividendScreener.Infrastructure.MarketData.Eulerpool;
using Microsoft.Extensions.Logging.Abstractions;

namespace DividendScreener.Infrastructure.Tests;

/// <summary>
/// Canned responses copy the shapes of real Eulerpool responses (profile without a ticker field, price metrics with
/// string-typed fields, statements in millions with an unfinished latest year). The company and figures are invented.
/// </summary>
public class EulerpoolMarketDataClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private const string Isin = "FR0000000001";

    private const string ProfileJson = """
        {"isin":"FR0000000001","name":"Exemple Dividendes SA","logo":"/api/logo/isin/FR0000000001","country":"FR",
         "currency":"EUR","employees":90000,"sector":"Health Care","industry":"Pharmaceuticals","ipo":"2002-07-01",
         "mcap":102196.29,"shares":1220.4,"website":"https://example.com","wkn":"000001","description":"Fictitious."}
        """;

    private const string QuotesJson = """
        [{"timestamp":1762560000000,"price":86.10},{"timestamp":1762732800000,"price":87.55},{"timestamp":1762646400000,"price":86.90}]
        """;

    private const string PriceMetricsJson = """
        {"symbol":"EXD.PA","beta":0.62,"ten_day_avg_volume":"3.32","three_month_avg_volume":"2.28",
         "week_52_high":"101.90","week_52_low":"74.85","week_52_high_date":"2025-04-03","week_52_low_date":"2026-03-09",
         "market_cap":"97699125000","price_relative_to_s_and_p_52w":"-18.3367","last_updated":"2026-03-29T12:18:51.559Z"}
        """;

    // 2025 is the unfinished row Eulerpool publishes early: capex and dividends still 0, fcf = operating cash flow.
    private const string CashFlowJson = """
        [
          {"ticker":"EXD.PA","period":"2023-12-31","netOperatingCashFlow":10258,"capex":-2906,"fcf":7352,"cashDividendsPaid":-4454,"year":2023},
          {"ticker":"EXD.PA","period":"2024-12-31","netOperatingCashFlow":9081,"capex":-3195,"fcf":5886,"cashDividendsPaid":-4742,"year":2024},
          {"ticker":"EXD.PA","period":"2025-12-31","netOperatingCashFlow":10917,"capex":0,"fcf":10917,"cashDividendsPaid":0,"year":2025}
        ]
        """;

    // Period is the ex-date. The 2023 ex-date falls in May but is paid in June; 2026 is the current year.
    private const string DividendsJson = """
        [
          {"payDate":"2013-05-15","period":"2013-05-13","dividend":2.77},
          {"payDate":"2014-05-14","period":"2014-05-12","dividend":2.8},
          {"payDate":"2015-05-13","period":"2015-05-11","dividend":2.85},
          {"payDate":"2016-05-11","period":"2016-05-09","dividend":2.93},
          {"payDate":"2017-05-16","period":"2017-05-12","dividend":2.96},
          {"payDate":"2018-05-16","period":"2018-05-14","dividend":3.03},
          {"payDate":"2019-05-13","period":"2019-05-09","dividend":3.07},
          {"payDate":"2020-05-06","period":"2020-05-04","dividend":3.15},
          {"payDate":"2021-05-07","period":"2021-05-05","dividend":3.2},
          {"payDate":"2022-05-10","period":"2022-05-06","dividend":3.3126307},
          {"payDate":"2023-06-01","period":"2023-05-30","dividend":3.56},
          {"payDate":"2024-05-15","period":"2024-05-13","dividend":3.76},
          {"payDate":"2025-05-14","period":"2025-05-12","dividend":3.92},
          {"payDate":"2026-05-07","period":"2026-05-05","dividend":4.12}
        ]
        """;

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Json("equity/profile", ProfileJson)
        .Json("equity/quotes", QuotesJson)
        .Json("sentiment/price-metrics", PriceMetricsJson)
        .Json("equity/cashflowstatement", CashFlowJson)
        .Json("equity/dividends", DividendsJson);

    private EulerpoolMarketDataClient CreateClient(string? apiKey = "test-key") => new(
        new StubHttpClientFactory(_http, "https://eulerpool.test/api/1/"),
        new EulerpoolOptions { ApiKey = apiKey },
        new FixedTimeProvider(Now),
        NullLogger<EulerpoolMarketDataClient>.Instance);

    // ---------- Happy path ----------

    [Fact]
    public async Task Snapshot_MapsProfilePriceAndBeta()
    {
        var snapshot = await CreateClient().GetSnapshotAsync("exd.pa");

        Assert.NotNull(snapshot);
        Assert.Equal("Eulerpool", snapshot.Source);
        Assert.Equal(Now, snapshot.RetrievedAt);
        Assert.Equal("EXD.PA", snapshot.Stock.Ticker);
        Assert.Equal("Exemple Dividendes SA", snapshot.Stock.Name);
        Assert.Equal(Isin, snapshot.Stock.Isin);
        Assert.Equal("Euronext Paris", snapshot.Stock.Exchange);
        Assert.Equal("EUR", snapshot.Stock.Currency);
        Assert.Equal("Health Care", snapshot.Stock.Sector);
        Assert.Equal(87.55m, snapshot.Stock.CurrentPrice); // latest timestamp, though not last in the array
        Assert.Equal(0.62m, snapshot.Metrics.Beta);
    }

    [Fact]
    public async Task Snapshot_SkipsUnfinishedLatestStatement_AndScalesMillions()
    {
        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Equal(5_886_000_000m, snapshot!.Metrics.FreeCashFlow);
        Assert.Equal(-4_742_000_000m, snapshot.Metrics.DividendsPaid);
    }

    [Fact]
    public async Task Snapshot_BuildsHistoryByExDividendYear()
    {
        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        AnnualDividend[] expected =
        [
            new(2015, 2.85m), new(2016, 2.93m), new(2017, 2.96m), new(2018, 3.03m), new(2019, 3.07m),
            new(2020, 3.15m), new(2021, 3.2m), new(2022, 3.3126307m), new(2023, 3.56m), new(2024, 3.76m), new(2025, 3.92m),
        ];
        Assert.Equal(expected, snapshot!.Metrics.DividendHistory);
    }

    [Fact]
    public async Task Requests_ResolveTickerOnce_ThenUseIsin_WithBearerKey()
    {
        await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Equal(5, _http.Requests.Count);
        Assert.Equal("/api/1/equity/profile/EXD.PA", _http.Requests[0].AbsolutePath);
        Assert.All(_http.Requests.Skip(1), uri => Assert.EndsWith("/" + Isin, uri.AbsolutePath));
        Assert.All(_http.Requests, uri => Assert.DoesNotContain("test-key", uri.ToString()));
        Assert.All(_http.AuthorizationHeaders, h => Assert.Equal("Bearer test-key", h));
    }

    [Theory]
    [InlineData("SAN.PA", "Euronext Paris")]
    [InlineData("NDA-SE.ST", "Nasdaq Stockholm")]
    [InlineData("ASML.AS", "Euronext Amsterdam")]
    [InlineData("XYZ.QQ", "QQ")]
    [InlineData("KO", "")]
    public void ExchangeName_FromTickerSuffix(string ticker, string expected)
    {
        Assert.Equal(expected, EulerpoolMarketDataClient.ExchangeName(ticker));
    }

    // ---------- Statement selection ----------

    [Fact]
    public void LatestCompleteStatement_ComputesNothing_WhenEveryRowIsUnfinished()
    {
        EulerpoolCashFlowRow[] rows = [new("2025-12-31", 2025, 100, 0, 100, 0)];

        Assert.Null(EulerpoolMarketDataClient.LatestCompleteStatement(rows));
    }

    [Fact]
    public async Task MissingFcf_IsDerivedFromOperatingCashFlowAndCapex()
    {
        _http.Json("equity/cashflowstatement", """[{"period":"2024-12-31","year":2024,"netOperatingCashFlow":900,"capex":-300,"fcf":null,"cashDividendsPaid":-250}]""");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Equal(600_000_000m, snapshot!.Metrics.FreeCashFlow);
        Assert.Equal(-250_000_000m, snapshot.Metrics.DividendsPaid);
    }

    [Fact]
    public async Task NoStatements_LeavesCashFlowFieldsNull()
    {
        _http.Json("equity/cashflowstatement", "[]");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Null(snapshot!.Metrics.FreeCashFlow);
        Assert.Null(snapshot.Metrics.DividendsPaid);
    }

    [Fact]
    public async Task StatementsOutsidePlan_AreTreatedAsMissing()
    {
        _http.Status("equity/cashflowstatement", HttpStatusCode.Forbidden, """{"error":"Insufficient access rights"}""");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Metrics.FreeCashFlow);
    }

    // ---------- Missing data ----------

    [Fact]
    public async Task UnknownSecurity_Returns404Null_WithoutFurtherRequests()
    {
        _http.Status("equity/profile", HttpStatusCode.NotFound, """{"error":"Security not found"}""");

        Assert.Null(await CreateClient().GetSnapshotAsync("NOPE.PA"));
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task ProfileWithoutIsin_IsTreatedAsUnknown()
    {
        _http.Json("equity/profile", """{"name":"Something"}""");

        Assert.Null(await CreateClient().GetSnapshotAsync("ODD.PA"));
    }

    [Theory]
    [InlineData("equity/quotes", "[]", "no recent price")]
    [InlineData("equity/quotes", """[{"timestamp":1762732800000,"price":null}]""", "no recent price")]
    [InlineData("sentiment/price-metrics", """{"symbol":"EXD.PA","beta":null}""", "no beta")]
    [InlineData("sentiment/price-metrics", """{"symbol":"EXD.PA","beta":0}""", "no beta")]
    [InlineData("equity/profile", """{"isin":"FR0000000001","name":"X","currency":null}""", "no currency")]
    public async Task MissingRequiredField_Throws(string endpoint, string json, string expectedMessage)
    {
        _http.Json(endpoint, json);

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task NoDividends_HasEmptyHistory()
    {
        _http.Json("equity/dividends", "[]");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Empty(snapshot!.Metrics.DividendHistory);
    }

    // ---------- Errors and rate limits ----------

    [Fact]
    public async Task MissingApiKey_Throws_WithoutCallingEulerpool()
    {
        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient(apiKey: "").GetSnapshotAsync("EXD.PA"));

        Assert.Contains("EULERPOOL_API_KEY", ex.Message);
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData("equity/profile", HttpStatusCode.Unauthorized, """{"error":"Missing or invalid API key. Pass ?token=YOUR_KEY or Authorization: Bearer YOUR_KEY."}""", "rejected the API key")]
    [InlineData("equity/profile", HttpStatusCode.TooManyRequests, "", "rate limit")]
    [InlineData("equity/quotes", HttpStatusCode.TooManyRequests, "", "rate limit")]
    [InlineData("equity/dividends", HttpStatusCode.Forbidden, """{"error":"Insufficient access rights"}""", "plan does not include equity/dividends data for EXD.PA")]
    [InlineData("equity/quotes", HttpStatusCode.PaymentRequired, "", "plan does not include equity/quotes data for EXD.PA")]
    [InlineData("equity/profile", HttpStatusCode.InternalServerError, """{"error":"Database unavailable"}""", "HTTP 500 for equity/profile: Database unavailable")]
    [InlineData("sentiment/price-metrics", HttpStatusCode.OK, """{"error":"Unexpected failure"}""", "Unexpected failure")]
    [InlineData("equity/profile", HttpStatusCode.OK, "<html>not json</html>", "malformed")]
    [InlineData("equity/quotes", HttpStatusCode.OK, """{"not":"an array"}""", "malformed")]
    public async Task Errors_BecomeMarketDataException(string endpoint, HttpStatusCode status, string body, string expectedMessage)
    {
        _http.Status(endpoint, status, body);

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task NetworkFailure_Throws()
    {
        _http.Throw("equity/profile", new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains("Could not reach Eulerpool", ex.Message);
    }

    [Fact]
    public async Task Timeout_Throws()
    {
        _http.Throw("equity/profile", new TaskCanceledException("timed out"));

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains("did not respond in time", ex.Message);
    }
}
