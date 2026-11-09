using DividendScreener.Core.Models;
using DividendScreener.Core.Valuation;

namespace DividendScreener.Core.Tests;

public class ValuationServiceTests
{
    // Decimal places compared for values that involve an n-th root (CAGR). Everything else is
    // pure decimal arithmetic and is compared exactly.
    private const int RootPrecision = 24;

    private readonly ValuationService _sut = new();

    // ---------- CAPM ----------

    [Fact]
    public void RequiredReturn_StandardInputs_IsNinePercent()
    {
        // 0.03 + 1.2 × (0.08 − 0.03) = 0.09
        Assert.Equal(0.09m, _sut.CalculateRequiredReturn(1.2m, 0.03m, 0.08m));
    }

    [Theory]
    [InlineData("1.0", "0.025", "0.075", "0.075")]   // beta 1 → market return
    [InlineData("0", "0.025", "0.075", "0.025")]     // beta 0 → risk-free rate
    [InlineData("0.65", "0.026", "0.081", "0.06175")] // defensive stock: 0.026 + 0.65 × 0.055
    [InlineData("1.5", "0.02", "0.07", "0.095")]
    [InlineData("-0.2", "0.03", "0.08", "0.02")]     // negative beta lowers r below Rf
    public void RequiredReturn_MatchesCapmFormula(string beta, string rf, string rm, string expected)
    {
        var actual = _sut.CalculateRequiredReturn(decimal.Parse(beta, Inv), decimal.Parse(rf, Inv), decimal.Parse(rm, Inv));

        Assert.Equal(decimal.Parse(expected, Inv), actual);
    }

    // ---------- CAGR ----------

    [Theory]
    [InlineData("1000", "1331", 3, "0.1")]   // 1.1³ = 1.331
    [InlineData("2", "2.4333058048", 5, "0.04")]
    [InlineData("100", "100", 7, "0")]
    [InlineData("1331", "1000", 3, "-0.0909090909090909090909090909")] // 1/1.1 − 1
    [InlineData("50", "60", 1, "0.2")]
    public void Cagr_KnownValues(string start, string end, int years, string expected)
    {
        var actual = _sut.CalculateCagr(decimal.Parse(start, Inv), decimal.Parse(end, Inv), years);

        Assert.Equal(decimal.Parse(expected, Inv), actual, RootPrecision);
    }

    [Fact]
    public void Cagr_DoublingOverTenYears_IsAccurateBeyondDoublePrecision()
    {
        // 2^(1/10) − 1 = 0.07177346253629316421300632502334...
        Assert.Equal(0.0717734625362931642130063250m, _sut.CalculateCagr(100m, 200m, 10), RootPrecision);
    }

    [Fact]
    public void Cagr_DividendCutToZero_IsMinusOneHundredPercent()
    {
        Assert.Equal(-1m, _sut.CalculateCagr(1.5m, 0m, 3));
    }

    [Theory]
    [InlineData("0", "1", 3)]
    [InlineData("-1", "1", 3)]
    [InlineData("1", "-1", 3)]
    [InlineData("1", "2", 0)]
    [InlineData("1", "2", -5)]
    public void Cagr_InvalidInputs_Throw(string start, string end, int years)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _sut.CalculateCagr(decimal.Parse(start, Inv), decimal.Parse(end, Inv), years));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void DividendCagr_StandardHistory_IsFourPercentForEveryWindow(int years)
    {
        var cagr = _sut.CalculateDividendCagr(TestData.FourPercentHistory, years);

        Assert.NotNull(cagr);
        Assert.Equal(0.04m, cagr.Value, RootPrecision);
    }

    [Fact]
    public void DividendCagr_UsesLatestYearAsEndPoint_RegardlessOfInputOrder()
    {
        AnnualDividend[] history = [new(2024, 1.331m), new(2018, 0.5m), new(2021, 1.0m)];

        Assert.Equal(0.1m, _sut.CalculateDividendCagr(history, 3)!.Value, RootPrecision);
    }

    [Fact]
    public void DividendCagr_DistinguishesWindows()
    {
        // 3y: 1.00 → 1.331 = 10%. 5y: 1.00 → 1.331 over 5 years. 10y: 0.50 → 1.331.
        AnnualDividend[] history = [new(2014, 0.5m), new(2019, 1m), new(2021, 1m), new(2024, 1.331m)];

        Assert.Equal(0.1m, _sut.CalculateDividendCagr(history, 3)!.Value, RootPrecision);
        Assert.Equal(_sut.CalculateCagr(1m, 1.331m, 5), _sut.CalculateDividendCagr(history, 5));
        Assert.Equal(_sut.CalculateCagr(0.5m, 1.331m, 10), _sut.CalculateDividendCagr(history, 10));
    }

    [Fact]
    public void DividendCagr_MissingStartYear_ReturnsNull()
    {
        AnnualDividend[] history = [new(2022, 1m), new(2023, 1.1m), new(2024, 1.2m)];

        Assert.Null(_sut.CalculateDividendCagr(history, 3));
        Assert.Null(_sut.CalculateDividendCagr(history, 5));
    }

    [Fact]
    public void DividendCagr_ZeroStartDividend_ReturnsNull()
    {
        // A company that initiated its dividend has no defined growth rate from zero.
        AnnualDividend[] history = [new(2021, 0m), new(2024, 1m)];

        Assert.Null(_sut.CalculateDividendCagr(history, 3));
    }

    [Fact]
    public void DividendCagr_EmptyHistory_ReturnsNull()
    {
        Assert.Null(_sut.CalculateDividendCagr([], 3));
    }

    [Fact]
    public void DividendCagr_DuplicateYear_Throws()
    {
        AnnualDividend[] history = [new(2021, 1m), new(2024, 1m), new(2024, 1.2m)];

        Assert.Throws<ArgumentException>(() => _sut.CalculateDividendCagr(history, 3));
    }

    // ---------- Gordon Growth Model ----------

    [Fact]
    public void GordonGrowth_TextbookCase()
    {
        // D1 = 2.00 × 1.04 = 2.08; V0 = 2.08 / (0.09 − 0.04) = 41.60
        Assert.Equal(41.60m, _sut.CalculateGordonGrowthValue(2.00m, 0.09m, 0.04m));
    }

    [Theory]
    [InlineData("3.92", "0.07", "0.03", "100.94")]    // 3.92 × 1.03 / 0.04
    [InlineData("1.00", "0.10", "0", "10")]           // zero growth = perpetuity D / r
    [InlineData("4.00", "0.08", "-0.02", "39.2")]     // shrinking dividend: 3.92 / 0.10
    [InlineData("0", "0.08", "0.02", "0")]
    public void GordonGrowth_MatchesFormula(string d0, string r, string g, string expected)
    {
        var actual = _sut.CalculateGordonGrowthValue(decimal.Parse(d0, Inv), decimal.Parse(r, Inv), decimal.Parse(g, Inv));

        Assert.Equal(decimal.Parse(expected, Inv), actual);
    }

    [Theory]
    [InlineData("0.05", "0.05")]
    [InlineData("0.05", "0.06")]
    public void GordonGrowth_RequiredReturnNotAboveGrowth_Throws(string r, string g)
    {
        Assert.Throws<ArgumentException>(
            () => _sut.CalculateGordonGrowthValue(2m, decimal.Parse(r, Inv), decimal.Parse(g, Inv)));
    }

    [Fact]
    public void GordonGrowth_NegativeDividend_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.CalculateGordonGrowthValue(-1m, 0.09m, 0.04m));
    }

    // ---------- FCF payout ratio ----------

    [Theory]
    [InlineData("4400", "8000", "0.55")]
    [InlineData("-4400", "8000", "0.55")] // cash-flow statement sign convention
    [InlineData("0", "8000", "0")]
    [InlineData("9000", "6000", "1.5")]   // paying out more than it generates
    public void FcfPayoutRatio_MatchesFormula(string dividendsPaid, string fcf, string expected)
    {
        var actual = _sut.CalculateFcfPayoutRatio(decimal.Parse(dividendsPaid, Inv), decimal.Parse(fcf, Inv));

        Assert.Equal(decimal.Parse(expected, Inv), actual);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1000")]
    public void FcfPayoutRatio_NonPositiveFcf_ReturnsNull(string fcf)
    {
        Assert.Null(_sut.CalculateFcfPayoutRatio(4400m, decimal.Parse(fcf, Inv)));
    }

    // ---------- Yield ----------

    [Theory]
    [InlineData("3.92", "98", "0.04")]
    [InlineData("2.00", "25", "0.08")]
    [InlineData("0", "40", "0")]
    public void Yield_MatchesFormula(string dividend, string price, string expected)
    {
        Assert.Equal(decimal.Parse(expected, Inv), _sut.CalculateYield(decimal.Parse(dividend, Inv), decimal.Parse(price, Inv)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Yield_NonPositivePrice_ReturnsNull(string price)
    {
        Assert.Null(_sut.CalculateYield(2m, decimal.Parse(price, Inv)));
    }

    [Fact]
    public void Yield_NegativeDividend_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.CalculateYield(-1m, 50m));
    }

    // ---------- Full valuation ----------

    [Fact]
    public void Value_StandardStock_ProducesExpectedValuation()
    {
        var result = _sut.Value(TestData.StandardStock, TestData.StandardMetrics, TestData.StandardMarket);

        Assert.Equal("STDX", result.Ticker);
        Assert.Equal("Standard Dividend SA", result.Name);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal(50.00m, result.CurrentPrice);
        Assert.Equal(1.2m, result.Beta);
        Assert.Equal(0.09m, result.RequiredReturn);
        Assert.Equal(0.04m, result.DividendCagr3Y!.Value, RootPrecision);
        Assert.Equal(0.04m, result.DividendCagr5Y!.Value, RootPrecision);
        Assert.Equal(0.04m, result.DividendCagr10Y!.Value, RootPrecision);
        Assert.Equal(5, result.GrowthRatePeriodYears);
        Assert.Equal(0.04m, result.GrowthRate!.Value, RootPrecision);
        Assert.Equal(2.96048856983668785152m, result.CurrentDividend);
        Assert.Equal(3.0789081126301553655808m, result.NextDividend!.Value, 20);
        // V0 = 2.96048856983668785152 × 1.04 / 0.05
        Assert.Equal(61.578162252603107311616m, result.IntrinsicValue!.Value, 20);
        // (61.578162... − 50) / 61.578162...
        Assert.Equal(0.188023835545914835913m, result.MarginOfSafety!.Value, 20);
        Assert.Equal(0.55m, result.FcfPayoutRatio);
        // 2.96048856983668785152 / 50
        Assert.Equal(0.0592097713967337570304m, result.DividendYield);
        Assert.Null(result.YieldOnCost);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Value_WithCostBasis_ReportsYieldOnCost()
    {
        var result = _sut.Value(
            TestData.StandardStock, TestData.StandardMetrics, TestData.StandardMarket, costBasis: 32m);

        // 2.96048856983668785152 / 32
        Assert.Equal(0.09251526780739649536m, result.YieldOnCost);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public void Value_NonPositiveCostBasis_Throws(string costBasis)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.Value(
            TestData.StandardStock, TestData.StandardMetrics, TestData.StandardMarket,
            costBasis: decimal.Parse(costBasis, Inv)));
    }

    [Fact]
    public void Value_OvervaluedStock_HasNegativeMarginOfSafety()
    {
        var expensive = TestData.StandardStock with { CurrentPrice = 80m };

        var result = _sut.Value(expensive, TestData.StandardMetrics, TestData.StandardMarket);

        Assert.True(result.MarginOfSafety < 0);
    }

    [Fact]
    public void Value_PreferredPeriodMissing_FallsBackToNextPeriod()
    {
        var shortHistory = TestData.StandardMetrics with
        {
            DividendHistory = [new(2021, 1m), new(2022, 1.1m), new(2023, 1.21m), new(2024, 1.331m)],
        };

        var result = _sut.Value(TestData.StandardStock, shortHistory, TestData.StandardMarket);

        Assert.Null(result.DividendCagr5Y);
        Assert.Null(result.DividendCagr10Y);
        Assert.Equal(3, result.GrowthRatePeriodYears);
        Assert.Equal(0.1m, result.GrowthRate!.Value, RootPrecision);
    }

    [Fact]
    public void Value_CustomPeriodPreference_IsRespected()
    {
        var options = new ValuationOptions { GrowthRatePeriodPreference = [10] };

        var result = _sut.Value(TestData.StandardStock, TestData.StandardMetrics, TestData.StandardMarket, options);

        Assert.Equal(10, result.GrowthRatePeriodYears);
    }

    [Fact]
    public void Value_GrowthAboveCap_IsCappedAndWarned()
    {
        var options = new ValuationOptions { MaxPerpetualGrowthRate = 0.03m };

        var result = _sut.Value(TestData.StandardStock, TestData.StandardMetrics, TestData.StandardMarket, options);

        Assert.Equal(0.03m, result.GrowthRate);
        // 2.96048856983668785152 × 1.03 / 0.06
        Assert.Equal(50.821720448863141451093333333m, result.IntrinsicValue!.Value, 20);
        Assert.Single(result.Warnings, w => w.Contains("capped"));
    }

    [Fact]
    public void Value_GrowthAtOrAboveRequiredReturn_HasNoIntrinsicValue()
    {
        // Beta 0.2 → r = 0.03 + 0.2 × 0.05 = 0.04 = g
        var lowBeta = TestData.StandardMetrics with { Beta = 0.2m };

        var result = _sut.Value(TestData.StandardStock, lowBeta, TestData.StandardMarket);

        Assert.Null(result.IntrinsicValue);
        Assert.Null(result.MarginOfSafety);
        Assert.Contains(result.Warnings, w => w.Contains("Gordon Growth Model is undefined"));
    }

    [Fact]
    public void Value_InsufficientHistory_HasNoGrowthOrValue()
    {
        var oneYear = TestData.StandardMetrics with { DividendHistory = [new(2024, 3m)] };

        var result = _sut.Value(TestData.StandardStock, oneYear, TestData.StandardMarket);

        Assert.Equal(3m, result.CurrentDividend);
        Assert.Null(result.GrowthRate);
        Assert.Null(result.GrowthRatePeriodYears);
        Assert.Null(result.NextDividend);
        Assert.Null(result.IntrinsicValue);
        Assert.Contains(result.Warnings, w => w.Contains("too short"));
    }

    [Fact]
    public void Value_NegativeFreeCashFlow_WarnsAndOmitsPayout()
    {
        var burningCash = TestData.StandardMetrics with { FreeCashFlow = -500_000_000m };

        var result = _sut.Value(TestData.StandardStock, burningCash, TestData.StandardMarket);

        Assert.Null(result.FcfPayoutRatio);
        Assert.Contains(result.Warnings, w => w.Contains("Free cash flow"));
    }

    [Fact]
    public void Value_MissingCashFlowData_WarnsAndOmitsPayout()
    {
        var noCashFlow = TestData.StandardMetrics with { FreeCashFlow = null, DividendsPaid = null };

        var result = _sut.Value(TestData.StandardStock, noCashFlow, TestData.StandardMarket);

        Assert.Null(result.FcfPayoutRatio);
        Assert.NotNull(result.IntrinsicValue);
        Assert.Contains(result.Warnings, w => w.Contains("Cash-flow data is unavailable"));
    }

    [Fact]
    public void Value_DividendsExceedFcf_Warns()
    {
        var overpaying = TestData.StandardMetrics with { FreeCashFlow = 4_000_000_000m };

        var result = _sut.Value(TestData.StandardStock, overpaying, TestData.StandardMarket);

        Assert.Equal(1.1m, result.FcfPayoutRatio);
        Assert.Contains(result.Warnings, w => w.Contains("not be sustainable"));
    }

    [Fact]
    public void Value_EmptyHistory_Throws()
    {
        var noDividends = TestData.StandardMetrics with { DividendHistory = [] };

        Assert.Throws<ArgumentException>(
            () => _sut.Value(TestData.StandardStock, noDividends, TestData.StandardMarket));
    }

    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
}
