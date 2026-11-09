using DividendScreener.Core.Models;
using DividendScreener.Core.Valuation;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";

builder.Services.AddSingleton<IValuationService, ValuationService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
          .AllowAnyHeader()
          .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

app.MapPost("/api/valuations", (ValuationRequest request, IValuationService valuation) =>
    {
        try
        {
            return Results.Ok(valuation.Value(request.Stock, request.Metrics, request.Market, request.Options));
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] });
        }
    })
   .WithName("ValueStock");

app.Run();

/// <summary>Ad-hoc valuation from caller-supplied inputs. Market data ingestion comes in a later chunk.</summary>
public sealed record ValuationRequest(
    Stock Stock, FinancialMetrics Metrics, MarketAssumptions Market, ValuationOptions? Options = null);

public partial class Program;
