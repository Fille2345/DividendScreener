using System.Text.RegularExpressions;
using DividendScreener.Core.MarketData;
using DividendScreener.Core.Models;
using DividendScreener.Core.Valuation;
using DividendScreener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";

// Default CAPM inputs. For euro-area stocks Rf should track the 10-year Bund yield; review these periodically.
var defaultMarket = builder.Configuration.GetSection("MarketAssumptions").Get<MarketAssumptions>()
                    ?? throw new InvalidOperationException("MarketAssumptions configuration section is missing.");

builder.Services.AddSingleton<IValuationService, ValuationService>();
builder.Services.AddMarketData(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
          .AllowAnyHeader()
          .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

app.MapGet("/api/market-assumptions", () => Results.Ok(defaultMarket))
   .WithName("GetMarketAssumptions");

app.MapGet("/api/valuations/{ticker}", async (
        string ticker,
        decimal? costBasis,
        decimal? riskFreeRate,
        decimal? marketReturn,
        decimal? maxGrowthRate,
        IMarketDataClient marketData,
        IValuationService valuation,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var errors = ValidateQuery(ticker, costBasis, riskFreeRate, marketReturn, maxGrowthRate);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        MarketSnapshot? snapshot;
        try
        {
            snapshot = await marketData.GetSnapshotAsync(ticker, cancellationToken);
        }
        catch (MarketDataException ex)
        {
            logger.LogWarning(ex, "Market data lookup failed for {Ticker}", ticker);
            return Results.Problem(
                title: "Market data unavailable",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (snapshot is null)
            return Results.Problem(
                title: "Unknown ticker",
                detail: $"No market data found for '{ticker}'.",
                statusCode: StatusCodes.Status404NotFound);

        var market = new MarketAssumptions(
            riskFreeRate ?? defaultMarket.RiskFreeRate,
            marketReturn ?? defaultMarket.ExpectedMarketReturn);
        var options = ValuationOptions.Default with { MaxPerpetualGrowthRate = maxGrowthRate };

        try
        {
            var result = valuation.Value(snapshot.Stock, snapshot.Metrics, market, options, costBasis);
            return Results.Ok(new StockValuationResponse(
                snapshot.Stock, snapshot.Metrics, market, result, snapshot.Source, snapshot.RetrievedAt));
        }
        catch (ArgumentException ex)
        {
            // The provider returned data the model cannot use (e.g. no dividend history).
            return Results.Problem(
                title: "Insufficient data for valuation",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    })
   .WithName("ValueTicker");

app.MapPost("/api/valuations", (ValuationRequest request, IValuationService valuation) =>
    {
        try
        {
            return Results.Ok(valuation.Value(
                request.Stock, request.Metrics, request.Market, request.Options, request.CostBasis));
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] });
        }
    })
   .WithName("ValueStock");

app.Run();

static Dictionary<string, string[]> ValidateQuery(
    string ticker, decimal? costBasis, decimal? riskFreeRate, decimal? marketReturn, decimal? maxGrowthRate)
{
    var errors = new Dictionary<string, string[]>();
    if (!TickerFormat.IsValid(ticker))
        errors["ticker"] = ["Ticker must be 1–20 characters: letters, digits, '.', '-', '^' or '='."];
    if (costBasis is <= 0)
        errors["costBasis"] = ["Cost basis must be positive."];
    if (riskFreeRate is < -0.05m or > 0.25m)
        errors["riskFreeRate"] = ["Risk-free rate must be between -0.05 and 0.25 (decimal fraction)."];
    if (marketReturn is < -0.05m or > 0.30m)
        errors["marketReturn"] = ["Market return must be between -0.05 and 0.30 (decimal fraction)."];
    if (maxGrowthRate is < -0.10m or > 0.20m)
        errors["maxGrowthRate"] = ["Max growth rate must be between -0.10 and 0.20 (decimal fraction)."];
    return errors;
}

/// <summary>Ad-hoc valuation from caller-supplied inputs.</summary>
public sealed record ValuationRequest(
    Stock Stock, FinancialMetrics Metrics, MarketAssumptions Market,
    ValuationOptions? Options = null, decimal? CostBasis = null);

/// <summary>A valuation together with the inputs it was computed from, so the UI can show its working.</summary>
public sealed record StockValuationResponse(
    Stock Stock, FinancialMetrics Metrics, MarketAssumptions Market, ValuationResult Valuation,
    string Source, DateTimeOffset RetrievedAt);

internal static partial class TickerFormat
{
    [GeneratedRegex(@"^[A-Za-z0-9.\-^=]{1,20}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string ticker) => Pattern().IsMatch(ticker);
}

public partial class Program;
