using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Mapsui-free contract for resolving the default S-98 display plane of a
/// (product, feature-or-layer-kind) pair. This is the only part of the
/// cross-dataset interoperability authority that dataset processors need
/// (to stamp a plane onto each portrayal sub-layer); the Mapsui-typed
/// stack sorting and suppression live in the Mapsui renderer package.
/// </summary>
/// <remarks>
/// Splitting <c>GetDefaultPlane</c> out of the full interoperability
/// authority keeps the Pipelines assembly Mapsui-free: processors depend on
/// this interface, while the viewer drives the Mapsui-typed
/// <c>Sort</c> / <c>ApplyRules</c> stack policy separately.
/// </remarks>
public interface IDisplayPlaneAuthority
{
    /// <summary>
    /// Returns the default S-98 display plane for a given product
    /// specification and optional feature-type or sub-layer kind hint.
    /// </summary>
    /// <param name="productSpec">The product spec name (e.g. <c>"S-101"</c>).</param>
    /// <param name="featureTypeOrLayerKind">Optional sub-layer kind / feature-type hint.</param>
    /// <returns>The default plane for the pair.</returns>
    S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null);
}
