using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The 10 m wind speed (<c>wind.speed</c>) as a colour fill in Beaufort
/// bands, force 0 to 12. Off by default: the barbs already carry the speed.
/// </summary>
/// <remarks>
/// <para>
/// The bands start at the Beaufort scale's lower limits in knots
/// (<see cref="BeaufortLowerKnots"/>): force 1 is 1–3 kn, force 2 is 4–6 kn
/// and so on, and force 12 is 64 kn and higher. Between whole knots a value
/// takes the higher force from its lower limit, so 3.6 kn is force 1 and
/// 4 kn is force 2.
/// </para>
/// <para>
/// The bands are defined in knots and converted to metres per second, the
/// unit <c>wind.speed</c> is stored in (whether the file stores it or the
/// source derives it from U and V), so the field keeps one canonical unit
/// for sampling and picking, and no second, knot-valued copy of the grid is
/// made. <see cref="WeatherBand.Label"/>s are in knots.
/// </para>
/// </remarks>
public sealed class WindSpeedStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.wind.speed"</c>.</summary>
    public const string Key = "weather.wind.speed";

    /// <summary>The lower limit of each Beaufort force from 0 to 12, in knots.</summary>
    public static IReadOnlyList<int> BeaufortLowerKnots { get; } = [0, 1, 4, 7, 11, 17, 22, 28, 34, 41, 48, 56, 64];

    /// <summary>The bands, force 0 first, in metres per second.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } = BuildBands();

    /// <summary>Creates the style, with the Day palette active.</summary>
    public WindSpeedStyle()
        : base(Key, "Wind speed", WeatherLayerKind.Fill, WeatherParameters.WindSpeed, null, isVisibleByDefault: false)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);

    /// <summary>The Beaufort force of a wind speed, 0 to 12.</summary>
    /// <param name="speed">The speed.</param>
    public static int BeaufortForce(Speed speed)
    {
        double knots = speed.TotalKnots;
        int force = 0;
        for (int i = 1; i < BeaufortLowerKnots.Count; i++)
        {
            if (knots >= BeaufortLowerKnots[i])
                force = i;
        }

        return force;
    }

    private static WeatherBand[] BuildBands()
    {
        var bands = new WeatherBand[BeaufortLowerKnots.Count];
        for (int force = 0; force < bands.Length; force++)
        {
            int lower = BeaufortLowerKnots[force];
            bool top = force == bands.Length - 1;
            float minimum = force == 0 ? 0f : (float)Speed.FromKnots(lower).TotalMetresPerSecond;
            float maximum = top ? float.PositiveInfinity : (float)Speed.FromKnots(BeaufortLowerKnots[force + 1]).TotalMetresPerSecond;
            string range = force switch
            {
                0 => "under 1 kn",
                _ when top => string.Create(CultureInfo.InvariantCulture, $"{lower} kn and higher"),
                _ => string.Create(CultureInfo.InvariantCulture, $"{lower}–{BeaufortLowerKnots[force + 1] - 1} kn"),
            };
            bands[force] = new WeatherBand(
                minimum,
                maximum,
                WeatherPalette.WindSpeedToken(force),
                string.Create(CultureInfo.InvariantCulture, $"Force {force}, {range}"));
        }

        return bands;
    }
}
