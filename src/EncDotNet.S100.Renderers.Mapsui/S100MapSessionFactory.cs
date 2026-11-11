using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Default <see cref="IS100MapSessionFactory"/>: resolves the CRS transform
/// factory (required) and <see cref="S100MapsuiOptions"/> (optional) from the
/// container and composes a session via <see cref="S100MapExtensions.AddS100"/>.
/// </summary>
internal sealed class S100MapSessionFactory : IS100MapSessionFactory
{
    private readonly IServiceProvider _services;

    public S100MapSessionFactory(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public IS100MapSession Create(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var options = _services.GetService<S100MapsuiOptions>() ?? new S100MapsuiOptions();

        // Share the container's registered collaborators when the options did
        // not already carry them, so a host that registered a processor owner or
        // a dataset renderer composes the session over the same instances its
        // other services use rather than fresh, session-private copies.
        if (options.ProcessorOwner is null
            && _services.GetService<DatasetProcessorOwner>() is { } processorOwner)
        {
            options = options with { ProcessorOwner = processorOwner };
        }

        if (options.DatasetRenderer is null
            && _services.GetService<MapsuiDatasetRenderer>() is { } datasetRenderer)
        {
            options = options with { DatasetRenderer = datasetRenderer };
        }

        // Default the dynamic-source renderer resolver to the container's keyed
        // IDynamicFeatureRenderer services (registered via
        // AddDynamicFeatureRenderer) when the host didn't supply one, so a DI
        // host gets renderer resolution for free.
        if (options.DynamicFeatureRendererResolver is null)
        {
            options = options with
            {
                DynamicFeatureRendererResolver = key =>
                    key is null ? null : _services.GetKeyedService<IDynamicFeatureRenderer>(key),
            };
        }

        // Fold a DI-registered CRS factory into the options when the caller
        // supplied neither one nor a prebuilt renderer (which carries its own).
        // The reusable assembly ships no CRS implementation; a missing
        // registration on the default-renderer path surfaces as a clear DI error
        // here.
        if (options.CrsTransformFactory is null && options.DatasetRenderer is null)
        {
            options = options with
            {
                CrsTransformFactory = _services.GetRequiredService<ICrsTransformFactory>(),
            };
        }

        return map.AddS100(options);
    }
}
