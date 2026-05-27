using EncDotNet.S100.DynamicSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// <see cref="IServiceCollection"/> helpers that correlate a
/// <see cref="IDynamicFeatureSource"/> registration with an
/// <see cref="IDynamicFeatureRenderer"/> registration under a
/// shared string key.
/// </summary>
/// <remarks>
/// <para>
/// The viewer-side overlay host resolves the renderer for a
/// registered source via
/// <c>IServiceProvider.GetKeyedService&lt;IDynamicFeatureRenderer&gt;(
/// source.Metadata.RendererKey)</c>. Adapter authors typically call
/// <see cref="AddDynamicFeatureSource{TSource, TRenderer}"/> at
/// composition time, which wires both registrations in one step and
/// keeps the pair impossible to break in isolation.
/// </para>
/// <para>
/// When advanced lifetime / composition is required, register the
/// renderer with <see cref="AddDynamicFeatureRenderer{TRenderer}"/>
/// and the source separately via the standard
/// <see cref="IServiceCollection"/> APIs.
/// </para>
/// </remarks>
public static class DynamicFeatureRendererServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TRenderer"/> as a keyed
    /// <see cref="IDynamicFeatureRenderer"/> under
    /// <paramref name="rendererKey"/>.
    /// </summary>
    public static IServiceCollection AddDynamicFeatureRenderer<TRenderer>(
        this IServiceCollection services,
        string rendererKey,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRenderer : class, IDynamicFeatureRenderer
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(rendererKey);

        services.Add(new ServiceDescriptor(
            typeof(IDynamicFeatureRenderer),
            rendererKey,
            typeof(TRenderer),
            lifetime));
        return services;
    }

    /// <summary>
    /// Registers both <typeparamref name="TSource"/> (as
    /// <see cref="IDynamicFeatureSource"/>) and
    /// <typeparamref name="TRenderer"/> (as a keyed
    /// <see cref="IDynamicFeatureRenderer"/>) under
    /// <paramref name="rendererKey"/> in a single call.
    /// </summary>
    /// <remarks>
    /// The source is registered with
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(
    /// IServiceCollection, ServiceDescriptor)"/> semantics so that
    /// multiple instances of the same source type may coexist
    /// without de-duplication, while keeping the renderer keyed
    /// registration unique to <paramref name="rendererKey"/>.
    /// </remarks>
    public static IServiceCollection AddDynamicFeatureSource<TSource, TRenderer>(
        this IServiceCollection services,
        string rendererKey,
        ServiceLifetime sourceLifetime = ServiceLifetime.Singleton,
        ServiceLifetime rendererLifetime = ServiceLifetime.Singleton)
        where TSource : class, IDynamicFeatureSource
        where TRenderer : class, IDynamicFeatureRenderer
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(rendererKey);

        services.TryAddEnumerable(new ServiceDescriptor(
            typeof(IDynamicFeatureSource),
            typeof(TSource),
            sourceLifetime));

        services.Add(new ServiceDescriptor(
            typeof(IDynamicFeatureRenderer),
            rendererKey,
            typeof(TRenderer),
            rendererLifetime));

        return services;
    }
}
