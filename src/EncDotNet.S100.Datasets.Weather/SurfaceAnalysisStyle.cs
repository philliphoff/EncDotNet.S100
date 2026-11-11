using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The code-defined drawing of a <see cref="SurfaceAnalysis"/>: its fronts and
/// troughs in WMO surface-analysis symbology and its highs and lows as
/// <c>H</c> and <c>L</c> with their pressure, all in one monochrome front ink
/// (design note <c>docs/design/vector-weather.md</c> §5, V17–V22). Built with
/// the portrayal toolkit (<c>EncDotNet.S100.Portrayals.Application</c>).
/// Not for navigation.
/// </summary>
/// <remarks>
/// <para><b>Lines (V17).</b> Each kind is a <see cref="ComplexLineStyle"/> in <see cref="LineStyles"/>:</para>
/// <list type="table">
/// <listheader><term>Kind</term><description>Line and pips</description></listheader>
/// <item><term>Cold front</term><description>Solid 0.7 mm; filled triangles on the left.</description></item>
/// <item><term>Warm front</term><description>Solid 0.7 mm; filled semicircles on the left.</description></item>
/// <item><term>Occluded front</term><description>Solid 0.7 mm; triangle and semicircle alternating, both on the left.</description></item>
/// <item><term>Stationary front</term><description>Solid 0.7 mm; triangle on the left, semicircle on the right, alternating.</description></item>
/// <item><term>Trough</term><description>Dashed 1.5 / 1 mm, 0.5 mm; no pips.</description></item>
/// </list>
/// <para>
/// "Left" is walking the line from its first point to its last, which is
/// where the coded bulletin puts the pips (<see cref="SurfaceFront"/>). The
/// bulletin doesn't say which way a stationary front's symbols face, so the
/// triangles take the left, as a cold front's do, and the semicircles the
/// right: a convention, not data.
/// </para>
/// <para>
/// <b>Placement (V20, V21).</b> A pip every 9 mm on screen, the first 4 mm in,
/// so the density holds at every scale (the toolkit's complex line style).
/// The bulletin's points are smoothed with three Chaikin passes
/// (<see cref="Smooth"/>) before drawing; the pick report keeps the coded
/// points. Each line has a halo in the isobar label halo colour
/// (<see cref="WeatherPalette.IsobarHaloToken"/>) so it stands clear of the
/// isobars under it.
/// </para>
/// <para>
/// <b>Pressure centres (V19).</b> A bold <c>H</c> or <c>L</c> above an
/// <c>×</c> at the exact centre, and the pressure in hectopascals underlined
/// below it, all haloed.
/// </para>
/// <para>
/// <b>Scale (V22).</b> Fronts and centres are drawn at every scale; troughs
/// only at 1:<see cref="TroughScaleMinimum"/> and larger; the pressure under a
/// centre at 1:<see cref="PressureValueScaleMinimum"/> and larger. There's no
/// maximum scale.
/// </para>
/// <para>
/// <b>Colour (V18; Q3).</b> One token, <see cref="WeatherPalette.FrontInkToken"/>
/// (<c>WXFRONT</c>), resolved against <see cref="WeatherPalette.Palette"/>. The
/// symbols are in <see cref="WeatherCoverageStyle.Symbols"/>.
/// </para>
/// <para>
/// <b>Layer order (V25, §9.2).</b> One sub-layer, <see cref="LayerKey"/>, on
/// the Other chart overlays plane at priority <see cref="WithinPlanePriority"/>
/// (92), above the isobars (90). Its legend (<see cref="BuildLegend"/>) has
/// the five line kinds and the pressure centre.
/// </para>
/// </remarks>
public static class SurfaceAnalysisStyle
{
    /// <summary>The style's name in the tile cache scope.</summary>
    public const string Name = "WeatherFronts";

    /// <summary>The style's version: bump it whenever its symbols, palette or rules change.</summary>
    public const int Version = 1;

    /// <summary>The sub-layer key, <c>"weather.fronts"</c>.</summary>
    public const string LayerKey = "weather.fronts";

    /// <summary>The sub-layer's order on the Other chart overlays plane: 92, above the isobars' 90.</summary>
    public const int WithinPlanePriority = 92;

    /// <summary>The viewing group of every instruction.</summary>
    public const int ViewingGroup = 31030;

    /// <summary>The pen width of a front, in millimetres.</summary>
    public const double FrontWidth = 0.7;

    /// <summary>The pen width of a trough, in millimetres.</summary>
    public const double TroughWidth = 0.5;

    /// <summary>The width of the halo under a line, in millimetres: the pen plus a margin either side.</summary>
    public const double HaloMargin = 0.35;

    /// <summary>The largest scale denominator troughs are drawn at: 40,000,000.</summary>
    public const double TroughScaleMinimum = 40_000_000;

    /// <summary>The largest scale denominator the pressure under a centre is drawn at: 60,000,000.</summary>
    public const double PressureValueScaleMinimum = 60_000_000;

    /// <summary>The number of Chaikin passes that smooth a coded line before drawing: 3.</summary>
    public const int SmoothingPasses = 3;

    /// <summary>A front's triangle pip, drawn above its x axis (the left of the line).</summary>
    public const string TriangleSymbol = "WXFRONTTRI";

    /// <summary>A front's semicircle pip, drawn above its x axis (the left of the line).</summary>
    public const string SemicircleSymbol = "WXFRONTSEMI";

    /// <summary>The <c>×</c> at the exact centre of a high or low.</summary>
    public const string CentreSymbol = "WXPCENTRE";

    /// <summary>
    /// The <c>×</c> at the centre with the rule under the centre's pressure,
    /// drawn instead of <see cref="CentreSymbol"/> where the pressure is.
    /// </summary>
    public const string CentreWithRuleSymbol = "WXPCENTRERULE";

    /// <summary>The legend glyph of a pressure centre: an <c>L</c>.</summary>
    public const string CentreLegendSymbol = "WXPCLEGEND";

    /// <summary>The tile disk-cache content scope of the style (<see cref="ApplicationContentScope"/>).</summary>
    public static string ContentScope { get; } = ApplicationContentScope.For(Name, Version);

    /// <summary>The cold front: triangles on the left.</summary>
    public static ComplexLineStyle Cold { get; } = Front("WXFRONT_COLD", [TriangleSymbol]);

    /// <summary>The warm front: semicircles on the left.</summary>
    public static ComplexLineStyle Warm { get; } = Front("WXFRONT_WARM", [SemicircleSymbol]);

    /// <summary>The occluded front: a triangle and a semicircle in turn, both on the left.</summary>
    public static ComplexLineStyle Occluded { get; } = Front("WXFRONT_OCCLUDED", [TriangleSymbol, SemicircleSymbol]);

    /// <summary>The stationary front: a triangle on the left, then a semicircle on the right, in turn.</summary>
    public static ComplexLineStyle Stationary { get; } = Front("WXFRONT_STATIONARY", [TriangleSymbol, SemicircleSymbol]) with { AlternateSides = true };

    /// <summary>The trough: a dashed pen, no pips.</summary>
    public static ComplexLineStyle Trough { get; } = new()
    {
        Name = "WXFRONT_TROUGH",
        ColorToken = WeatherPalette.FrontInkToken,
        Width = TroughWidth,
        DashLength = 1.5,
        GapLength = 1,
    };

    /// <summary>Every line style: <see cref="Cold"/>, <see cref="Warm"/>, <see cref="Occluded"/>, <see cref="Stationary"/> and <see cref="Trough"/>.</summary>
    public static LineStyleSet LineStyles { get; } = new([Cold, Warm, Occluded, Stationary, Trough]);

    private static readonly TextInstructionStyle CentreLetterStyle = new()
    {
        FontSize = 22,
        FontColor = WeatherPalette.FrontInkToken,
        FontWeight = TextFontWeight.Bold,
        HaloColor = WeatherPalette.IsobarHaloToken,
        HaloWidth = 0.6,
        VerticalAlignment = TextVerticalAlignment.Bottom,
        OffsetY = -1.0,
    };

    private static readonly TextInstructionStyle PressureStyle = new()
    {
        FontSize = 12,
        FontColor = WeatherPalette.FrontInkToken,
        FontWeight = TextFontWeight.Bold,
        HaloColor = WeatherPalette.IsobarHaloToken,
        HaloWidth = 0.6,
        VerticalAlignment = TextVerticalAlignment.Top,
        OffsetY = 1.2,
    };

    private static readonly TextInstructionStyle AgeStyle = PressureStyle with
    {
        FontSize = 10,
        OffsetY = 5.2,
    };

    /// <summary>The line style of a kind of front.</summary>
    /// <param name="type">The kind of front.</param>
    public static ComplexLineStyle LineStyleOf(SurfaceFrontType type) => type switch
    {
        SurfaceFrontType.Cold => Cold,
        SurfaceFrontType.Warm => Warm,
        SurfaceFrontType.Occluded => Occluded,
        SurfaceFrontType.Stationary => Stationary,
        SurfaceFrontType.Trough => Trough,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// The drawing instructions of one front or trough: its halo and its line,
    /// referencing <paramref name="featureReference"/>, whose geometry is the
    /// smoothed line (<see cref="Smooth"/>).
    /// </summary>
    /// <param name="featureReference">The front's feature id.</param>
    /// <param name="type">The kind of front.</param>
    public static IReadOnlyList<DrawingInstruction> BuildFront(string featureReference, SurfaceFrontType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        var style = LineStyleOf(type);
        var builder = new InstructionBuilder
        {
            ViewingGroup = ViewingGroup,
            DrawingPriority = type == SurfaceFrontType.Trough ? 1 : 3,
            ScaleMinimum = type == SurfaceFrontType.Trough ? TroughScaleMinimum : null,
        };

        var halo = (builder with { DrawingPriority = builder.DrawingPriority - 1 }).Line(
            featureReference,
            WeatherPalette.IsobarHaloToken,
            style.Width + 2 * HaloMargin,
            style.DashLength,
            style.GapLength);
        return [halo, builder.ComplexLine(featureReference, style.Name)];
    }

    /// <summary>
    /// The opacity a stale analysis is drawn at, lines and labels alike: half
    /// (design note §7, V30).
    /// </summary>
    public const double StaleOpacity = 0.5;

    /// <summary>"9 h old": a stale analysis's age, to the nearest hour, as its <c>H</c> and <c>L</c> show it.</summary>
    /// <param name="age">The age.</param>
    public static string FormatAge(TimeSpan age) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)Math.Round(age.TotalHours, MidpointRounding.AwayFromZero))} h old");

    /// <summary>
    /// The drawing instructions of one high or low: the letter, the <c>×</c>
    /// at the centre, and the pressure with its rule, referencing
    /// <paramref name="featureReference"/>, a point at the centre. A stale
    /// analysis's centres also say its age under the pressure (V30).
    /// </summary>
    /// <param name="featureReference">The centre's feature id.</param>
    /// <param name="centre">The high or low.</param>
    /// <param name="staleAge">The analysis's age once stale, or null while it is current.</param>
    public static IReadOnlyList<DrawingInstruction> BuildCentre(string featureReference, PressureCentre centre, TimeSpan? staleAge)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        ArgumentNullException.ThrowIfNull(centre);
        var builder = new InstructionBuilder { ViewingGroup = ViewingGroup, DrawingPriority = 5 };
        // Where the pressure is drawn, the cross carries its rule; at smaller
        // scales, the cross alone.
        var value = builder with { ScaleMinimum = PressureValueScaleMinimum };
        var crossOnly = builder with { ScaleMaximum = PressureValueScaleMinimum };
        List<DrawingInstruction> instructions =
        [
            crossOnly.Point(featureReference, CentreSymbol),
            value.Point(featureReference, CentreWithRuleSymbol),
            builder.Text(featureReference, LetterOf(centre.Kind), CentreLetterStyle),
            value.Text(featureReference, centre.PressureHectopascals.ToString(CultureInfo.InvariantCulture), PressureStyle),
        ];
        if (staleAge is { } age)
        {
            // Under the pressure where it is drawn; under the cross alone otherwise.
            instructions.Add(value.Text(featureReference, FormatAge(age), AgeStyle));
            instructions.Add(crossOnly.Text(featureReference, FormatAge(age), AgeStyle with { OffsetY = PressureStyle.OffsetY }));
        }

        return instructions;
    }

    /// <summary>The letter of a high or a low: <c>H</c> or <c>L</c>.</summary>
    /// <param name="kind">High or low.</param>
    public static string LetterOf(PressureCentreKind kind) => kind == PressureCentreKind.High ? "H" : "L";

    /// <summary>
    /// The legend under the sub-layer's Layer stack entry, in the given
    /// palette: the five line kinds and "Pressure centre, hPa".
    /// </summary>
    /// <param name="palette">The palette the glyphs are drawn in.</param>
    public static IReadOnlyList<LegendEntry> BuildLegend(PaletteType palette)
    {
        var legend = new LegendBuilder(WeatherPalette.Palette, palette, WeatherCoverageStyle.Symbols.Provider);
        return
        [
            legend.Line(Cold, "Cold front"),
            legend.Line(Warm, "Warm front"),
            legend.Line(Occluded, "Occluded front"),
            legend.Line(Stationary, "Stationary front"),
            legend.Line(Trough, "Trough"),
            legend.Symbol(CentreLegendSymbol, "Pressure centre, hPa"),
        ];
    }

    /// <summary>
    /// Smooths a coded line for drawing (V21): repeated points are dropped,
    /// then <paramref name="passes"/> Chaikin passes cut each corner at a
    /// quarter and three quarters of its edges, keeping the end points. A line
    /// that crosses 180° is smoothed the short way round, and its longitudes
    /// are returned in −180 to 180.
    /// </summary>
    /// <param name="points">The bulletin's points, in order.</param>
    /// <param name="passes">The number of passes, usually <see cref="SmoothingPasses"/>.</param>
    /// <returns>The smoothed line; the points as they are when there are fewer than three distinct ones.</returns>
    public static IReadOnlyList<GeoPosition> Smooth(IReadOnlyList<GeoPosition> points, int passes)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegative(passes);

        // Unwrap the longitudes so each lies within 180° of the one before it,
        // dropping repeats (WPC sometimes repeats a front's last point).
        var line = new List<(double Lat, double Lon)>(points.Count);
        foreach (var p in points)
        {
            double lon = p.Longitude;
            if (line.Count > 0)
            {
                double previous = line[^1].Lon;
                lon += 360 * Math.Round((previous - lon) / 360);
                if (line[^1].Lat == p.Latitude && previous == lon)
                    continue;
            }

            line.Add((p.Latitude, lon));
        }

        if (line.Count >= 3)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new List<(double, double)>(2 * line.Count) { line[0] };
                for (int i = 0; i < line.Count - 1; i++)
                {
                    var (aLat, aLon) = line[i];
                    var (bLat, bLon) = line[i + 1];
                    next.Add((0.75 * aLat + 0.25 * bLat, 0.75 * aLon + 0.25 * bLon));
                    next.Add((0.25 * aLat + 0.75 * bLat, 0.25 * aLon + 0.75 * bLon));
                }

                next.Add(line[^1]);
                line = next;
            }
        }

        return line.Select(p => new GeoPosition(p.Lat, Wrap(p.Lon))).ToArray();
    }

    private static double Wrap(double longitude)
    {
        if (longitude is >= -180 and <= 180)
            return longitude;
        double wrapped = ((longitude + 180) % 360 + 360) % 360 - 180;
        // Keep +180 as it is rather than turning it into -180.
        return wrapped == -180 && longitude > 0 ? 180 : wrapped;
    }

    private static ComplexLineStyle Front(string name, string[] symbols) => new()
    {
        Name = name,
        ColorToken = WeatherPalette.FrontInkToken,
        Width = FrontWidth,
        Symbols = symbols,
    };
}
