using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.DynamicSources.Nmea;

/// <summary>
/// The own-ship weather glyph, built with the code-defined portrayal toolkit:
/// a small station ring next to the own-ship symbol, a WMO wind barb growing
/// from it into the wind, and short wind and water temperature labels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placement.</b> The feature is at own ship, which the own-ship symbol
/// already marks, so the glyph is drawn <see cref="GlyphOffsetX"/> right of and
/// <see cref="GlyphOffsetY"/> above it (millimetres on the display, fixed on
/// screen as the chart rotates) and stays clear of the hull and heading line.
/// </para>
/// <para>
/// <b>Symbols.</b> The barbs are the weather styles' own
/// (<see cref="WeatherCoverageStyle.Symbols"/> through
/// <see cref="WindBarb.FromSpeed"/>, and the calm circle), as
/// <c>NdbcStationPortrayal</c> uses them, so own-ship wind reads like a
/// forecast's or a buoy's. A barb is rotated by the direction the wind comes
/// from, in the geographic frame. Barbs are northern-hemisphere ones
/// everywhere until the hemisphere selection of issue #1072 lands; this
/// portrayal should adopt it then.
/// </para>
/// <para>
/// <b>Relative wind.</b> When only the relative wind is known
/// (<see cref="OwnShipWeather.IsRelativeOnly"/>) and the heading is, the barb
/// shows the direction the relative wind comes from, drawn in the grey
/// <see cref="RelativeInkToken"/> (the same SVGs with their palette classes
/// swapped, under <see cref="RelativeSymbolPrefix"/>), and the label reads
/// <c>Rel. wind</c>. Without a heading there is no barb, only the label.
/// </para>
/// <para>
/// <b>Layer.</b> S-98 <see cref="Interoperability.S98DisplayPlane.OtherChartOverlays"/>
/// at within-plane priority <see cref="WithinPlanePriority"/>, just above buoy
/// observations.
/// </para>
/// </remarks>
public sealed class OwnShipWeatherPortrayal : IDynamicFeaturePortrayal
{
    /// <summary>The within-plane priority on <see cref="Interoperability.S98DisplayPlane.OtherChartOverlays"/>: 110, above NDBC observations (100).</summary>
    public const int WithinPlanePriority = 110;

    /// <summary>The station ring symbol the barb grows from.</summary>
    public const string StationSymbol = "OWNWXSTN";

    /// <summary>The prefix of a relative-wind symbol's name: the symbol after it, drawn in <see cref="RelativeInkToken"/>.</summary>
    public const string RelativeSymbolPrefix = "OWNWXREL_";

    /// <summary>The colour token of the station ring's fill.</summary>
    public const string StationFillToken = "OWNWXFILL";

    /// <summary>The colour token of relative wind's ink.</summary>
    public const string RelativeInkToken = "OWNWXREL";

    /// <summary>The colour token of the label halo.</summary>
    public const string LabelHaloToken = "OWNWXHALO";

    /// <summary>How far right of own ship the glyph is drawn, in millimetres: 8.</summary>
    public const double GlyphOffsetX = 8;

    /// <summary>How far below own ship the glyph is drawn, in millimetres (negative is up): -8.</summary>
    public const double GlyphOffsetY = -8;

    /// <summary>The labels' font size.</summary>
    public const double LabelFontSize = 10;

    /// <summary>The shared portrayal.</summary>
    public static OwnShipWeatherPortrayal Instance { get; } = new();

    /// <summary>The station ring, embedded from the project's <c>Symbols</c> folder. The barbs come from <see cref="WeatherCoverageStyle.Symbols"/>.</summary>
    public static SymbolSet Symbols { get; } =
        new(typeof(OwnShipWeatherPortrayal).Assembly, "EncDotNet.S100.DynamicSources.Nmea.Symbols");

    /// <summary>
    /// The colours: the weather barb ink (<see cref="WeatherPalette.BarbInkToken"/>),
    /// <see cref="StationFillToken"/>, <see cref="RelativeInkToken"/> and
    /// <see cref="LabelHaloToken"/>, each with a Day, Dusk and Night value.
    /// </summary>
    public static ApplicationPalette WeatherInkPalette { get; } = new(
        "OWNWX",
        new Dictionary<string, PaletteColors>(StringComparer.Ordinal)
        {
            [WeatherPalette.BarbInkToken] = WeatherPalette.Palette.Tokens[WeatherPalette.BarbInkToken],
            [StationFillToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4250", night: "#0B0E14"),
            [RelativeInkToken] = PaletteColors.FromHex(day: "#7A7F88", dusk: "#8A8F99", night: "#4A4E56"),
            [LabelHaloToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4250", night: "#0B0E14"),
        });

    /// <summary>The ring's instructions: viewing group 0, priority 0, every scale.</summary>
    public static InstructionBuilder StationInstructions { get; } = new();

    /// <summary>The barb's and labels' instructions: drawn over the ring.</summary>
    public static InstructionBuilder BarbInstructions { get; } = StationInstructions with { DrawingPriority = 1 };

    private static readonly Func<string, string?> Provider = ResolveSymbol;

    /// <inheritdoc />
    public ApplicationPalette Palette => WeatherInkPalette;

    /// <inheritdoc />
    public Func<string, string?> SymbolProvider => Provider;

    /// <inheritdoc />
    public bool CanPortray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Kind == NmeaWeatherSource.FeatureKind && feature.Coordinates.Count > 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!CanPortray(feature) || NmeaWeatherSource.GetWeather(feature) is not { } weather)
            return [];

        bool relative = weather.IsRelativeOnly;
        string Symbol(string name) => relative ? RelativeSymbolPrefix + name : name;

        var instructions = new List<DrawingInstruction>(4)
        {
            Offset(StationInstructions.Point(feature.Id, Symbol(StationSymbol))),
        };

        var (speed, from) = weather.TrueWind is { } tw
            ? (tw.Speed, (Quantities.Angle?)tw.FromDirection)
            : (weather.RelativeWindSpeed ?? default, weather.RelativeWindFromDirection);
        if (weather.TrueWind is not null || weather.RelativeWindSpeed is not null)
        {
            var barb = WindBarb.FromSpeed(speed);
            if (barb.IsCalm)
                instructions.Add(Offset(BarbInstructions.Point(feature.Id, Symbol(barb.SymbolName))));
            else if (from is { } direction)
                instructions.Add(Offset(BarbInstructions.Point(
                    feature.Id,
                    Symbol(barb.SymbolName),
                    rotation: direction.TotalDegrees,
                    rotationCrs: SymbolRotationCrs.Geographic)));
        }

        var ink = relative ? RelativeInkToken : WeatherPalette.BarbInkToken;
        if (NmeaWeatherFormatter.WindLabel(weather) is { } windLabel)
            instructions.Add(BarbInstructions.Text(feature.Id, windLabel, Label(ink, row: 0)));
        if (NmeaWeatherFormatter.WaterLabel(weather) is { } waterLabel)
            instructions.Add(BarbInstructions.Text(feature.Id, waterLabel, Label(WeatherPalette.BarbInkToken, row: 1)));

        return instructions;
    }

    // The ring and barb sit at the glyph offset; InstructionBuilder has no
    // offset, so the point is copied with one.
    private static PointInstruction Offset(PointInstruction p) => new()
    {
        FeatureReference = p.FeatureReference,
        SymbolReference = p.SymbolReference,
        Rotation = p.Rotation,
        RotationCrs = p.RotationCrs,
        Plane = p.Plane,
        ViewingGroup = p.ViewingGroup,
        DrawingPriority = p.DrawingPriority,
        ScaleMinimum = p.ScaleMinimum,
        ScaleMaximum = p.ScaleMaximum,
        LocalOffsetX = GlyphOffsetX,
        LocalOffsetY = GlyphOffsetY,
    };

    // Labels to the right of the ring, the wind on the ring's line and the
    // water temperature under it.
    private static TextInstructionStyle Label(string ink, int row) => new()
    {
        FontSize = LabelFontSize,
        FontColor = ink,
        FontWeight = TextFontWeight.Bold,
        HaloColor = LabelHaloToken,
        HaloWidth = 0.3,
        HorizontalAlignment = TextHorizontalAlignment.Start,
        VerticalAlignment = TextVerticalAlignment.Center,
        OffsetX = GlyphOffsetX + 2,
        OffsetY = GlyphOffsetY + row * 4,
    };

    private static string? ResolveSymbol(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.StartsWith(RelativeSymbolPrefix, StringComparison.Ordinal))
        {
            return ResolveSymbol(name[RelativeSymbolPrefix.Length..]) is { } svg
                ? svg.Replace("sWXBARB", "s" + RelativeInkToken, StringComparison.Ordinal)
                    .Replace("fWXBARB", "f" + RelativeInkToken, StringComparison.Ordinal)
                : null;
        }

        return Symbols.GetSymbol(name) ?? WeatherCoverageStyle.Symbols.GetSymbol(name);
    }
}
