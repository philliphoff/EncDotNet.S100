using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A water level to add to charted depths across the chart (S-98 Appendix D,
/// water level adjustment). A depth processor that honours it portrays
/// <c>d + h</c> instead of the charted <c>d</c> wherever <see cref="At"/>
/// gives an <c>h</c>, and the charted depth everywhere else.
/// </summary>
/// <remarks>
/// <para>
/// Depths are metres below the vertical datum, positive down, and the water
/// level is metres above it, positive up, so the adjusted depth is
/// <c>d + h</c>. A negative <c>h</c> makes the water shallower.
/// </para>
/// <para>
/// The host decides which water level applies and when: the instant on the
/// Timeline, or the lowest over a period. Processors only read it. See
/// <c>docs/design/chart-wide-water-level-adjustment.md</c>.
/// </para>
/// </remarks>
public interface IDepthAdjustment
{
    /// <summary>
    /// What this adjustment applies (its sources, time or period, and datum
    /// policy), as a string. Two adjustments with the same key give the same
    /// result at every point, so it's safe to use in a memory cache key. It
    /// isn't stable across processes and shouldn't go into a disk cache.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Where a water level is available, before any datum check: the area
    /// whose edge the chart draws as the adjusted-area boundary.
    /// </summary>
    IReadOnlyList<CoverageArea> Footprint { get; }

    /// <summary>
    /// The water level to add to a charted depth on <paramref name="depthDatum"/>
    /// at a point, or <see langword="null"/> where none applies: outside the
    /// footprint or the data's time range, or on a vertical datum the
    /// adjustment refuses.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <param name="depthDatum">The S-100 vertical datum code of the charted depth, or <see langword="null"/> when it isn't declared.</param>
    /// <returns>The water level to add, or <see langword="null"/>.</returns>
    DepthAdjustmentSample? At(double latitude, double longitude, int? depthDatum);

    /// <summary>
    /// Whether a charted depth on <paramref name="depthDatum"/> can be
    /// adjusted at all: the datum check alone, without a position.
    /// </summary>
    /// <param name="depthDatum">The S-100 vertical datum code of the charted depth, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when every water level is refused for that datum.</returns>
    bool Accepts(int? depthDatum);
}

/// <summary>A water level that applies at a point.</summary>
/// <param name="HeightMeters">Metres above the vertical datum, positive up.</param>
/// <param name="UncertaintyMeters">Its vertical uncertainty in metres, when the data gives one.</param>
public readonly record struct DepthAdjustmentSample(double HeightMeters, double? UncertaintyMeters = null);
