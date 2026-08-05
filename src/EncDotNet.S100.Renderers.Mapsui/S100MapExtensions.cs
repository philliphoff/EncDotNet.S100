using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Extension entry point that attaches a reusable S-100 subsystem to a
/// <see cref="Map"/>.
/// </summary>
public static class S100MapExtensions
{
    /// <summary>
    /// Attaches an S-100 subsystem to <paramref name="map"/> and returns the
    /// disposable session that owns it. The session composes and owns the layer
    /// bands, processor ownership, dataset renderer, dataset-layer session, and
    /// navigation surface; disposing the returned instance releases all of them.
    /// </summary>
    /// <param name="map">The Mapsui map to attach to.</param>
    /// <param name="crsTransformFactory">
    /// The CRS transform factory the coverage and arrow renderers use to
    /// project a native grid CRS to EPSG:3857. Required — the reusable assembly
    /// ships no CRS implementation, so a host supplies one (e.g.
    /// <c>ProjNetCrsTransformFactory</c>).
    /// </param>
    /// <param name="options">
    /// Optional rendering configuration. When <see langword="null"/>, defaults
    /// are used.
    /// </param>
    /// <returns>The owned, disposable S-100 session.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="map"/> or <paramref name="crsTransformFactory"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Ownership lives only on the returned instance — it is not stored in a
    /// static table or <c>Map.Tag</c>. Normal pan, zoom, and rotation remain
    /// available through <c>Map.Navigator</c>. This first API renders
    /// caller-supplied processors via
    /// <see cref="IS100MapSession.AddDatasetAsync"/>; file/exchange-set loading
    /// and DI registration helpers are later additions.
    /// </remarks>
    public static IS100MapSession AddS100(
        this Map map,
        ICrsTransformFactory crsTransformFactory,
        S100MapsuiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(crsTransformFactory);
        options ??= new S100MapsuiOptions();

        // Register the custom S-100 style and layer renderers with Mapsui. The
        // call is idempotent, so repeated AddS100 calls are safe.
        S100MapsuiRendering.Register();

        var layerBands = new MapsuiLayerBands(map);
        var processorOwner = new DatasetProcessorOwner();
        var renderer = new MapsuiDatasetRenderer(
            crsTransformFactory,
            options.PatternClipCache,
            options);
        var authorityProvider = options.InteroperabilityAuthorityProvider
            ?? new InteroperabilityAuthorityProvider(new InteroperabilityAuthority());
        var session = new MapsuiMapSession(
            layerBands, processorOwner, renderer, authorityProvider);
        var navigator = new MapsuiMapNavigator(map);

        return new S100MapSession(
            processorOwner, session, navigator, options.DatasetPipelineFactory);
    }
}
