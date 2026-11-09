using DividendScreener.Core.Models;

namespace DividendScreener.Core.Valuation;

public interface IValuationService
{
    /// <summary>CAPM: r = Rf + β × (Rm − Rf).</summary>
    decimal CalculateRequiredReturn(decimal beta, decimal riskFreeRate, decimal expectedMarketReturn);

    /// <summary>CAGR = (end / start)^(1 / years) − 1.</summary>
    decimal CalculateCagr(decimal startValue, decimal endValue, int years);

    /// <summary>
    /// Dividend CAGR over the <paramref name="years"/> fiscal years ending at the latest year in the history.
    /// Null when the history lacks the start year or the start dividend is zero.
    /// </summary>
    decimal? CalculateDividendCagr(IReadOnlyList<AnnualDividend> history, int years);

    /// <summary>Gordon Growth Model: V0 = D0 × (1 + g) / (r − g). Requires r &gt; g.</summary>
    decimal CalculateGordonGrowthValue(decimal currentDividend, decimal requiredReturn, decimal growthRate);

    /// <summary>Dividends paid / free cash flow. Null when free cash flow is zero or negative.</summary>
    decimal? CalculateFcfPayoutRatio(decimal dividendsPaid, decimal freeCashFlow);

    /// <summary>Annual dividend / price. Used for both dividend yield and yield on cost. Null when price is not positive.</summary>
    decimal? CalculateYield(decimal annualDividend, decimal price);

    /// <summary>
    /// Runs the full dividend discount valuation for one stock.
    /// <paramref name="costBasis"/> is the investor's purchase price per share, used for yield on cost.
    /// </summary>
    ValuationResult Value(
        Stock stock, FinancialMetrics metrics, MarketAssumptions market,
        ValuationOptions? options = null, decimal? costBasis = null);
}
