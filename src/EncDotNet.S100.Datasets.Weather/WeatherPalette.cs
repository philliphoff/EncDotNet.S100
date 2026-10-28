using System.Globalization;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The colours of the weather styles: the band fills of
/// <see cref="WaveHeightStyle"/> and <see cref="WindSpeedStyle"/>, and the
/// inks of the wind barbs, wave arrows and pressure isobars, each with a Day,
/// Dusk and Night value.
/// </summary>
/// <remarks>
/// <para>
/// No weather format has a portrayal catalogue, so these are SoundCharts'
/// own colours. They keep clear of the hues S-101 already uses: blues and
/// greens (depth areas), red and amber (danger), magenta (mariner
/// information) and the S-104 teal. Wave height is a violet ramp
/// (OKLCH hue 295); wind speed, off by default, is a low-chroma sepia ramp
/// (OKLCH hue 55), well short of the saturated amber of a danger colour.
/// Both are sequential: in Day the higher bands are darker, in Dusk and
/// Night they are lighter against the dark chart. The fills are drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>, so the chart under them
/// stays readable.
/// </para>
/// <para>
/// Every Night colour stays under relative luminance 0.2, as the S-104
/// style's do.
/// </para>
/// </remarks>
public static class WeatherPalette
{
    /// <summary>Colour token of the wind barb ink (staff, feathers, station dot and calm circle).</summary>
    public const string BarbInkToken = "WXBARB";

    /// <summary>Colour token of the wave direction arrow.</summary>
    public const string WaveArrowToken = "WXWAVE";

    /// <summary>Colour token of the pressure isobars and their labels (<see cref="PressureIsobarStyle"/>).</summary>
    public const string IsobarInkToken = "WXISOBAR";

    /// <summary>Colour token of the halo under the isobar labels: the chart's background.</summary>
    public const string IsobarHaloToken = "WXISOHALO";

    /// <summary>The colour token prefix of the wave height bands, followed by the band number: <c>WXHS01</c> to <c>WXHS15</c>.</summary>
    public const string WaveHeightTokenPrefix = "WXHS";

    /// <summary>The colour token prefix of the Beaufort wind speed bands, followed by the force: <c>WXBF00</c> to <c>WXBF12</c>.</summary>
    public const string WindSpeedTokenPrefix = "WXBF";

    // Day, Dusk, Night for each wave height band (WXHS01 lowest).
    private static readonly (string Day, string Dusk, string Night)[] WaveHeightColors =
    [
        ("#F3EEFF", "#494260", "#171322"),
        ("#E5DFFC", "#524A6B", "#1B1728"),
        ("#D7D0F4", "#5A5177", "#201B2F"),
        ("#CAC1EB", "#635983", "#241F35"),
        ("#BDB2E3", "#6C608F", "#29233C"),
        ("#B0A3DA", "#75689B", "#2E2743"),
        ("#A394D2", "#7E70A7", "#332B4A"),
        ("#9786C9", "#8778B4", "#382F51"),
        ("#8A77C0", "#9080C0", "#3D3358"),
        ("#7E69B8", "#9A88CD", "#42375F"),
        ("#735AAF", "#A391DA", "#473B67"),
        ("#674BA6", "#AD99E7", "#4C406E"),
        ("#5C3C9D", "#B6A2F4", "#514475"),
        ("#522C93", "#C0AAFF", "#57487D"),
        ("#48198A", "#CAB3FF", "#5C4D85"),
    ];

    // Day, Dusk, Night for each Beaufort force (WXBF00 calm).
    private static readonly (string Day, string Dusk, string Night)[] WindSpeedColors =
    [
        ("#FAEFE8", "#51453E", "#1A1512"),
        ("#EEDFD6", "#5C4E45", "#201915"),
        ("#E1CFC3", "#68574C", "#261E19"),
        ("#D5BFB1", "#746053", "#2D231C"),
        ("#C9B0A0", "#80695B", "#34281F"),
        ("#BCA08E", "#8C7362", "#3B2C23"),
        ("#B0917D", "#997C6A", "#423127"),
        ("#A4826C", "#A68671", "#49362A"),
        ("#98735B", "#B39079", "#503C2E"),
        ("#8C654A", "#C09A81", "#574132"),
        ("#80573A", "#CDA489", "#5F4635"),
        ("#744929", "#DBAE91", "#674B39"),
        ("#683B18", "#E9B999", "#6E513D"),
    ];

    /// <summary>
    /// The weather colours: <see cref="BarbInkToken"/>, <see cref="WaveArrowToken"/>,
    /// <see cref="IsobarInkToken"/>, <see cref="IsobarHaloToken"/>,
    /// the wave height bands (<see cref="WaveHeightToken"/>) and the Beaufort
    /// bands (<see cref="WindSpeedToken"/>).
    /// </summary>
    public static ApplicationPalette Palette { get; } = new("Weather", BuildTokens());

    /// <summary>The number of wave height bands: 15.</summary>
    public static int WaveHeightBandCount => WaveHeightColors.Length;

    /// <summary>The number of Beaufort bands, force 0 to 12: 13.</summary>
    public static int WindSpeedBandCount => WindSpeedColors.Length;

    /// <summary>The colour token of wave height band <paramref name="band"/>, 1 (lowest) to <see cref="WaveHeightBandCount"/>.</summary>
    /// <param name="band">The band number.</param>
    public static string WaveHeightToken(int band)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(band, WaveHeightBandCount);
        return WaveHeightTokenPrefix + band.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>The colour token of Beaufort force <paramref name="force"/>, 0 to 12.</summary>
    /// <param name="force">The Beaufort force.</param>
    public static string WindSpeedToken(int force)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(force);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(force, WindSpeedBandCount - 1);
        return WindSpeedTokenPrefix + force.ToString("00", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, PaletteColors> BuildTokens()
    {
        var tokens = new Dictionary<string, PaletteColors>(StringComparer.Ordinal)
        {
            // Near-black slate by day, light grey at dusk, dim grey at night.
            [BarbInkToken] = PaletteColors.FromHex(day: "#222630", dusk: "#D4D7DE", night: "#5F636C"),
            // The violet of the wave height ramp, darker than its top band.
            [WaveArrowToken] = PaletteColors.FromHex(day: "#462281", dusk: "#C7BAF3", night: "#615486"),
            // A neutral grey-blue for the isobars, lighter than the barb ink so
            // the barbs stay on top visually; the halo is the chart's paper.
            [IsobarInkToken] = PaletteColors.FromHex(day: "#4A5160", dusk: "#BFC4CE", night: "#555A63"),
            [IsobarHaloToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4250", night: "#0B0E14"),
        };

        for (int i = 0; i < WaveHeightColors.Length; i++)
        {
            var (day, dusk, night) = WaveHeightColors[i];
            tokens[WaveHeightToken(i + 1)] = PaletteColors.FromHex(day, dusk, night);
        }

        for (int i = 0; i < WindSpeedColors.Length; i++)
        {
            var (day, dusk, night) = WindSpeedColors[i];
            tokens[WindSpeedToken(i)] = PaletteColors.FromHex(day, dusk, night);
        }

        return tokens;
    }
}
