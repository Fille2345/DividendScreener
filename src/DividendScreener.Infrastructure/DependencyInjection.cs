using DividendScreener.Core.MarketData;
using DividendScreener.Infrastructure.MarketData;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            _ => throw new InvalidOperationException(
                $"Unknown MarketData:Provider '{options.Provider}'. Supported providers: Sample."),
        };

        services.AddSingleton(options);
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMarketDataClient>(sp => new CachingMarketDataClient(
            createProvider(sp), sp.GetRequiredService<IMemoryCache>(), options));

        return services;
    }
}
