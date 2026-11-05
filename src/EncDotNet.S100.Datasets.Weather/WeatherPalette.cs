using System.Globalization;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The colours of the weather styles: the band fills of
/// <see cref="WaveHeightStyle"/>, <see cref="WindSpeedStyle"/>,
/// <see cref="SeaTemperatureStyle"/>, <see cref="VisibilityStyle"/> and
/// <see cref="FreezingSprayStyle"/>, and the inks of the wind barbs, wave
/// arrows, pressure isobars and isotherms, each with a Day, Dusk and Night
/// value.
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
/// Night they are lighter against the dark chart. Sea surface temperature is
/// diverging, as temperature maps conventionally are: slate blue (OKLCH hue
/// 262) for the coldest water, paling to near white at 15 °C, then pale
/// yellow (hue 98) through orange to brick (hue 30) for the warmest. The warm
/// side turns in hue as well as lightness, so each degree between 15 °C and
/// 30 °C, where the Gulf Stream and other fronts mariners look for lie, is a
/// visibly different colour. Lightness runs from the ends to the middle:
/// lightest at 15 °C by day, darkest at dusk and night. This ramp is the
/// exception to the hue rule above: SST is read by its colour convention,
/// and the fill is drawn at half opacity, so its orange and brick don't
/// stand alone as danger colours. The fills are drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>, so the chart under them
/// stays readable. Visibility is a neutral grey ramp, the colour of fog: in
/// Day the poorer bands are darker, in Dusk and Night lighter, and there's
/// no band for good visibility, so clear areas aren't tinted at all.
/// Freezing spray is a muted plum ramp (OKLCH hue about 325), redder and
/// greyer than the wave violet and far less saturated than S-101 magenta:
/// in Day the heavier classes are darker, in Dusk and Night lighter. The air
/// isotherms are a warm grey, set apart from the sea isotherms' neutral
/// grey and dashed.
/// </para>
/// <para>
/// Every Night colour stays under relative luminance 0.2, as the S-104
/// style's do.
/// </para>
/// <para>
/// The current arrows (<see cref="CurrentArrowStyle"/>) don't follow these
/// rules: they take S-111's own speed band colours, so a current reads the
/// same in a model as in an S-111 dataset.
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

    /// <summary>The colour token prefix of the sea surface temperature bands, followed by the band number: <c>WXSST01</c> to <c>WXSST32</c>.</summary>
    public const string SeaTemperatureTokenPrefix = "WXSST";

    /// <summary>Colour token of the isotherms and their labels (<see cref="SeaTemperatureIsothermStyle"/>).</summary>
    public const string IsothermInkToken = "WXISOTH";

    /// <summary>The colour token prefix of the current speed bands, followed by the band number: <c>WXCUR1</c> to <c>WXCUR9</c>.</summary>
    public const string CurrentTokenPrefix = "WXCUR";

    /// <summary>The colour token prefix of the freezing spray bands, followed by the class: <c>WXICE1</c> (light) to <c>WXICE4</c> (extreme).</summary>
    public const string FreezingSprayTokenPrefix = "WXICE";

    /// <summary>Colour token of the air temperature isotherms and their labels (<see cref="AirTemperatureIsothermStyle"/>).</summary>
    public const string AirIsothermInkToken = "WXAIRTH";

    /// <summary>The colour token prefix of the visibility bands, followed by the band number: <c>WXVIS1</c> (very poor) to <c>WXVIS3</c> (moderate).</summary>
    public const string VisibilityTokenPrefix = "WXVIS";

    /// <summary>Colour token of the outline of the current arrows (<see cref="CurrentArrowStyle"/>).</summary>
    public const string CurrentOutlineToken = "WXCUROUT";

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

    // Day, Dusk, Night for each sea surface temperature band (WXSST01 coldest).
    private static readonly (string Day, string Dusk, string Night)[] SeaTemperatureColors =
    [
        ("#345FB2", "#81AAF7", "#3D5787"),
        ("#3B69B8", "#78A3EA", "#38537F"),
        ("#4273BD", "#709CDE", "#324E77"),
        ("#4A7DC3", "#6995D2", "#2D496F"),
        ("#5387C9", "#618EC6", "#294467"),
        ("#5D91CE", "#5A86BA", "#243F5F"),
        ("#679AD3", "#547FAE", "#203B57"),
        ("#71A4D8", "#4E78A3", "#1C3650"),
        ("#7CAEDD", "#487197", "#183148"),
        ("#87B7E2", "#43698B", "#152C40"),
        ("#93C0E6", "#3E6280", "#122839"),
        ("#A0CAEB", "#3A5B74", "#0F2332"),
        ("#ADD3EF", "#365469", "#0D1E2A"),
        ("#BBDCF3", "#334C5E", "#0B1A23"),
        ("#C9E5F7", "#304553", "#0A151C"),
        ("#D9EDFA", "#2E3E47", "#091015"),
        ("#F6EDCA", "#413B25", "#130F04"),
        ("#F3E1B4", "#4D4122", "#1A1302"),
        ("#F0D59F", "#59471E", "#211600"),
        ("#EDC88A", "#674C19", "#281900"),
        ("#EBBB76", "#745115", "#2F1D00"),
        ("#E8AE64", "#825511", "#372000"),
        ("#E6A052", "#915A10", "#3F2300"),
        ("#E29242", "#9F5E13", "#472601"),
        ("#DE8434", "#AC621B", "#502801"),
        ("#D97728", "#BA6726", "#5A2A01"),
        ("#D2691F", "#C76C32", "#642C01"),
        ("#CB5C1A", "#D37140", "#6C2E0A"),
        ("#C25019", "#DE774E", "#733217"),
        ("#B9441B", "#E97E5D", "#7A3721"),
        ("#AF381D", "#F3856C", "#813B2B"),
        ("#A52B1E", "#FD8C7B", "#874035"),
    ];

    // Day, Dusk, Night for each current speed band (WXCUR1 slowest): the
    // S-111 Ed 2.0.0 colour profile's SCBN1 to SCBN9, so a speed reads the
    // same colour in model currents as in S-111.
    private static readonly (string Day, string Dusk, string Night)[] CurrentColors =
    [
        ("#7652E2", "#51379F", "#1A0F3B"),
        ("#4898D3", "#14344C", "#04111C"),
        ("#61CBE5", "#13333A", "#030E11"),
        ("#6DBC45", "#407227", "#132808"),
        ("#B4DC00", "#6E8800", "#263100"),
        ("#CDC100", "#7E7700", "#2D2A00"),
        ("#F8A718", "#936101", "#362100"),
        ("#F7A29D", "#563533", "#211111"),
        ("#FF1E1E", "#B20101", "#3F0000"),
    ];

    // Day, Dusk, Night for each visibility band (WXVIS1 very poor): neutral
    // greys, cool by a few degrees so they don't read as the chart's paper.
    private static readonly (string Day, string Dusk, string Night)[] VisibilityColors =
    [
        ("#5B606B", "#A9AEB8", "#4A4E57"),
        ("#8B909A", "#7D828C", "#363A42"),
        ("#BCC0C7", "#565B65", "#25282E"),
    ];

    // Day, Dusk, Night for each freezing spray class (WXICE1 light).
    private static readonly (string Day, string Dusk, string Night)[] FreezingSprayColors =
    [
        ("#D8C3DA", "#4E3A52", "#2D2030"),
        ("#B78ABD", "#6D4E73", "#3C2A41"),
        ("#91579A", "#946F9B", "#513957"),
        ("#652873", "#C19BC8", "#6A4B71"),
    ];

    /// <summary>
    /// The weather colours: <see cref="BarbInkToken"/>, <see cref="WaveArrowToken"/>,
    /// <see cref="IsobarInkToken"/>, <see cref="IsobarHaloToken"/>,
    /// <see cref="IsothermInkToken"/>, the wave height bands
    /// (<see cref="WaveHeightToken"/>), the Beaufort bands
    /// (<see cref="WindSpeedToken"/>), the sea surface temperature bands
    /// (<see cref="SeaTemperatureToken"/>), and the current speed bands
    /// (<see cref="CurrentToken"/>) with <see cref="CurrentOutlineToken"/>,
    /// the visibility bands (<see cref="VisibilityToken"/>), the freezing spray
    /// bands (<see cref="FreezingSprayToken"/>) and <see cref="AirIsothermInkToken"/>.
    /// </summary>
    public static ApplicationPalette Palette { get; } = new("Weather", BuildTokens());

    /// <summary>The number of wave height bands: 15.</summary>
    public static int WaveHeightBandCount => WaveHeightColors.Length;

    /// <summary>The number of Beaufort bands, force 0 to 12: 13.</summary>
    public static int WindSpeedBandCount => WindSpeedColors.Length;

    /// <summary>The number of sea surface temperature bands: 32.</summary>
    public static int SeaTemperatureBandCount => SeaTemperatureColors.Length;

    /// <summary>The number of current speed bands: 9, as S-111's.</summary>
    public static int CurrentBandCount => CurrentColors.Length;

    /// <summary>The number of freezing spray bands: 4 (light, moderate, heavy and extreme).</summary>
    public static int FreezingSprayBandCount => FreezingSprayColors.Length;

    /// <summary>The number of visibility bands: 3 (very poor, poor and moderate).</summary>
    public static int VisibilityBandCount => VisibilityColors.Length;

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

    /// <summary>The colour token of sea surface temperature band <paramref name="band"/>, 1 (coldest) to <see cref="SeaTemperatureBandCount"/>.</summary>
    /// <param name="band">The band number.</param>
    public static string SeaTemperatureToken(int band)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(band, SeaTemperatureBandCount);
        return SeaTemperatureTokenPrefix + band.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>The colour token of current speed band <paramref name="band"/>, 1 (slowest) to <see cref="CurrentBandCount"/>.</summary>
    /// <param name="band">The band number.</param>
    public static string CurrentToken(int band)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(band, CurrentBandCount);
        return CurrentTokenPrefix + band.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The colour token of visibility band <paramref name="band"/>, 1 (very poor) to <see cref="VisibilityBandCount"/>.</summary>
    /// <param name="band">The band number.</param>
    public static string VisibilityToken(int band)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(band, VisibilityBandCount);
        return VisibilityTokenPrefix + band.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The colour token of freezing spray band <paramref name="band"/>, 1 (light) to <see cref="FreezingSprayBandCount"/>.</summary>
    /// <param name="band">The band number.</param>
    public static string FreezingSprayToken(int band)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(band, FreezingSprayBandCount);
        return FreezingSprayTokenPrefix + band.ToString(CultureInfo.InvariantCulture);
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
            // A dark neutral grey for the isotherms, so they read over both
            // ends of the temperature fill; it shares the isobar label halo.
            [IsothermInkToken] = PaletteColors.FromHex(day: "#3A3D44", dusk: "#E2E4E8", night: "#6A6D74"),
            // The current arrows' outline: S-111's CHBLK, so model and S-111
            // arrows of the same speed look alike.
            [CurrentOutlineToken] = PaletteColors.FromHex(day: "#000000", dusk: "#6B7F89", night: "#252D31"),
            // A warm grey for the dashed air isotherms; it shares the isobar label halo.
            [AirIsothermInkToken] = PaletteColors.FromHex(day: "#5A4A40", dusk: "#E3D6CC", night: "#6E6158"),
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

        for (int i = 0; i < SeaTemperatureColors.Length; i++)
        {
            var (day, dusk, night) = SeaTemperatureColors[i];
            tokens[SeaTemperatureToken(i + 1)] = PaletteColors.FromHex(day, dusk, night);
        }

        for (int i = 0; i < CurrentColors.Length; i++)
        {
            var (day, dusk, night) = CurrentColors[i];
            tokens[CurrentToken(i + 1)] = PaletteColors.FromHex(day, dusk, night);
        }

        for (int i = 0; i < FreezingSprayColors.Length; i++)
        {
            var (day, dusk, night) = FreezingSprayColors[i];
            tokens[FreezingSprayToken(i + 1)] = PaletteColors.FromHex(day, dusk, night);
        }

        for (int i = 0; i < VisibilityColors.Length; i++)
        {
            var (day, dusk, night) = VisibilityColors[i];
            tokens[VisibilityToken(i + 1)] = PaletteColors.FromHex(day, dusk, night);
        }

        return tokens;
    }
}
