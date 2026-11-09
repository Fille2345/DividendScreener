namespace DividendScreener.Core.Models;

/// <summary>
/// Output of a dividend discount valuation. All rates are decimal fractions (0.05 = 5%).
/// Values that cannot be computed meaningfully are null and explained in <see cref="Warnings"/>.
/// </summary>
public sealed record ValuationResult
{
    public required string Ticker { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal CurrentPrice { get; init; }

    /// <summary>Equity beta used in CAPM.</summary>
    public required decimal Beta { get; init; }

    /// <summary>Required rate of return r from CAPM.</summary>
    public required decimal RequiredReturn { get; init; }

    public decimal? DividendCagr3Y { get; init; }
    public decimal? DividendCagr5Y { get; init; }
    public decimal? DividendCagr10Y { get; init; }

    /// <summary>Growth rate g used in the Gordon Growth Model, after any cap.</summary>
    public decimal? GrowthRate { get; init; }

    /// <summary>Which CAGR window (in years) <see cref="GrowthRate"/> came from.</summary>
    public int? GrowthRatePeriodYears { get; init; }

    /// <summary>Latest annual dividend per share (D0).</summary>
    public required decimal CurrentDividend { get; init; }

    /// <summary>Next year's expected dividend per share, D1 = D0 × (1 + g).</summary>
    public decimal? NextDividend { get; init; }

    /// <summary>Trailing dividend yield, D0 / current price.</summary>
    public decimal? DividendYield { get; init; }

    /// <summary>Yield on cost, D0 / the investor's purchase price. Null when no cost basis was given.</summary>
    public decimal? YieldOnCost { get; init; }

    /// <summary>Gordon Growth Model value per share. Null when r ≤ g or no growth rate is available.</summary>
    public decimal? IntrinsicValue { get; init; }

    /// <summary>(Intrinsic value − price) / intrinsic value. Negative means the stock trades above its value.</summary>
    public decimal? MarginOfSafety { get; init; }

    /// <summary>Dividends paid / free cash flow. Null when free cash flow is zero or negative.</summary>
    public decimal? FcfPayoutRatio { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
