using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// The renderer-neutral payload carried by a <see cref="SubLayerStackItem"/>:
/// the Mapsui-free portrayal slice a renderer (the headless Skia compositor or
/// the Mapsui viewer) turns into a drawable layer. Keeping the payload on the
/// stack item — rather than a pre-built Mapsui <c>ILayer</c> — is what lets the
/// S-98 ordering / suppression engine live in this (Mapsui-free) assembly and
/// be shared by every renderer (issue #398).
/// </summary>
public abstract class StackPayload
{
    private protected StackPayload()
    {
    }
}

/// <summary>
/// A vector sub-layer payload: one <see cref="VectorSubLayer"/> slice of a
/// dataset's <see cref="VectorPortrayalResult"/>. S-98 depth-feature
/// suppression (R-101-102-B) rewrites the <see cref="SubLayer"/>'s drawing
/// instructions in place of the old Mapsui <c>IFeature</c> filtering, so the
/// decision lives once, on the encoding-neutral instruction list.
/// </summary>
public sealed class VectorStackPayload : StackPayload
{
    /// <summary>
    /// Creates a vector payload binding a sub-layer to its parent portrayal
    /// result (needed for the palette, geometry provider, feature tags, and
    /// asset resolvers a renderer consumes).
    /// </summary>
    /// <param name="result">The parent vector portrayal result.</param>
    /// <param name="subLayer">The sub-layer slice this payload represents.</param>
    public VectorStackPayload(VectorPortrayalResult result, VectorSubLayer subLayer)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        System.ArgumentNullException.ThrowIfNull(subLayer);
        Result = result;
        SubLayer = subLayer;
    }

    /// <summary>The parent vector portrayal result.</summary>
    public VectorPortrayalResult Result { get; }

    /// <summary>The (possibly suppression-filtered) sub-layer slice.</summary>
    public VectorSubLayer SubLayer { get; }

    /// <summary>
    /// Returns a copy of this payload with <see cref="SubLayer"/> replaced —
    /// used by S-98 suppression to substitute a sub-layer whose instruction
    /// slice has been filtered.
    /// </summary>
    public VectorStackPayload WithSubLayer(VectorSubLayer subLayer) => new(Result, subLayer);
}

/// <summary>
/// A coverage sub-layer payload: one <see cref="CoverageSubLayerBase"/> slice
/// (colour band, arrow overlay, or station glyphs) of a dataset's
/// <see cref="CoveragePortrayalResult"/>.
/// </summary>
public sealed class CoverageStackPayload : StackPayload
{
    /// <summary>
    /// Creates a coverage payload binding a sub-layer to its parent portrayal
    /// result.
    /// </summary>
    /// <param name="result">The parent coverage portrayal result.</param>
    /// <param name="subLayer">The sub-layer slice this payload represents.</param>
    public CoverageStackPayload(CoveragePortrayalResult result, CoverageSubLayerBase subLayer)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        System.ArgumentNullException.ThrowIfNull(subLayer);
        Result = result;
        SubLayer = subLayer;
    }

    /// <summary>The parent coverage portrayal result.</summary>
    public CoveragePortrayalResult Result { get; }

    /// <summary>The coverage sub-layer slice.</summary>
    public CoverageSubLayerBase SubLayer { get; }

    /// <summary>
    /// Returns a copy of this payload with <see cref="SubLayer"/> replaced — used
    /// by the S-98 water-area clip rule to substitute a surface sub-layer that
    /// carries a land-area mask (issue #483), mirroring
    /// <see cref="VectorStackPayload.WithSubLayer"/>.
    /// </summary>
    /// <param name="subLayer">The replacement coverage sub-layer.</param>
    public CoverageStackPayload WithSubLayer(CoverageSubLayerBase subLayer) => new(Result, subLayer);
}

/// <summary>
/// A placeholder payload for a prebuilt layer that has no Mapsui-free portrayal
/// slice — used only by the viewer's legacy fallback path when a processor
/// hands the dataset loader raw layers without S-98 stack metadata. It carries
/// just a stable key so a renderer can recover the associated layer; it is
/// never produced by the headless composite path and is never suppressed.
/// </summary>
public sealed class SyntheticStackPayload : StackPayload
{
    /// <summary>
    /// Creates a synthetic payload with the given stable layer key.
    /// </summary>
    /// <param name="layerKey">Stable per-layer key within its source dataset.</param>
    public SyntheticStackPayload(string layerKey)
    {
        System.ArgumentNullException.ThrowIfNull(layerKey);
        LayerKey = layerKey;
    }

    /// <summary>Stable per-layer key within its source dataset.</summary>
    public string LayerKey { get; }
}
