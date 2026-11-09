using DividendScreener.Core.Models;

namespace DividendScreener.Core.Valuation;

/// <summary>
/// Stateless dividend valuation engine. All money and rate math is done in <see cref="decimal"/>;
/// the only non-arithmetic step (the n-th root in CAGR) is refined in decimal with Newton's method,
/// so results are accurate to decimal precision rather than double precision.
/// </summary>
public sealed class ValuationService : IValuationService
{
    public decimal CalculateRequiredReturn(decimal beta, decimal riskFreeRate, decimal expectedMarketReturn)
        => riskFreeRate + beta * (expectedMarketReturn - riskFreeRate);

    public decimal CalculateCagr(decimal startValue, decimal endValue, int years)
    {
        if (years <= 0)
            throw new ArgumentOutOfRangeException(nameof(years), years, "CAGR period must be at least one year.");
        if (startValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(startValue), startValue, "CAGR start value must be positive.");
        if (endValue < 0)
            throw new ArgumentOutOfRangeException(nameof(endValue), endValue, "CAGR end value cannot be negative.");

        if (endValue == 0)
            return -1m;

        return NthRoot(endValue / startValue, years) - 1m;
    }

    public decimal? CalculateDividendCagr(IReadOnlyList<AnnualDividend> history, int years)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0)
            return null;

        var byYear = ToYearLookup(history);
        var latestYear = byYear.Keys.Max();

        if (!byYear.TryGetValue(latestYear - years, out var start) || start <= 0)
            return null;

        return CalculateCagr(start, byYear[latestYear], years);
    }

    public decimal CalculateGordonGrowthValue(decimal currentDividend, decimal requiredReturn, decimal growthRate)
    {
        if (currentDividend < 0)
            throw new ArgumentOutOfRangeException(nameof(currentDividend), currentDividend, "Dividend cannot be negative.");
        if (requiredReturn <= growthRate)
            throw new ArgumentException(
                $"Gordon Growth Model requires r > g (r = {requiredReturn}, g = {growthRate}).", nameof(growthRate));

        return currentDividend * (1m + growthRate) / (requiredReturn - growthRate);
    }

    public decimal? CalculateFcfPayoutRatio(decimal dividendsPaid, decimal freeCashFlow)
    {
        if (freeCashFlow <= 0)
            return null;

        return Math.Abs(dividendsPaid) / freeCashFlow;
    }

    public decimal? CalculateYield(decimal annualDividend, decimal price)
    {
        if (annualDividend < 0)
            throw new ArgumentOutOfRangeException(nameof(annualDividend), annualDividend, "Dividend cannot be negative.");

        return price > 0 ? annualDividend / price : null;
    }

    public ValuationResult Value(
        Stock stock, FinancialMetrics metrics, MarketAssumptions market,
        ValuationOptions? options = null, decimal? costBasis = null)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(market);
        options ??= ValuationOptions.Default;

        if (metrics.DividendHistory.Count == 0)
            throw new ArgumentException("Dividend history is empty.", nameof(metrics));
        if (costBasis is <= 0)
            throw new ArgumentOutOfRangeException(nameof(costBasis), costBasis, "Cost basis must be positive.");

        var warnings = new List<string>();
        var byYear = ToYearLookup(metrics.DividendHistory);
        var currentDividend = byYear[byYear.Keys.Max()];

        var r = CalculateRequiredReturn(metrics.Beta, market.RiskFreeRate, market.ExpectedMarketReturn);

        var cagrs = new Dictionary<int, decimal?>
        {
            [3] = CalculateDividendCagr(metrics.DividendHistory, 3),
            [5] = CalculateDividendCagr(metrics.DividendHistory, 5),
            [10] = CalculateDividendCagr(metrics.DividendHistory, 10),
        };

        decimal? g = null;
        int? gPeriod = null;
        foreach (var period in options.GrowthRatePeriodPreference)
        {
            var cagr = cagrs.TryGetValue(period, out var known)
                ? known
                : CalculateDividendCagr(metrics.DividendHistory, period);
            if (cagr is not null)
            {
                g = cagr;
                gPeriod = period;
                break;
            }
        }

        if (g is null)
            warnings.Add("Dividend history is too short to derive a growth rate for any configured period.");

        if (g is not null && options.MaxPerpetualGrowthRate is { } cap && g > cap)
        {
            warnings.Add($"{gPeriod}-year dividend CAGR {g:P2} capped at {cap:P2} for perpetual growth.");
            g = cap;
        }

        decimal? nextDividend = g is null ? null : currentDividend * (1m + g.Value);
        decimal? intrinsicValue = null;
        if (g is not null)
        {
            if (r > g)
                intrinsicValue = CalculateGordonGrowthValue(currentDividend, r, g.Value);
            else
                warnings.Add($"Required return {r:P2} does not exceed growth rate {g:P2}; Gordon Growth Model is undefined.");
        }

        decimal? marginOfSafety = intrinsicValue is > 0
            ? (intrinsicValue.Value - stock.CurrentPrice) / intrinsicValue.Value
            : null;

        decimal? fcfPayout = null;
        if (metrics.FreeCashFlow is not { } fcf || metrics.DividendsPaid is not { } dividendsPaid)
        {
            warnings.Add("Cash-flow data is unavailable; FCF payout ratio cannot be calculated.");
        }
        else
        {
            fcfPayout = CalculateFcfPayoutRatio(dividendsPaid, fcf);
            if (fcfPayout is null)
                warnings.Add("Free cash flow is zero or negative; FCF payout ratio is not meaningful.");
        }

        if (fcfPayout > 1m)
            warnings.Add($"Dividends exceed free cash flow (FCF payout {fcfPayout:P1}); the dividend may not be sustainable.");

        return new ValuationResult
        {
            Ticker = stock.Ticker,
            Name = stock.Name,
            Currency = stock.Currency,
            CurrentPrice = stock.CurrentPrice,
            Beta = metrics.Beta,
            RequiredReturn = r,
            DividendCagr3Y = cagrs[3],
            DividendCagr5Y = cagrs[5],
            DividendCagr10Y = cagrs[10],
            GrowthRate = g,
            GrowthRatePeriodYears = gPeriod,
            CurrentDividend = currentDividend,
            NextDividend = nextDividend,
            DividendYield = CalculateYield(currentDividend, stock.CurrentPrice),
            YieldOnCost = costBasis is { } cost ? CalculateYield(currentDividend, cost) : null,
            IntrinsicValue = intrinsicValue,
            MarginOfSafety = marginOfSafety,
            FcfPayoutRatio = fcfPayout,
            Warnings = warnings,
        };
    }

    private static Dictionary<int, decimal> ToYearLookup(IReadOnlyList<AnnualDividend> history)
    {
        var byYear = new Dictionary<int, decimal>(history.Count);
        foreach (var d in history)
        {
            if (d.DividendPerShare < 0)
                throw new ArgumentException($"Negative dividend for fiscal year {d.FiscalYear}.", nameof(history));
            if (!byYear.TryAdd(d.FiscalYear, d.DividendPerShare))
                throw new ArgumentException($"Duplicate dividend entry for fiscal year {d.FiscalYear}.", nameof(history));
        }
        return byYear;
    }

    /// <summary>
    /// Positive real n-th root in decimal precision: a double estimate refined with Newton's method,
    /// x ← x − (xⁿ − a) / (n·xⁿ⁻¹), which roughly doubles the correct digits per step.
    /// </summary>
    private static decimal NthRoot(decimal value, int n)
    {
        if (n == 1)
            return value;

        var x = (decimal)Math.Pow((double)value, 1.0 / n);
        for (var i = 0; i < 4; i++)
        {
            var xPowNMinus1 = Pow(x, n - 1);
            var next = x - (xPowNMinus1 * x - value) / (n * xPowNMinus1);
            if (next == x)
                break;
            x = next;
        }
        return x;
    }

    private static decimal Pow(decimal x, int n)
    {
        var result = 1m;
        for (var i = 0; i < n; i++)
            result *= x;
        return result;
    }
}
