using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S129.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// Convenience helpers that fuse an <see cref="S129ControlPoint"/> with
/// a resolved S-102 or S-104 coverage in a single call.
/// </summary>
/// <remarks>
/// All helpers are pure data accessors built on top of
/// <see cref="S129BathymetryFusion"/> and
/// <see cref="S129WaterLevelFusion"/>. They never produce drawing
/// instructions and never depend on any rendering library.
/// </remarks>
public static class S129PlanFusion
{
    /// <summary>
    /// Samples the supplied S-102 coverage at the control point's
    /// position. Returns <c>null</c> when the control point has no
    /// position or the position falls outside the coverage.
    /// </summary>
    public static S129BathymetrySample? SampleBathymetryAt(
        S129ControlPoint controlPoint,
        S102CoverageSource bathymetry)
    {
        ArgumentNullException.ThrowIfNull(controlPoint);
        ArgumentNullException.ThrowIfNull(bathymetry);

        if (controlPoint.Position is not { } pos) return null;
        return S129BathymetryFusion.Sample(bathymetry, pos);
    }

    /// <summary>
    /// Samples the supplied S-104 coverage at the control point's
    /// position and <see cref="S129ControlPoint.ExpectedPassingTime"/>.
    /// Returns <c>null</c> when the control point has no position, no
    /// expected-passing-time, or the position falls outside the
    /// coverage.
    /// </summary>
    public static S129WaterLevelSample? SampleWaterLevelAt(
        S129ControlPoint controlPoint,
        S104CoverageSource waterLevel)
    {
        ArgumentNullException.ThrowIfNull(controlPoint);
        ArgumentNullException.ThrowIfNull(waterLevel);

        if (controlPoint.Position is not { } pos) return null;
        if (controlPoint.ExpectedPassingTime is not { } t) return null;
        return S129WaterLevelFusion.Sample(waterLevel, pos, t);
    }

    /// <summary>
    /// Samples the supplied S-104 coverage at the control point's
    /// position and an explicit <paramref name="time"/>. Use this
    /// overload to query a control point's location at a time other
    /// than its expected passing time (e.g. when querying a timeline
    /// snapshot).
    /// </summary>
    public static S129WaterLevelSample? SampleWaterLevelAt(
        S129ControlPoint controlPoint,
        S104CoverageSource waterLevel,
        DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(controlPoint);
        ArgumentNullException.ThrowIfNull(waterLevel);

        if (controlPoint.Position is not { } pos) return null;
        return S129WaterLevelFusion.Sample(waterLevel, pos, time);
    }
}
