using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Classifies a picked location as water or land so the depth-assimilation
/// card can be shown on water picks only (design visibility rule).
/// </summary>
/// <remarks>
/// The decision is driven by the S-101 skin-of-the-earth (group-1) area under
/// the pick, read directly from the already-resolved pick hits — no extra
/// dataset query — and classified by the shared <see cref="SkinOfTheEarth"/>
/// rules the route check uses too. When no S-101 group-1 area is present, S-102 coverage over
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

            switch (SkinOfTheEarth.Classify(hit.FeatureType))
            {
                case SkinOfTheEarthClass.Water:
                    hasWaterArea = true;
                    break;
                case SkinOfTheEarthClass.Land:
                    hasLandArea = true;
                    break;
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
