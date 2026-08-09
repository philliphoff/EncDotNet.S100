using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
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
    /// <param name="options">
    /// Optional rendering configuration. When <see langword="null"/>, defaults
    /// are used. Supply <see cref="S100MapsuiOptions.CrsTransformFactory"/> (or a
    /// prebuilt <see cref="S100MapsuiOptions.DatasetRenderer"/>) so a renderer can
    /// be built. A DI host can also share its own <see
    /// cref="S100MapsuiOptions.ProcessorOwner"/> and <see
    /// cref="S100MapsuiOptions.DatasetRenderer"/> here so the session composes
    /// over the same instances other services use.
    /// </param>
    /// <returns>The owned, disposable S-100 session.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="map"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Neither <see cref="S100MapsuiOptions.CrsTransformFactory"/> nor <see
    /// cref="S100MapsuiOptions.DatasetRenderer"/> is supplied, so no renderer can
    /// be built.
    /// </exception>
    /// <remarks>
    /// Ownership lives only on the returned instance — it is not stored in a
    /// static table or <c>Map.Tag</c>. Collaborators supplied on
    /// <paramref name="options"/> are borrowed: the session never disposes an
    /// injected <see cref="S100MapsuiOptions.ProcessorOwner"/> (the renderer is
    /// not disposable), so a DI host retains their lifetime. Normal pan, zoom,
    /// and rotation remain available through <c>Map.Navigator</c>. This first API
    /// renders caller-supplied processors via
    /// <see cref="IS100MapSession.AddDatasetAsync"/>; file/exchange-set loading
    /// and DI registration helpers are later additions.
    /// </remarks>
    public static IS100MapSession AddS100(
        this Map map,
        S100MapsuiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        options ??= new S100MapsuiOptions();

        // A renderer needs a CRS transform factory to project native grid CRSes
        // to EPSG:3857. Either the caller injects a prebuilt renderer (which
        // already carries one) via options.DatasetRenderer, or it supplies the
        // factory via options.CrsTransformFactory so AddS100 can build the
        // default renderer.
        if (options.CrsTransformFactory is null && options.DatasetRenderer is null)
        {
            throw new ArgumentException(
                "Set options.CrsTransformFactory so AddS100 can build the default "
                + "dataset renderer, or options.DatasetRenderer to a prebuilt "
                + "renderer.",
                nameof(options));
        }

        // Register the custom S-100 style and layer renderers with Mapsui. The
        // call is idempotent, so repeated AddS100 calls are safe.
        S100MapsuiRendering.Register();

        var layerBands = new MapsuiLayerBands(map);

        // Borrow the collaborators supplied on the options and self-create the
        // rest. Only the processor owner is disposable, so only its ownership is
        // tracked: the session disposes it only when AddS100 created it.
        var ownsProcessorOwner = options.ProcessorOwner is null;
        var processorOwner = options.ProcessorOwner ?? new DatasetProcessorOwner();

        // CrsTransformFactory! is safe: the validation above guarantees it is
        // non-null whenever options.DatasetRenderer is null, and the null-
        // coalescing skips it when a renderer was injected.
        var renderer = options.DatasetRenderer
            ?? new MapsuiDatasetRenderer(
                options.CrsTransformFactory!,
                options.PatternClipCache,
                options);

        var authorityProvider = options.InteroperabilityAuthorityProvider
            ?? new InteroperabilityAuthorityProvider(new InteroperabilityAuthority());
        var session = new MapsuiMapSession(
            layerBands, processorOwner, renderer, authorityProvider);
        var navigator = new MapsuiMapNavigator(map);
        var dynamicSourceHost = new DynamicSources.S100DynamicSourceHost(
            layerBands,
            options.DynamicFeatureRendererResolver,
            options.DynamicSourceMarshal,
            logger: null,
            coalesceWindow: options.DynamicSourceCoalesceWindow);

        return new S100MapSession(
            processorOwner,
            session,
            navigator,
            dynamicSourceHost,
            options.DatasetPipelineFactory,
            ownsProcessorOwner);
    }
}
