using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// The optional relief shading of the S-102 surface: a hillshade from the
/// depths, drawn over the band colours when the display switch
/// <see cref="LayerKey"/> is on (<see cref="EcdisDisplaySettings.LayerVisibility"/>).
/// Off by default: it is not part of the S-102 portrayal.
/// </summary>
public static class S102Relief
{
    /// <summary>The display switch key for S-102 relief shading.</summary>
    public const string LayerKey = "s102.relief";

    /// <summary>Whether relief shading is on under <paramref name="display"/>; off when unset or headless with no settings.</summary>
    /// <param name="display">The display settings, or <see langword="null"/>.</param>
    public static bool IsOn(EcdisDisplaySettings? display) =>
        display?.IsLayerVisible(LayerKey, defaultVisible: false) ?? false;

    /// <summary>The label of the legend row that marks relief shading as a display aid.</summary>
    public const string LegendLabel = "Relief shading (display aid)";

    /// <summary>
    /// The S-102 legend row shown while relief shading is on: a swatch of
    /// <paramref name="color"/>, lit from the north-west, labelled
    /// <see cref="LegendLabel"/> (design note <c>s102-relief-design.md</c> §3).
    /// </summary>
    /// <param name="color">The swatch's band colour (<c>#RRGGBB</c>), e.g. a middle band in the active palette.</param>
    public static SymbolLegendEntry LegendEntry(string color)
    {
        ArgumentNullException.ThrowIfNull(color);
        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\">" +
            "<defs><linearGradient id=\"l\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">" +
            "<stop offset=\"0\" stop-color=\"#FFFFFF\" stop-opacity=\"0.45\"/>" +
            "<stop offset=\"0.5\" stop-color=\"#FFFFFF\" stop-opacity=\"0\"/>" +
            "<stop offset=\"1\" stop-color=\"#000000\" stop-opacity=\"0.45\"/>" +
            "</linearGradient></defs>" +
            $"<rect x=\"1\" y=\"1\" width=\"14\" height=\"14\" rx=\"2\" fill=\"{color}\"/>" +
            "<rect x=\"1\" y=\"1\" width=\"14\" height=\"14\" rx=\"2\" fill=\"url(#l)\"/>" +
            "</svg>";
        return new SymbolLegendEntry(svg, LegendLabel);
    }

    /// <summary>
    /// The display option for the shading strength
    /// (<see cref="EcdisDisplaySettings.LayerOptions"/>): <c>"subtle"</c>,
    /// <c>"medium"</c> or <c>"strong"</c>. Medium when unset or unknown.
    /// </summary>
    public const string StrengthKey = "s102.relief.strength";

    /// <summary>The strength set in <paramref name="display"/>; <see cref="ReliefStrength.Medium"/> when unset or unknown.</summary>
    /// <param name="display">The display settings, or <see langword="null"/>.</param>
    public static ReliefStrength StrengthOf(EcdisDisplaySettings? display) =>
        ParseStrength(display?.GetLayerOption(StrengthKey));

    /// <summary>The strength a <see cref="StrengthKey"/> option value names; <see cref="ReliefStrength.Medium"/> when null or unknown.</summary>
    /// <param name="value">The option value, or <see langword="null"/>.</param>
    public static ReliefStrength ParseStrength(string? value) =>
        value switch
        {
            "subtle" => ReliefStrength.Subtle,
            "strong" => ReliefStrength.Strong,
            _ => ReliefStrength.Medium,
        };

    /// <summary>The <see cref="StrengthKey"/> option value for <paramref name="strength"/>.</summary>
    /// <param name="strength">The strength.</param>
    public static string OptionValue(ReliefStrength strength) => strength switch
    {
        ReliefStrength.Subtle => "subtle",
        ReliefStrength.Strong => "strong",
        _ => "medium",
    };

    /// <summary>
    /// The shading for <paramref name="display"/> in <paramref name="palette"/>:
    /// <see cref="ReliefShading.Strength"/> 0.35, 0.6 or 0.85 by the strength
    /// option, and <see cref="ReliefShading.Lightening"/> all of it in Day, half
    /// at Dusk and 30 % at Night, so lit slopes don't glare on a dark display
    /// (design note <c>s102-relief-design.md</c> §2, §4).
    /// </summary>
    /// <param name="display">The display settings, or <see langword="null"/>.</param>
    /// <param name="palette">The colour palette the surface is drawn in.</param>
    public static ReliefShading ShadingFor(EcdisDisplaySettings? display, PaletteType palette) =>
        ReliefShading.Default with
        {
            Strength = StrengthOf(display) switch
            {
                ReliefStrength.Subtle => 0.35,
                ReliefStrength.Strong => 0.85,
                _ => 0.6,
            },
            Lightening = palette switch
            {
                PaletteType.Night => 0.3,
                PaletteType.Dusk => 0.5,
                _ => 1.0,
            },
        };

    /// <summary>
    /// Returns <paramref name="layer"/> with <see cref="StyledCoverageLayer.ReliefShade"/>
    /// computed from its depths, or the layer unchanged when it has no depth
    /// field or fewer than 2 × 2 cells.
    /// </summary>
    /// <param name="layer">The styled S-102 layer.</param>
    /// <param name="nativeToWgs84">
    /// Maps a native (x, y) to WGS-84 (longitude, latitude), or
    /// <see langword="null"/> when the native CRS is WGS-84.
    /// </param>
    /// <param name="shading">The shading parameters.</param>
    /// <returns>The shaded layer.</returns>
    public static StyledCoverageLayer Apply(
        StyledCoverageLayer layer,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84,
        ReliefShading shading)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(shading);

        var coverage = layer.Coverage;
        var meta = coverage.Metadata;
        if (meta.NumRows < 2 || meta.NumColumns < 2 || !coverage.HasField(S102DepthAdjuster.DepthField))
            return layer;

        var (width, height) = CellSizeMetres(layer.Georeferencer, nativeToWgs84);
        if (!(width > 0) || !(height > 0))
            return layer;

        var depths = coverage.GetValues(S102DepthAdjuster.DepthField);
        var shade = shading.Compute(depths, meta.NumRows, meta.NumColumns, layer.NoDataValue, width, height);

        return new StyledCoverageLayer
        {
            Coverage = coverage,
            ColorScheme = layer.ColorScheme,
            NoDataValue = layer.NoDataValue,
            Georeferencer = layer.Georeferencer,
            SymbolScheme = layer.SymbolScheme,
            ReliefShade = shade,
        };
    }

    // The ground size of a cell at the grid's centre, in metres: works for a
    // geographic grid (degrees) and a projected one (e.g. UTM) alike.
    private static (double Width, double Height) CellSizeMetres(
        GridGeoreferencer georeferencer,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84)
    {
        var meta = georeferencer.Metadata;
        int r = (meta.NumRows - 1) / 2;
        int c = (meta.NumColumns - 1) / 2;

        var origin = ToWgs84(georeferencer.ToNative(r, c));
        var east = ToWgs84(georeferencer.ToNative(r, c + 1));
        var north = ToWgs84(georeferencer.ToNative(r + 1, c));
        return (Distance(origin, east), Distance(origin, north));

        (double Lon, double Lat) ToWgs84((double X, double Y) native) =>
            nativeToWgs84 is null ? (native.X, native.Y) : nativeToWgs84(native.X, native.Y);
    }

    // Local flat-earth distance: exact enough over one cell.
    private static double Distance((double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        const double metresPerDegree = 111_195.0;
        double dLat = (b.Lat - a.Lat) * metresPerDegree;
        double dLon = (b.Lon - a.Lon) * metresPerDegree * Math.Cos((a.Lat + b.Lat) / 2 * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }
}

/// <summary>The three steps of S-102 relief shading strength (<see cref="S102Relief.StrengthKey"/>).</summary>
public enum ReliefStrength
{
    /// <summary>Strength 0.35: enough for fine, high-resolution cells.</summary>
    Subtle,

    /// <summary>Strength 0.6, the default.</summary>
    Medium,

    /// <summary>Strength 0.85: strongest; quay walls and steep edges draw dark.</summary>
    Strong,
}
