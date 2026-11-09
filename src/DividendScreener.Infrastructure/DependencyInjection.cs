using System.Net;
using DividendScreener.Core.MarketData;
using DividendScreener.Infrastructure.MarketData;
using DividendScreener.Infrastructure.MarketData.Eulerpool;
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
            "ROUTER" => AddRouter(services),
            "FMP" => AddFmp(services),
            "EULERPOOL" => AddEulerpool(services),
            "SAMPLE" => sp => ActivatorUtilities.CreateInstance<SampleMarketDataClient>(sp),
            _ => throw new InvalidOperationException(
                $"Unknown MarketData:Provider '{options.Provider}'. Supported providers: Router, Fmp, Eulerpool, Sample."),
        };

        services.AddSingleton(options);
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        // One cache in front of everything, so a routed lookup (including its fallback) is cached as a whole.
        services.AddSingleton<IMarketDataClient>(sp => new CachingMarketDataClient(
            createProvider(sp), sp.GetRequiredService<IMemoryCache>(), options));

        return services;
    }

    private static Func<IServiceProvider, IMarketDataClient> AddRouter(IServiceCollection services)
    {
        var createFmp = AddFmp(services);
        var createEulerpool = AddEulerpool(services);
        return sp => new RoutingMarketDataClient(
            usClient: createFmp(sp),
            europeanClient: createEulerpool(sp),
            sp.GetRequiredService<ILogger<RoutingMarketDataClient>>());
    }

    private static Func<IServiceProvider, IMarketDataClient> AddFmp(IServiceCollection services)
    {
        AddResilientHttpClient(services, FmpOptions.HttpClientName, cfg => ReadFmpOptions(cfg).BaseUrl);

        return sp =>
        {
            var fmp = ReadFmpOptions(sp.GetRequiredService<IConfiguration>());
            var logger = sp.GetRequiredService<ILogger<FmpMarketDataClient>>();
            WarnIfMissing(logger, fmp.ApiKey, FmpOptions.ApiKeyConfigKey);
            return new FmpMarketDataClient(
                sp.GetRequiredService<IHttpClientFactory>(), fmp, sp.GetRequiredService<TimeProvider>(), logger);
        };
    }

    private static Func<IServiceProvider, IMarketDataClient> AddEulerpool(IServiceCollection services)
    {
        AddResilientHttpClient(services, EulerpoolOptions.HttpClientName, cfg => ReadEulerpoolOptions(cfg).BaseUrl);

        return sp =>
        {
            var eulerpool = ReadEulerpoolOptions(sp.GetRequiredService<IConfiguration>());
            var logger = sp.GetRequiredService<ILogger<EulerpoolMarketDataClient>>();
            WarnIfMissing(logger, eulerpool.ApiKey, EulerpoolOptions.ApiKeyConfigKey);
            return new EulerpoolMarketDataClient(
                sp.GetRequiredService<IHttpClientFactory>(), eulerpool, sp.GetRequiredService<TimeProvider>(), logger);
        };
    }

    /// <summary>
    /// Timeouts plus two retries on network errors and 5xx. 429 is not retried: provider quotas are daily or
    /// monthly, so retrying only burns quota.
    /// </summary>
    private static void AddResilientHttpClient(
        IServiceCollection services, string name, Func<IConfiguration, string> baseUrl)
    {
        services
            .AddHttpClient(name, (sp, client) =>
                client.BaseAddress = new Uri(baseUrl(sp.GetRequiredService<IConfiguration>())))
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                resilience.Retry.MaxRetryAttempts = 2;
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome) &&
                    args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests);
            });
    }

    // Options are read when a client is first resolved, so configuration added later (tests, user secrets) applies.
    // API keys come only from their own top-level keys (environment variable or user secret), never appsettings.json.

    private static FmpOptions ReadFmpOptions(IConfiguration configuration)
    {
        var fmp = configuration.GetSection("MarketData:Fmp").Get<FmpOptions>() ?? new FmpOptions();
        fmp.ApiKey = configuration[FmpOptions.ApiKeyConfigKey];
        return fmp;
    }

    private static EulerpoolOptions ReadEulerpoolOptions(IConfiguration configuration)
    {
        var eulerpool = configuration.GetSection("MarketData:Eulerpool").Get<EulerpoolOptions>() ?? new EulerpoolOptions();
        eulerpool.ApiKey = configuration[EulerpoolOptions.ApiKeyConfigKey];
        return eulerpool;
    }

    private static void WarnIfMissing(ILogger logger, string? apiKey, string configKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            logger.LogWarning("{Key} is not set; requests to this provider will fail until it is configured.", configKey);
    }
}
