using System.Globalization;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// The station-model glyph of an NDBC observation, built with the
/// code-defined portrayal toolkit: a station marker, a WMO wind barb pointing
/// into the wind, and the significant wave height as a label.
/// </summary>
/// <remarks>
/// <para>
/// <b>Symbols.</b> The barbs are the weather styles' own
/// (<see cref="WeatherCoverageStyle.Symbols"/>, one per 5 kn, and the calm
/// circle), so an observed wind reads like a forecast one. A barb is rotated by
/// the direction the wind blows from, in the geographic frame, so it turns with
/// a rotated chart. The barbs are northern-hemisphere ones everywhere, as the
/// weather styles' are until southern barbs land (issue #1072).
/// </para>
/// <para>
/// <b>Ageing.</b> An observation older than
/// <see cref="NdbcObservationSourceOptions.StaleAfter"/> is drawn in the grey
/// <see cref="StaleInkToken"/> instead of the barb ink: the stale symbols are
/// the same SVGs with their <c>WXBARB</c> palette classes swapped, served under
/// the <see cref="StaleSymbolPrefix"/>.
/// </para>
/// <para>
/// <b>Layer.</b> Observations belong on S-98
/// <see cref="Interoperability.S98DisplayPlane.OtherChartOverlays"/> at
/// within-plane priority <see cref="WithinPlanePriority"/>, above the pressure
/// isobars (weather-sources.md §5). The source carries that on its
/// <see cref="DynamicSourceMetadata"/>.
/// </para>
/// </remarks>
public sealed class NdbcStationPortrayal : IDynamicFeaturePortrayal
{
    /// <summary>The within-plane priority of observations on <see cref="Interoperability.S98DisplayPlane.OtherChartOverlays"/>: 100.</summary>
    public const int WithinPlanePriority = 100;

    /// <summary>The station marker symbol.</summary>
    public const string StationSymbol = "NDBCSTN";

    /// <summary>The prefix of a stale symbol's name: the symbol after it, drawn in <see cref="StaleInkToken"/>.</summary>
    public const string StaleSymbolPrefix = "NDBCSTALE_";

    /// <summary>The colour token of the station marker's fill.</summary>
    public const string StationFillToken = "NDBCFILL";

    /// <summary>The colour token of a stale observation's ink.</summary>
    public const string StaleInkToken = "NDBCSTALE";

    /// <summary>The colour token of the label halo.</summary>
    public const string LabelHaloToken = "NDBCHALO";

    /// <summary>The largest scale denominator the wave height label is drawn at: 1:2,000,000.</summary>
    public const double LabelMaxScaleDenominator = 2_000_000;

    /// <summary>The label's font size.</summary>
    public const double LabelFontSize = 10;

    /// <summary>The shared portrayal.</summary>
    public static NdbcStationPortrayal Instance { get; } = new();

    /// <summary>
    /// The station marker symbols, embedded from the project's <c>Symbols</c>
    /// folder. The barbs come from <see cref="WeatherCoverageStyle.Symbols"/>.
    /// </summary>
    public static SymbolSet Symbols { get; } =
        new(typeof(NdbcStationPortrayal).Assembly, "EncDotNet.S100.DynamicSources.Ndbc.Symbols");

    /// <summary>
    /// The colours: the weather barb ink (<see cref="WeatherPalette.BarbInkToken"/>),
    /// <see cref="StationFillToken"/>, <see cref="StaleInkToken"/> and
    /// <see cref="LabelHaloToken"/>, each with a Day, Dusk and Night value.
    /// </summary>
    public static ApplicationPalette ObservationPalette { get; } = new(
        "NDBC",
        new Dictionary<string, PaletteColors>(StringComparer.Ordinal)
        {
            [WeatherPalette.BarbInkToken] = WeatherPalette.Palette.Tokens[WeatherPalette.BarbInkToken],
            [StationFillToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4250", night: "#0B0E14"),
            [StaleInkToken] = PaletteColors.FromHex(day: "#8A8F99", dusk: "#7D828C", night: "#3A3D44"),
            [LabelHaloToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4250", night: "#0B0E14"),
        });

    /// <summary>The marker instructions: viewing group 0, priority 0, every scale.</summary>
    public static InstructionBuilder MarkerInstructions { get; } = new();

    /// <summary>The barb instructions: drawn over the marker.</summary>
    public static InstructionBuilder BarbInstructions { get; } = MarkerInstructions with { DrawingPriority = 1 };

    /// <summary>The label instructions: drawn at 1:<see cref="LabelMaxScaleDenominator"/> and larger.</summary>
    public static InstructionBuilder LabelInstructions { get; } =
        BarbInstructions with { ScaleMinimum = LabelMaxScaleDenominator };

    private static readonly Func<string, string?> Provider = ResolveSymbol;

    /// <inheritdoc />
    public ApplicationPalette Palette => ObservationPalette;

    /// <inheritdoc />
    public Func<string, string?> SymbolProvider => Provider;

    /// <inheritdoc />
    public bool CanPortray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Kind is { } kind
            && kind.StartsWith(NdbcObservationSource.FeatureKind, StringComparison.Ordinal)
            && feature.Coordinates.Count > 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!CanPortray(feature))
            return [];

        var observation = NdbcObservationSource.GetObservation(feature);
        bool stale = NdbcObservationSource.IsStale(feature);
        string Symbol(string name) => stale ? StaleSymbolPrefix + name : name;

        var instructions = new List<DrawingInstruction>(3)
        {
            MarkerInstructions.Point(feature.Id, Symbol(StationSymbol)),
        };

        if (observation?.WindSpeed is { } speed && speed.TotalMetresPerSecond >= 0)
        {
            var barb = WindBarb.FromSpeed(speed);
            if (barb.IsCalm)
            {
                instructions.Add(BarbInstructions.Point(feature.Id, Symbol(barb.SymbolName)));
            }
            else if (observation.WindFromDirection is { } from)
            {
                instructions.Add(BarbInstructions.Point(
                    feature.Id,
                    Symbol(barb.SymbolName),
                    rotation: Normalise(from.TotalDegrees),
                    rotationCrs: SymbolRotationCrs.Geographic));
            }
        }

        if (observation?.WaveHeight is { } height)
        {
            instructions.Add(LabelInstructions.Text(
                feature.Id,
                string.Create(CultureInfo.InvariantCulture, $"{height.TotalMetres:0.0} m"),
                LabelStyle(stale)));
        }

        return instructions;
    }

    private static TextInstructionStyle LabelStyle(bool stale) => stale ? StaleLabel : FreshLabel;

    private static readonly TextInstructionStyle FreshLabel = new()
    {
        FontSize = LabelFontSize,
        FontColor = WeatherPalette.BarbInkToken,
        FontWeight = TextFontWeight.Bold,
        HaloColor = LabelHaloToken,
        HaloWidth = 0.3,
        HorizontalAlignment = TextHorizontalAlignment.Start,
        VerticalAlignment = TextVerticalAlignment.Top,
        OffsetX = 1.5,
    };

    private static readonly TextInstructionStyle StaleLabel = FreshLabel with
    {
        FontColor = StaleInkToken,
        FontWeight = TextFontWeight.Medium,
    };

    private static double Normalise(double degrees)
    {
        double d = degrees % 360;
        return d < 0 ? d + 360 : d;
    }

    private static string? ResolveSymbol(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.StartsWith(StaleSymbolPrefix, StringComparison.Ordinal))
        {
            return ResolveFresh(name[StaleSymbolPrefix.Length..]) is { } svg
                ? svg.Replace("sWXBARB", "s" + StaleInkToken, StringComparison.Ordinal)
                    .Replace("fWXBARB", "f" + StaleInkToken, StringComparison.Ordinal)
                : null;
        }

        return ResolveFresh(name);
    }

    private static string? ResolveFresh(string name) =>
        Symbols.GetSymbol(name) ?? WeatherCoverageStyle.Symbols.GetSymbol(name);
}
