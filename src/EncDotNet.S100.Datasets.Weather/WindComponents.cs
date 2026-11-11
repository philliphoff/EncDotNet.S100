using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Wind speed and direction from the eastward (U) and northward (V) wind
/// components (design note weather-sources.md §3.5).
/// </summary>
/// <remarks>
/// Direction follows the meteorological convention: the direction the wind
/// blows <em>from</em>, in degrees clockwise from true north, in [0, 360).
/// It's <c>atan2(−u, −v)</c>, so a wind with <c>v &lt; 0</c> (blowing south)
/// comes from 0° (north), and one with <c>u &gt; 0</c> (blowing east) comes
/// from 270° (west). A calm wind (<c>u = v = 0</c>) has direction 0°.
/// </remarks>
public static class WindComponents
{
    private const double DegreesPerRadian = 180.0 / Math.PI;

    /// <summary>The wind speed, <c>√(u² + v²)</c>.</summary>
    /// <param name="u">The eastward component.</param>
    /// <param name="v">The northward component.</param>
    public static Speed GetSpeed(Speed u, Speed v) =>
        Speed.FromMetresPerSecond(Magnitude(u.TotalMetresPerSecond, v.TotalMetresPerSecond));

    /// <summary>The direction the wind blows from, in degrees true, in [0, 360).</summary>
    /// <param name="u">The eastward component.</param>
    /// <param name="v">The northward component.</param>
    public static Angle GetFromDirection(Speed u, Speed v) =>
        Angle.FromDegrees(FromDirectionDegrees(u.TotalMetresPerSecond, v.TotalMetresPerSecond));

    /// <summary>The wind speed from components in metres per second, for bulk grids.</summary>
    internal static double Magnitude(double u, double v) => Math.Sqrt(u * u + v * v);

    /// <summary>The "from" direction in degrees true, in [0, 360), for bulk grids.</summary>
    internal static double FromDirectionDegrees(double u, double v)
    {
        if (u == 0 && v == 0)
            return 0;

        double degrees = Math.Atan2(-u, -v) * DegreesPerRadian;
        if (degrees < 0)
            degrees += 360;
        return degrees >= 360 ? 0 : degrees;
    }

    /// <summary>A <see cref="EncDotNet.S100.Pipelines.Coverage.CoverageFieldComputation"/> for speed from (u, v).</summary>
    internal static float SpeedCell(ReadOnlySpan<float> inputs) => (float)Magnitude(inputs[0], inputs[1]);

    /// <summary>A <see cref="EncDotNet.S100.Pipelines.Coverage.CoverageFieldComputation"/> for direction from (u, v).</summary>
    internal static float FromDirectionCell(ReadOnlySpan<float> inputs)
    {
        // Rounding to float can turn 359.99999 into 360.
        float degrees = (float)FromDirectionDegrees(inputs[0], inputs[1]);
        return degrees >= 360f ? 0f : degrees;
    }
}
