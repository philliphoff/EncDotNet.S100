using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// The assimilated depth picture for a picked location: the chosen base depth,
/// the S-104 water level applied to it (when one covers the point on the same
/// vertical datum) and the adjusted depth curve, the charted uncertainty, and
/// the reason when covering water level was refused.
/// </summary>
/// <param name="Base">The chosen base depth and its source.</param>
/// <param name="Tide">
/// The applied S-104 water level, or <c>null</c> when none covers the point,
/// none has a value there, or it was refused (<paramref name="Refusal"/>).
/// </param>
/// <param name="DepthOverTime">
/// The adjusted depth curve (base + water level at each step), ordered by
/// time; empty when <see cref="Tide"/> is <c>null</c>.
/// </param>
/// <param name="UncertaintyMeters">
/// The base-depth vertical uncertainty in metres, populated only when the base
/// is S-102 bathymetry; <c>null</c> otherwise.
/// </param>
/// <param name="Refusal">
/// Why covering S-104 water level was not applied, or <c>null</c> when it was
/// applied or none covers the point.
/// </param>
internal sealed record LocationDepthResult(
    BaseDepthResult Base,
    LocationTideSelection? Tide,
    IReadOnlyList<DepthOverTimePoint> DepthOverTime,
    double? UncertaintyMeters,
    WaterLevelRefusal? Refusal = null)
{
    /// <summary>
    /// The water level applied at <paramref name="utc"/>, interpolated between
    /// steps as the passage check reads it, or <c>null</c> when none applies
    /// at that time (never clamped to the first or last step).
    /// </summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The water level, or <c>null</c>.</returns>
    public WaterLevelSample? WaterLevelAt(DateTime utc) =>
        Tide is { Series.Count: > 0 } tide
        && WaterLevelSnapshot.Resolve(tide.Series, utc, Base.VerticalDatumCode) is { Status: WaterLevelStatus.Applied, Sample: { } sample }
            ? sample
            : null;

    /// <summary>
    /// Combines independent uncertainties, √(a² + b²), counting only the ones
    /// given (S-98 Appendix D-2); <c>null</c> when neither is.
    /// </summary>
    /// <param name="charted">The charted depth's uncertainty, or <c>null</c>.</param>
    /// <param name="waterLevel">The water level's uncertainty, or <c>null</c>.</param>
    /// <returns>The combined uncertainty, or <c>null</c>.</returns>
    public static double? CombineUncertainty(double? charted, double? waterLevel) =>
        (charted, waterLevel) switch
        {
            (null, null) => null,
            ({ } a, null) => a,
            (null, { } b) => b,
            ({ } a, { } b) => Math.Sqrt((a * a) + (b * b)),
        };
}
