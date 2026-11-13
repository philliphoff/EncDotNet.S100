using System.Globalization;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// The predicted tide station glyph (tide predictions design D5, D18): the
/// S-104 gauge with its frame dashed, rising, falling or a bar while turning;
/// the height beside it from 1:150,000 and the station name from 1:50,000.
/// </summary>
/// <remarks>
/// The symbols are S-104's own gauges (<see cref="S104CoverageStyle.Symbols"/>)
/// with a dash added to the frame, so a predicted station reads as a gauge at
/// a glance and the dash says predicted, as it does on a checked route. The
/// colours are S-104's (<see cref="S104CoverageStyle.Palette"/>).
/// </remarks>
internal sealed class TideStationPortrayal : IDynamicFeaturePortrayal
{
    /// <summary>The smallest scale stations are drawn at: 1:500,000 (D4).</summary>
    public const double StationMaxScaleDenominator = 500_000;

    /// <summary>The smallest scale heights are drawn at: 1:150,000 (D4).</summary>
    public const double HeightMaxScaleDenominator = 150_000;

    /// <summary>The smallest scale station names are drawn at: 1:50,000 (D4).</summary>
    public const double NameMaxScaleDenominator = 50_000;

    /// <summary>The prefix that turns an S-104 gauge symbol into its dashed, predicted form.</summary>
    public const string PredictedPrefix = "TIDEPRED_";

    /// <summary>The rising symbol.</summary>
    public const string RisingSymbol = PredictedPrefix + S104CoverageStyle.RisingSymbol;

    /// <summary>The falling symbol.</summary>
    public const string FallingSymbol = PredictedPrefix + S104CoverageStyle.FallingSymbol;

    /// <summary>The turning symbol: within 20 minutes of a high or low.</summary>
    public const string TurningSymbol = PredictedPrefix + S104CoverageStyle.SteadySymbol;

    /// <summary>The shared portrayal.</summary>
    public static TideStationPortrayal Instance { get; } = new();

    private static readonly InstructionBuilder Markers = new() { ScaleMinimum = StationMaxScaleDenominator };
    private static readonly InstructionBuilder Heights = Markers with { ScaleMinimum = HeightMaxScaleDenominator, DrawingPriority = 1 };
    private static readonly InstructionBuilder Names = Markers with { ScaleMinimum = NameMaxScaleDenominator, DrawingPriority = 1 };
    private static readonly Func<string, string?> Provider = ResolveSymbol;

    private static readonly TextInstructionStyle NameStyle = S104CoverageStyle.StationLabelStyle with
    {
        FontWeight = TextFontWeight.Medium,
        FontProportion = TextFontProportion.Proportional,
        VerticalAlignment = TextVerticalAlignment.Top,
        OffsetY = 3.2,
    };

    /// <inheritdoc />
    public ApplicationPalette Palette => S104CoverageStyle.Palette;

    /// <inheritdoc />
    public Func<string, string?> SymbolProvider => Provider;

    /// <inheritdoc />
    public bool CanPortray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Kind == TideStationSource.FeatureKind && feature.Coordinates.Count > 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!CanPortray(feature) || TideStationSource.GetState(feature) is not { } state)
        {
            return [];
        }

        var instructions = new List<DrawingInstruction>(3)
        {
            Markers.Point(feature.Id, SymbolFor(state.Trend)),
            Heights.Text(
                feature.Id,
                string.Create(CultureInfo.InvariantCulture, $"{state.HeightMeters:0.0} m"),
                S104CoverageStyle.StationLabelStyle),
        };
        if (!string.IsNullOrWhiteSpace(state.Name))
        {
            instructions.Add(Names.Text(feature.Id, state.Name, NameStyle));
        }

        return instructions;
    }

    /// <summary>The symbol for a trend.</summary>
    /// <param name="trend">The trend.</param>
    public static string SymbolFor(TideTrend trend) => trend switch
    {
        TideTrend.Rising => RisingSymbol,
        TideTrend.Falling => FallingSymbol,
        _ => TurningSymbol,
    };

    /// <summary>Resolves a symbol: a predicted gauge, else S-104's own.</summary>
    /// <param name="name">The symbol name.</param>
    /// <returns>The SVG, or <c>null</c>.</returns>
    public static string? ResolveSymbol(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!name.StartsWith(PredictedPrefix, StringComparison.Ordinal))
        {
            return S104CoverageStyle.Symbols.GetSymbol(name);
        }

        return S104CoverageStyle.Symbols.GetSymbol(name[PredictedPrefix.Length..]) is { } svg
            ? Dash(svg)
            : null;
    }

    // The gauge's frame is the one rect with a 1.6 stroke; dash it (D5, D7).
    private static string Dash(string svg) => svg.Replace(
        "stroke-width=\"1.6\" x=\"-8\"",
        "stroke-width=\"1.6\" stroke-dasharray=\"2.6 1.5\" x=\"-8\"",
        StringComparison.Ordinal);
}
