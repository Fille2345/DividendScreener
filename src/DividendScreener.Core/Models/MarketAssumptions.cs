namespace DividendScreener.Core.Models;

/// <summary>
/// Market-wide CAPM inputs, as decimal fractions (0.03 = 3%).
/// For euro-area stocks the risk-free rate is typically the 10-year German Bund yield.
/// </summary>
public sealed record MarketAssumptions(decimal RiskFreeRate, decimal ExpectedMarketReturn);
