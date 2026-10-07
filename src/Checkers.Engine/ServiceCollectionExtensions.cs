using Checkers.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkers.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the KingsRow worker pool as the application's <see cref="IEngineGateway"/> and starts it with the app.</summary>
    public static IServiceCollection AddKingsRowEngine(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EngineOptions.SectionName);
        string type = section["Type"] ?? "kingsrow";
        if (!string.Equals(type, "kingsrow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Engine:Type \"{type}\" is not supported. Use \"kingsrow\".");
        }

        services.Configure<EngineOptions>(section);
        services.AddSingleton<IEngineAdapterFactory, KingsRowAdapterFactory>();
        services.AddSingleton<EnginePool>();
        services.AddSingleton<IEngineGateway>(provider => provider.GetRequiredService<EnginePool>());
        services.AddHostedService<EngineWarmupService>();
        return services;
    }
}
