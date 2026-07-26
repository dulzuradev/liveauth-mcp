using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers LiveAuth CostShield services.</summary>
public static class LiveAuthCostShieldServiceCollectionExtensions
{
    /// <summary>
    /// Adds CostShield token verification and authorization services.
    /// </summary>
    public static IServiceCollection AddLiveAuthCostShield(
        this IServiceCollection services,
        Action<LiveAuth.CostShield.AspNetCore.LiveAuthCostShieldOptions>
            configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<
                LiveAuth.CostShield.AspNetCore.LiveAuthCostShieldOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<
                    LiveAuth.CostShield.AspNetCore.LiveAuthCostShieldOptions>,
                LiveAuth.CostShield.AspNetCore
                    .LiveAuthCostShieldOptionsValidator>());
        services.AddHttpClient(
            LiveAuth.CostShield.AspNetCore
                .LiveAuthCostShieldDefaults.HttpClientName);
        services.TryAddSingleton<
            LiveAuth.CostShield.AspNetCore.ICostShieldJwksProvider,
            LiveAuth.CostShield.AspNetCore.CostShieldJwksProvider>();
        services.TryAddScoped<
            LiveAuth.CostShield.AspNetCore.ILiveAuthCostShieldVerifier,
            LiveAuth.CostShield.AspNetCore.LiveAuthCostShieldVerifier>();
        return services;
    }
}
