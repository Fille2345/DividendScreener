using System.Net;
using DividendScreener.Core.MarketData;
using DividendScreener.Infrastructure.MarketData;
using DividendScreener.Infrastructure.MarketData.Fmp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace DividendScreener.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the configured market data provider behind a caching decorator.</summary>
    public static IServiceCollection AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MarketDataOptions.SectionName).Get<MarketDataOptions>()
                      ?? new MarketDataOptions();

        Func<IServiceProvider, IMarketDataClient> createProvider = options.Provider.ToUpperInvariant() switch
        {
            "SAMPLE" => sp => ActivatorUtilities.CreateInstance<SampleMarketDataClient>(sp),
            "FMP" => AddFmp(services),
            _ => throw new InvalidOperationException(
                $"Unknown MarketData:Provider '{options.Provider}'. Supported providers: Fmp, Sample."),
        };

        services.AddSingleton(options);
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMarketDataClient>(sp => new CachingMarketDataClient(
            createProvider(sp), sp.GetRequiredService<IMemoryCache>(), options));

        return services;
    }

    private static Func<IServiceProvider, IMarketDataClient> AddFmp(IServiceCollection services)
    {
        services
            .AddHttpClient(FmpOptions.HttpClientName, (sp, client) =>
                client.BaseAddress = new Uri(ReadFmpOptions(sp.GetRequiredService<IConfiguration>()).BaseUrl))
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                resilience.Retry.MaxRetryAttempts = 2;
                // Retry network errors and 5xx, but not 429: FMP's limits are per day, so retrying only burns quota.
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome) &&
                    args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests);
            });

        // Options are read when the client is first resolved, so configuration added later (tests, user secrets) applies.
        return sp =>
        {
            var fmp = ReadFmpOptions(sp.GetRequiredService<IConfiguration>());
            var logger = sp.GetRequiredService<ILogger<FmpMarketDataClient>>();
            if (string.IsNullOrWhiteSpace(fmp.ApiKey))
                logger.LogWarning("{Key} is not set; market data requests will fail until it is configured.",
                    FmpOptions.ApiKeyConfigKey);

            return new FmpMarketDataClient(
                sp.GetRequiredService<IHttpClientFactory>(), fmp, sp.GetRequiredService<TimeProvider>(), logger);
        };
    }

    private static FmpOptions ReadFmpOptions(IConfiguration configuration)
    {
        var fmp = configuration.GetSection("MarketData:Fmp").Get<FmpOptions>() ?? new FmpOptions();
        fmp.ApiKey = configuration[FmpOptions.ApiKeyConfigKey];
        return fmp;
    }
}
