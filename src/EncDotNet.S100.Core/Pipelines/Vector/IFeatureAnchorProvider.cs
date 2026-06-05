namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Supplies the primary point anchor for a feature during drawing-instruction
/// parsing. Used by <see cref="DrawingInstructionParser"/> to tessellate
/// augmented line geometry (<c>AugmentedRay</c>, <c>ArcByRadius</c>,
/// <c>AugmentedPath</c>) whose geodesic origin is the feature's point position
/// (e.g. sector lights, all-around lights).
/// </summary>
/// <remarks>
/// This seam runs <i>during</i> parse, distinct from
/// <see cref="IDrawingInstructionTransform"/> which runs <i>after</i> parse on
/// already-typed instructions. Products without augmented line geometry (e.g.
/// S-131) supply no anchor provider.
/// </remarks>
public interface IFeatureAnchorProvider
{
    /// <summary>
    /// Returns the (latitude, longitude) of the feature's primary point
    /// geometry, or <see langword="null"/> if the feature has no point anchor.
    /// </summary>
    /// <param name="featureRef">Feature reference identifier.</param>
    (double Latitude, double Longitude)? GetAnchor(string featureRef);
}
