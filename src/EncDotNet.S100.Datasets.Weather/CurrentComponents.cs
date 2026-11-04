using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Current speed and direction from the eastward (U) and northward (V)
/// components of the sea water velocity.
/// </summary>
/// <remarks>
/// Direction follows the oceanographic convention, as S-111 does: the
/// direction the water flows <em>to</em>, in degrees clockwise from true
/// north, in [0, 360). It's <c>atan2(u, v)</c>, so a current with
/// <c>u &gt; 0</c> (flowing east) sets to 90°. A slack current
/// (<c>u = v = 0</c>) has direction 0°. Wind and wave directions are the
/// other way round (<see cref="WindComponents"/>).
/// </remarks>
public static class CurrentComponents
{
    private const double DegreesPerRadian = 180.0 / Math.PI;

    /// <summary>The current speed, <c>√(u² + v²)</c>.</summary>
    /// <param name="u">The eastward component.</param>
    /// <param name="v">The northward component.</param>
    public static Speed GetSpeed(Speed u, Speed v) =>
        Speed.FromMetresPerSecond(WindComponents.Magnitude(u.TotalMetresPerSecond, v.TotalMetresPerSecond));

    /// <summary>The direction the current flows to, in degrees true, in [0, 360).</summary>
    /// <param name="u">The eastward component.</param>
    /// <param name="v">The northward component.</param>
    public static Angle GetToDirection(Speed u, Speed v) =>
        Angle.FromDegrees(ToDirectionDegrees(u.TotalMetresPerSecond, v.TotalMetresPerSecond));

    /// <summary>The "to" direction in degrees true, in [0, 360), for bulk grids.</summary>
    internal static double ToDirectionDegrees(double u, double v)
    {
        if (u == 0 && v == 0)
            return 0;

        double degrees = Math.Atan2(u, v) * DegreesPerRadian;
        if (degrees < 0)
            degrees += 360;
        return degrees >= 360 ? 0 : degrees;
    }

    /// <summary>A <see cref="EncDotNet.S100.Pipelines.Coverage.CoverageFieldComputation"/> for direction from (u, v).</summary>
    internal static float ToDirectionCell(ReadOnlySpan<float> inputs)
    {
        // Rounding to float can turn 359.99999 into 360.
        float degrees = (float)ToDirectionDegrees(inputs[0], inputs[1]);
        return degrees >= 360f ? 0f : degrees;
    }
}
