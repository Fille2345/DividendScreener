namespace DividendScreener.Core.Models;

/// <summary>
/// A listed equity. Prices are in <see cref="Currency"/> (e.g. EUR for Sanofi on Euronext Paris).
/// </summary>
public sealed record Stock
{
    public required string Ticker { get; init; }
    public required string Name { get; init; }
    public required string Isin { get; init; }
    public required string Exchange { get; init; }
    public required string Currency { get; init; }
    public required decimal CurrentPrice { get; init; }
    public string? Sector { get; init; }
}
