using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Classifies a picked location as water or land so the depth-assimilation
/// card can be shown on water picks only (design visibility rule).
/// </summary>
/// <remarks>
/// The decision is driven by the S-101 skin-of-the-earth (group-1) area under
/// the pick, read directly from the already-resolved pick hits — no extra
/// dataset query. When no S-101 group-1 area is present, S-102 coverage over
/// the point is taken as a positive water signal (bathymetry is only
/// collected over water); otherwise the result is
/// <see cref="WaterLandClass.Unknown"/> and the card is suppressed (avoiding
/// tide-over-land when only a blanketing S-104 grid is present).
/// </remarks>
internal sealed class WaterLandClassifier
{
    /// <summary>Product specification code identifying S-101 pick hits.</summary>
    private const string S101Spec = "S-101";

    /// <summary>
    /// S-101 group-1 skin-of-the-earth feature types that denote water. A
    /// drying/intertidal area is a <c>DepthArea</c> with a negative
    /// <c>depthRangeMinimumValue</c>, so it classifies as water without
    /// special-casing.
    /// </summary>
    private static readonly HashSet<string> WaterAreas = new(StringComparer.Ordinal)
    {
        "DepthArea",
        "DredgedArea",
        "UnsurveyedArea",
    };

    /// <summary>
    /// S-101 group-1 skin-of-the-earth feature types that denote land.
    /// </summary>
    private static readonly HashSet<string> LandAreas = new(StringComparer.Ordinal)
    {
        "LandArea",
    };

    /// <summary>
    /// Classifies the pick.
    /// </summary>
    /// <param name="hits">The already-resolved pick hits.</param>
    /// <param name="s102CoversPoint">
    /// <c>true</c> when a loaded S-102 bathymetric coverage contains the pick
    /// point (the fallback water signal when no S-101 group-1 area is present).
    /// </param>
    /// <returns>The water/land classification.</returns>
    public WaterLandClass Classify(IReadOnlyList<PickHit> hits, bool s102CoversPoint)
    {
        ArgumentNullException.ThrowIfNull(hits);

        var hasWaterArea = false;
        var hasLandArea = false;
        foreach (var hit in hits)
        {
            if (!string.Equals(hit.ProductSpec, S101Spec, StringComparison.Ordinal))
            {
                continue;
            }

            if (WaterAreas.Contains(hit.FeatureType))
            {
                hasWaterArea = true;
            }
            else if (LandAreas.Contains(hit.FeatureType))
            {
                hasLandArea = true;
            }
        }

        // A water skin-of-the-earth area under the pick is the strongest
        // positive signal that depth data applies at the location, so it wins
        // over a co-picked land area at a coastline boundary.
        if (hasWaterArea)
        {
            return WaterLandClass.Water;
        }

        if (hasLandArea)
        {
            return WaterLandClass.Land;
        }

        return s102CoversPoint ? WaterLandClass.Water : WaterLandClass.Unknown;
    }
}
