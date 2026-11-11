using EncDotNet.S100.DataModel;
namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Resolves the spatial objects a portrayal rule names with a
/// <c>SpatialReference</c> drawing command, so a line instruction can be drawn
/// along those curves only rather than the feature's whole geometry.
/// </summary>
/// <remarks>
/// S-100 Part 9a §9a-11.2.4: <c>SpatialReference:id[,forward]</c> restricts the
/// following drawing instructions to the referenced spatial object until
/// <c>ClearGeometry</c>. The S-101 portrayal catalogue uses it to draw, for
/// example, the safety contour on just the depth-area edges at the safety
/// depth. Like <see cref="IFeatureAnchorProvider"/>, this seam runs during
/// parse; products whose rules never emit spatial references supply none.
/// </remarks>
public interface ISpatialReferenceResolver
{
    /// <summary>
    /// Returns the coordinates of the curve <paramref name="spatialId"/> in its
    /// own (forward) direction, or <see langword="null"/> when the id names no
    /// curve.
    /// </summary>
    /// <param name="spatialId">The spatial id the host gave the rule (DEF-decoded).</param>
    IReadOnlyList<GeoPosition>? ResolveCurve(string spatialId);
}
