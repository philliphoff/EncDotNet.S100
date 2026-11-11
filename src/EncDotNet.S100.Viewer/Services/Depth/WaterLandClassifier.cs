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
/// rules the route check uses too. S-401 cells and S-57 cells (translated into
/// the S-101 or S-401 model) count as S-101 (issue #1040). When no such area is present, S-102 coverage over
/// the point is taken as a positive water signal (bathymetry is only
/// collected over water); otherwise the result is
/// <see cref="WaterLandClass.Unknown"/> and the card is suppressed (avoiding
/// tide-over-land when only a blanketing S-104 grid is present).
/// </remarks>
internal sealed class WaterLandClassifier
{
    /// <summary>The products whose pick hits carry S-101 (or S-401) feature types.</summary>
    private static readonly HashSet<string> ChartSpecs = new(StringComparer.Ordinal) { "S-101", "S-401", "S-57" };

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
            if (hit.ProductSpec is null || !ChartSpecs.Contains(hit.ProductSpec))
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
