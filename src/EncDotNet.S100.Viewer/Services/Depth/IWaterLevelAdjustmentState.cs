using EncDotNet.S100.Collections;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Whether chart-wide water level adjustment is on, and what the drawn chart
/// applies. The seam the depth basis indicator (#1191) reads and chart-wide
/// adjustment (#1132) implements in <see cref="ChartWaterLevelAdjustmentService"/>.
/// </summary>
/// <remarks>
/// This state says what was applied; whether that adjusts the depths in a
/// view (datums, coverage, time range) is <see cref="DepthBasisEvaluator"/>'s
/// decision, so the two can't disagree. See
/// <c>docs/design/depth-basis-indicator.md</c> §2.4 and
/// <c>docs/design/chart-wide-water-level-adjustment.md</c> §7.
/// </remarks>
internal interface IWaterLevelAdjustmentState
{
    /// <summary>True when the mariner has turned chart adjustment on.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// What the drawn chart applies, or <see langword="null"/> when adjustment
    /// is off.
    /// </summary>
    ChartAdjustmentState? Current { get; }

    /// <summary>Raised when the setting or the applied result changes.</summary>
    event EventHandler? Changed;
}

/// <summary>The water level adjustment setting (<c>docs/design/chart-wide-water-level-adjustment.md</c> §6).</summary>
internal enum WaterLevelAdjustmentMode
{
    /// <summary>The chart shows charted depths.</summary>
    Off,

    /// <summary>The chart adds the S-104 water level at the Timeline's view time.</summary>
    AtViewTime,
}

/// <summary>What chart-wide adjustment applies to the drawn layers.</summary>
/// <param name="Mode">The mariner's setting.</param>
/// <param name="AppliedTime">
/// The time the drawn layers are for, or <see langword="null"/> before the
/// first adjusted layers land. While the Timeline is dragged this stays on the
/// last completed adjustment, so a time is never shown over another time's depths.
/// </param>
/// <param name="Sources">The S-104 datasets the water level comes from.</param>
/// <param name="Footprint">The areas where they give a water level (WGS-84): the adjusted-area boundary.</param>
/// <param name="ApplyUndeclaredDatum">The "Apply anyway when datum is not declared" override.</param>
internal sealed record ChartAdjustmentState(
    WaterLevelAdjustmentMode Mode,
    DateTime? AppliedTime,
    IReadOnlyList<WaterLevelSourceInfo> Sources,
    IReadOnlyList<EncDotNet.S100.DataModel.CoverageArea> Footprint,
    bool ApplyUndeclaredDatum)
{
    /// <summary>Whether a datum agreement lets the water level apply under this state's rule.</summary>
    /// <param name="agreement">The agreement between a depth's datum and a water level's.</param>
    /// <returns><see langword="true"/> when it applies.</returns>
    public bool Permits(DatumAgreement agreement) => agreement switch
    {
        DatumAgreement.Same => true,
        DatumAgreement.Unknown => ApplyUndeclaredDatum,
        _ => false,
    };

    /// <summary>The bounds of each <see cref="Footprint"/> area.</summary>
    public IReadOnlyList<GeoBounds> FootprintBounds { get; } = [.. Footprint.Select(BoundsOf)];

    private static GeoBounds BoundsOf(EncDotNet.S100.DataModel.CoverageArea area)
    {
        double south = 90, north = -90, west = 180, east = -180;
        foreach (var p in area.ExteriorRing)
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }

        return new GeoBounds(south, west, north, east);
    }
}

/// <summary>Chart adjustment that is always off.</summary>
internal sealed class NoWaterLevelAdjustment : IWaterLevelAdjustmentState
{
    /// <summary>The shared instance.</summary>
    public static NoWaterLevelAdjustment Instance { get; } = new();

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public ChartAdjustmentState? Current => null;

    /// <inheritdoc />
    public event EventHandler? Changed { add { } remove { } }
}
