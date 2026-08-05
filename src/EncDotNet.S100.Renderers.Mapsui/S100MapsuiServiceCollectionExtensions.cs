using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// <see cref="IServiceCollection"/> helpers for registering the reusable S-100
/// Mapsui session with Microsoft dependency injection. Using DI is optional —
/// hosts can compose a session manually with
/// <see cref="S100MapExtensions.AddS100"/>.
/// </summary>
public static class S100MapsuiServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IS100MapSessionFactory"/> that creates a
    /// per-<see cref="Mapsui.Map"/> <see cref="IS100MapSession"/> from
    /// container-resolved dependencies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsFactory">
    /// Optional factory for <see cref="S100MapsuiOptions"/>, letting the host
    /// supply a <see cref="S100MapsuiOptions.DatasetPipelineFactory"/>, S-98
    /// authority provider, pattern-clip cache, or render-subsystem/scene
    /// configuration from DI. When omitted, sessions use the defaults of
    /// <see cref="S100MapExtensions.AddS100"/>. (A factory delegate is used
    /// rather than a mutating action because <see cref="S100MapsuiOptions"/> is
    /// immutable.)
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The host must separately register an <c>ICrsTransformFactory</c> (e.g.
    /// <c>ProjNetCrsTransformFactory</c>); the reusable assembly ships no CRS
    /// implementation. Registration is idempotent.
    /// </remarks>
    public static IServiceCollection AddS100Mapsui(
        this IServiceCollection services,
        Func<IServiceProvider, S100MapsuiOptions>? optionsFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (optionsFactory is not null)
            services.TryAddSingleton(optionsFactory);

        // Transient (not singleton) so the factory captures the IServiceProvider
        // of whatever scope resolves it: a singleton would capture the root
        // provider and fail to resolve scoped dependencies (e.g. a scoped
        // ICrsTransformFactory) from Create.
        services.TryAddTransient<IS100MapSessionFactory, S100MapSessionFactory>();
        return services;
    }
}
