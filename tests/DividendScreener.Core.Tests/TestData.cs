using DividendScreener.Core.Models;

namespace DividendScreener.Core.Tests;

/// <summary>
/// A fictitious "standard" European dividend stock whose numbers are chosen so every expected
/// value can be checked by hand: the dividend grows exactly 4% a year from 2.00 EUR in FY2014,
/// beta is 1.2, and CAPM inputs are Rf = 3%, Rm = 8%, giving r = 9% and g = 4%.
/// </summary>
internal static class TestData
{
    public static readonly Stock StandardStock = new()
    {
        Ticker = "STDX",
        Name = "Standard Dividend SA",
        Isin = "FR0000000000",
        Exchange = "Euronext Paris",
        Currency = "EUR",
        CurrentPrice = 50.00m,
        Sector = "Consumer Staples",
    };

    /// <summary>DPS = 2.00 × 1.04^(year − 2014), exact in decimal.</summary>
    public static readonly IReadOnlyList<AnnualDividend> FourPercentHistory =
    [
        new(2014, 2m),
        new(2015, 2.08m),
        new(2016, 2.1632m),
        new(2017, 2.249728m),
        new(2018, 2.33971712m),
        new(2019, 2.4333058048m),
        new(2020, 2.530638036992m),
        new(2021, 2.63186355847168m),
        new(2022, 2.7371381008105472m),
        new(2023, 2.846623624842969088m),
        new(2024, 2.96048856983668785152m),
    ];

    public static readonly FinancialMetrics StandardMetrics = new()
    {
        Beta = 1.2m,
        FreeCashFlow = 8_000_000_000m,
        DividendsPaid = -4_400_000_000m, // reported as a cash outflow
        DividendHistory = FourPercentHistory,
    };

    public static readonly MarketAssumptions StandardMarket = new(RiskFreeRate: 0.03m, ExpectedMarketReturn: 0.08m);
}
