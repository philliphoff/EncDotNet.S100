using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// The tidal datums a station catalogue keeps, by the names NOAA CO-OPS
/// uses, and their S-100 vertical datum codes.
/// </summary>
public static class TideDatumNames
{
    /// <summary>Mean higher high water (S-100 code 21).</summary>
    public const string MeanHigherHighWater = "MHHW";

    /// <summary>Mean high water (S-100 code 16).</summary>
    public const string MeanHighWater = "MHW";

    /// <summary>Mean sea level (S-100 code 3), the level harmonic constituents describe the tide about.</summary>
    public const string MeanSeaLevel = "MSL";

    /// <summary>Mean low water (S-100 code 5).</summary>
    public const string MeanLowWater = "MLW";

    /// <summary>Mean lower low water (S-100 code 12), the chart datum of US charts.</summary>
    public const string MeanLowerLowWater = "MLLW";

    /// <summary>Lowest astronomical tide (S-100 code 23).</summary>
    public const string LowestAstronomicalTide = "LAT";

    /// <summary>Highest astronomical tide (S-100 code 30).</summary>
    public const string HighestAstronomicalTide = "HAT";

    private static readonly (string Name, VerticalDatum Datum)[] Map =
    [
        (MeanHigherHighWater, VerticalDatum.MeanHigherHighWater),
        (MeanHighWater, VerticalDatum.MeanHighWater),
        (MeanSeaLevel, VerticalDatum.MeanSeaLevel),
        (MeanLowWater, VerticalDatum.MeanLowWater),
        (MeanLowerLowWater, VerticalDatum.MeanLowerLowWater),
        (LowestAstronomicalTide, VerticalDatum.LowestAstronomicalTide),
        (HighestAstronomicalTide, VerticalDatum.HighestAstronomicalTide),
    ];

    /// <summary>The names of the datums a catalogue keeps.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Map.Select(m => m.Name)];

    /// <summary>The S-100 vertical datum a datum name stands for.</summary>
    /// <param name="name">The datum's name, such as <c>MLLW</c>; matched without regard to case.</param>
    /// <returns>The vertical datum, or <c>null</c> when the name isn't one of <see cref="All"/>.</returns>
    public static VerticalDatum? ToVerticalDatum(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var (n, datum) in Map)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                return datum;
            }
        }

        return null;
    }

    /// <summary>The name of the datum for an S-100 vertical datum.</summary>
    /// <param name="datum">The vertical datum.</param>
    /// <returns>The name, such as <c>MLLW</c>, or <c>null</c> when a catalogue doesn't keep that datum.</returns>
    public static string? FromVerticalDatum(VerticalDatum datum)
    {
        foreach (var (name, d) in Map)
        {
            if (d == datum)
            {
                return name;
            }
        }

        return null;
    }
}
