using System.Net;
using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using DividendScreener.Infrastructure.MarketData.Fmp;
using Microsoft.Extensions.Logging.Abstractions;

namespace DividendScreener.Infrastructure.Tests;

/// <summary>
/// Parses canned responses in FMP's /stable format. The company and figures are invented;
/// only the JSON shapes mirror FMP's.
/// </summary>
public class FmpMarketDataClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string ProfileJson = """
        [{
          "symbol": "EXD.PA", "price": 88.42, "marketCap": 110000000000, "beta": 0.48,
          "lastDividend": 4.1, "range": "80.1-105.3", "change": -0.4, "changePercentage": -0.45,
          "volume": 1500000, "averageVolume": 2000000, "companyName": "Exemple Dividendes SA",
          "currency": "EUR", "cik": null, "isin": "FR0000000001", "cusip": null,
          "exchangeFullName": "Euronext Paris", "exchange": "EURONEXT", "industry": "Drug Manufacturers - General",
          "website": "https://example.com", "description": "Fictitious company.", "ceo": "",
          "sector": "Healthcare", "country": "FR", "isEtf": false, "isActivelyTrading": true
        }]
        """;

    // Two statements, deliberately out of order; numbers include an exponent form FMP sometimes emits.
    private const string CashFlowJson = """
        [
          {
            "date": "2024-12-31", "symbol": "EXD.PA", "reportedCurrency": "EUR", "fiscalYear": "2024", "period": "FY",
            "operatingCashFlow": 10000000000, "capitalExpenditure": -2000000000, "freeCashFlow": 8000000000,
            "commonDividendsPaid": -4600000000, "preferredDividendsPaid": 0, "netDividendsPaid": -4600000000
          },
          {
            "date": "2025-12-31", "symbol": "EXD.PA", "reportedCurrency": "EUR", "fiscalYear": "2025", "period": "FY",
            "operatingCashFlow": 1.1E10, "capitalExpenditure": -2.5E9, "freeCashFlow": 8.5E9,
            "commonDividendsPaid": -4.8E9, "preferredDividendsPaid": 0, "netDividendsPaid": -4.8E9
          }
        ]
        """;

    // Annual May payments 2013–2026, with 2020 suspended. 2026 is the current, incomplete year.
    private const string DividendsJson = """
        [
          {"symbol":"EXD.PA","date":"2026-05-06","recordDate":"2026-05-07","paymentDate":"2026-05-08","declarationDate":"","adjDividend":4.1,"dividend":4.1,"yield":4.6,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2025-05-07","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.92,"dividend":3.92,"yield":4.4,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2024-05-08","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.76,"dividend":3.76,"yield":4.1,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2023-05-09","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.56,"dividend":3.56,"yield":3.8,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2022-05-10","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.33,"dividend":3.33,"yield":3.6,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2021-05-05","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.2,"dividend":3.2,"yield":3.9,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2019-05-06","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.07,"dividend":3.07,"yield":4.0,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2018-05-14","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":3.03,"dividend":3.03,"yield":4.2,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2017-05-12","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":2.96,"dividend":2.96,"yield":3.5,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2016-05-09","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":2.93,"dividend":2.93,"yield":3.7,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2015-05-08","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":2.85,"dividend":2.85,"yield":3.4,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2014-05-12","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":2.8,"dividend":2.8,"yield":3.9,"frequency":"Annual"},
          {"symbol":"EXD.PA","date":"2013-05-13","recordDate":"","paymentDate":"","declarationDate":"","adjDividend":2.77,"dividend":2.77,"yield":4.0,"frequency":"Annual"}
        ]
        """;

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Json("profile", ProfileJson)
        .Json("cash-flow-statement", CashFlowJson)
        .Json("dividends", DividendsJson);

    private FmpMarketDataClient CreateClient(string? apiKey = "test-key") => new(
        new StubHttpClientFactory(_http),
        new FmpOptions { ApiKey = apiKey },
        new FixedTimeProvider(Now),
        NullLogger<FmpMarketDataClient>.Instance);

    // ---------- Happy path ----------

    [Fact]
    public async Task Snapshot_MapsProfile()
    {
        var snapshot = await CreateClient().GetSnapshotAsync("exd.pa");

        Assert.NotNull(snapshot);
        Assert.Equal("Financial Modeling Prep", snapshot.Source);
        Assert.Equal(Now, snapshot.RetrievedAt);
        Assert.Equal("EXD.PA", snapshot.Stock.Ticker);
        Assert.Equal("Exemple Dividendes SA", snapshot.Stock.Name);
        Assert.Equal("FR0000000001", snapshot.Stock.Isin);
        Assert.Equal("Euronext Paris", snapshot.Stock.Exchange);
        Assert.Equal("EUR", snapshot.Stock.Currency);
        Assert.Equal("Healthcare", snapshot.Stock.Sector);
        Assert.Equal(88.42m, snapshot.Stock.CurrentPrice);
        Assert.Equal(0.48m, snapshot.Metrics.Beta);
    }

    [Fact]
    public async Task Snapshot_UsesMostRecentCashFlowStatement_IncludingExponentNumbers()
    {
        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Equal(8_500_000_000m, snapshot!.Metrics.FreeCashFlow);
        Assert.Equal(-4_800_000_000m, snapshot.Metrics.DividendsPaid);
    }

    [Fact]
    public async Task Snapshot_BuildsElevenCompleteYears_WithSuspendedYearAsZero()
    {
        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        AnnualDividend[] expected =
        [
            new(2015, 2.85m), new(2016, 2.93m), new(2017, 2.96m), new(2018, 3.03m), new(2019, 3.07m),
            new(2020, 0m), new(2021, 3.2m), new(2022, 3.33m), new(2023, 3.56m), new(2024, 3.76m), new(2025, 3.92m),
        ];
        Assert.Equal(expected, snapshot!.Metrics.DividendHistory);
    }

    [Fact]
    public async Task Requests_SendKeyAndEncodedSymbol_ToTheThreeEndpoints()
    {
        await CreateClient("k&y").GetSnapshotAsync("EXD.PA");

        Assert.Equal(3, _http.Requests.Count);
        Assert.All(_http.Requests, uri =>
        {
            Assert.Contains("symbol=EXD.PA", uri.Query);
            Assert.Contains("apikey=k%26y", uri.Query);
        });
        Assert.Contains(_http.Requests, u => u.AbsolutePath == "/stable/cash-flow-statement" && u.Query.Contains("period=annual"));
        Assert.Contains(_http.Requests, u => u.AbsolutePath == "/stable/dividends");
    }

    // ---------- Dividend aggregation ----------

    [Fact]
    public void AnnualHistory_SumsQuarterlyPayments_AndPrefersAdjustedAmount()
    {
        FmpDividend[] payments =
        [
            new("2024-02-10", 1.0m, 0.25m), // adjusted for a 4:1 split
            new("2024-05-10", 0.25m, null),
            new("2024-08-10", 0.25m, 0.25m),
            new("2024-11-10", 0.30m, 0.30m),
            new("2023-11-10", 0.20m, 0.20m),
            new("2023-08-10", 0.20m, 0.20m),
        ];

        var history = FmpMarketDataClient.ToAnnualHistory(payments, currentYear: 2026);

        // 2023 is the earliest year in the data and may be partial, so it is dropped. 2025 has no payments yet.
        Assert.Equal([new AnnualDividend(2024, 1.05m), new AnnualDividend(2025, 0m)], history);
    }

    [Fact]
    public void AnnualHistory_IgnoresBadRows()
    {
        FmpDividend[] payments =
        [
            new("2023-05-01", 1m, 1m),
            new("2024-05-01", 2m, 2m),
            new("not-a-date", 9m, 9m),
            new(null, 9m, 9m),
            new("2024-06-01", null, null),
            new("2024-07-01", -1m, -1m),
            new("2025-05-01", 3m, 3m),
        ];

        var history = FmpMarketDataClient.ToAnnualHistory(payments, currentYear: 2026);

        Assert.Equal([new AnnualDividend(2024, 2m), new AnnualDividend(2025, 3m)], history);
    }

    [Fact]
    public void AnnualHistory_NoPayments_IsEmpty()
    {
        Assert.Empty(FmpMarketDataClient.ToAnnualHistory([], 2026));
    }

    [Fact]
    public async Task Snapshot_NoDividends_HasEmptyHistory()
    {
        _http.Json("dividends", "[]");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Empty(snapshot!.Metrics.DividendHistory);
    }

    // ---------- Missing data ----------

    [Fact]
    public async Task UnknownSymbol_EmptyProfile_ReturnsNull_WithoutFurtherRequests()
    {
        _http.Json("profile", "[]");

        Assert.Null(await CreateClient().GetSnapshotAsync("NOPE.PA"));
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task UnknownSymbol_Http404_ReturnsNull()
    {
        _http.Status("profile", HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().GetSnapshotAsync("NOPE.PA"));
    }

    [Theory]
    [InlineData("\"beta\": 0.48", "\"beta\": null", "no beta")]
    [InlineData("\"beta\": 0.48", "\"beta\": 0", "no beta")]
    [InlineData("\"price\": 88.42", "\"price\": null", "no current price")]
    [InlineData("\"currency\": \"EUR\"", "\"currency\": null", "no trading currency")]
    public async Task MissingRequiredProfileField_Throws(string original, string replacement, string expectedMessage)
    {
        _http.Json("profile", ProfileJson.Replace(original, replacement));

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task NoCashFlowStatement_LeavesCashFlowFieldsNull()
    {
        _http.Json("cash-flow-statement", "[]");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Null(snapshot!.Metrics.FreeCashFlow);
        Assert.Null(snapshot.Metrics.DividendsPaid);
    }

    [Fact]
    public async Task CashFlowNotInPlan_IsTreatedAsMissing()
    {
        _http.Status("cash-flow-statement", HttpStatusCode.PaymentRequired, "Premium Query Parameter");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Metrics.FreeCashFlow);
    }

    [Fact]
    public async Task CashFlow_FallsBackToNetDividends_AndComputedFcf()
    {
        _http.Json("cash-flow-statement", """
            [{ "date": "2025-12-31", "operatingCashFlow": 900, "capitalExpenditure": -300,
               "commonDividendsPaid": 0, "netDividendsPaid": -250 }]
            """);

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Equal(600m, snapshot!.Metrics.FreeCashFlow);
        Assert.Equal(-250m, snapshot.Metrics.DividendsPaid);
    }

    [Fact]
    public async Task CashFlow_LegacyDividendsPaidField_IsRead()
    {
        _http.Json("cash-flow-statement", """[{ "date": "2025-12-31", "freeCashFlow": 1000, "dividendsPaid": -400 }]""");

        var snapshot = await CreateClient().GetSnapshotAsync("EXD.PA");

        Assert.Equal(-400m, snapshot!.Metrics.DividendsPaid);
    }

    // ---------- Errors and rate limits ----------

    [Fact]
    public async Task MissingApiKey_Throws_WithoutCallingFmp()
    {
        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient(apiKey: " ").GetSnapshotAsync("EXD.PA"));

        Assert.Contains("FMP_API_KEY", ex.Message);
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "", "rate limit")]
    [InlineData(HttpStatusCode.OK, """{"Error Message": "Limit Reach . Please upgrade your plan or visit our documentation for more details."}""", "rate limit")]
    [InlineData(HttpStatusCode.Unauthorized, "", "rejected the API key")]
    [InlineData(HttpStatusCode.Forbidden, "", "rejected the API key")]
    [InlineData(HttpStatusCode.OK, """{"Error Message": "Invalid API KEY. Feel free to create a Free API Key."}""", "rejected the API key")]
    [InlineData(HttpStatusCode.PaymentRequired, "Premium Query Parameter: this symbol is not available under your current subscription", "plan does not include data for EXD.PA")]
    [InlineData(HttpStatusCode.OK, """{"Error Message": "Special Endpoint : this endpoint is not available under your current subscription"}""", "plan does not include data for EXD.PA")]
    [InlineData(HttpStatusCode.InternalServerError, "", "HTTP 500")]
    [InlineData(HttpStatusCode.OK, """{"Error Message": "Something odd"}""", "Something odd")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>", "malformed")]
    [InlineData(HttpStatusCode.OK, """{"unexpected": "object"}""", "malformed")]
    public async Task ProfileErrors_BecomeMarketDataException(HttpStatusCode status, string body, string expectedMessage)
    {
        _http.Status("profile", status, body);

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task DividendsRateLimited_Throws()
    {
        _http.Status("dividends", HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains("rate limit", ex.Message);
    }

    [Fact]
    public async Task NetworkFailure_Throws()
    {
        _http.Throw("profile", new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains("Could not reach", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_Throws()
    {
        _http.Throw("profile", new TaskCanceledException("timed out"));

        var ex = await Assert.ThrowsAsync<MarketDataException>(() => CreateClient().GetSnapshotAsync("EXD.PA"));
        Assert.Contains("did not respond in time", ex.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsNotDisguisedAsProviderFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _http.Throw("profile", new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => CreateClient().GetSnapshotAsync("EXD.PA", cts.Token));
    }
}
