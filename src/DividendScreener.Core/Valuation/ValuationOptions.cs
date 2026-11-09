namespace DividendScreener.Core.Valuation;

public sealed record ValuationOptions
{
    public static readonly ValuationOptions Default = new();

    /// <summary>
    /// CAGR windows to try for g, in order. The first one the dividend history supports is used.
    /// </summary>
    public IReadOnlyList<int> GrowthRatePeriodPreference { get; init; } = [5, 3, 10];

    /// <summary>
    /// Optional ceiling on g. The Gordon model assumes g holds forever, so a short burst of
    /// fast dividend growth can produce absurd values; capping g near long-run nominal GDP
    /// growth (around 0.03–0.04 for the euro area) keeps results grounded. Null disables the cap.
    /// </summary>
    public decimal? MaxPerpetualGrowthRate { get; init; }
}
