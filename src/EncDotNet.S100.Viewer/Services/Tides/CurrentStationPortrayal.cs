using System.Globalization;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// The predicted current station glyph (tide predictions design D16, Q8a):
/// S-111's arrow in its speed band's colour rising from a dashed station
/// frame, with the speed beside it and, from 1:50,000, the name. At slack the
/// frame holds a bar and the label gives the turn time.
/// </summary>
/// <remarks>
/// The arrows and colours are the ones model currents use
/// (<see cref="CurrentArrowStyle"/>, <see cref="WeatherPalette"/>), which are
/// S-111's nine speed bands, with the tail moved to the station so the arrow
/// rises from it. The arrow's length doesn't scale with speed; the label
/// gives it. The frame means a predicted station, so it can't pass for an
/// S-111 grid node, whose arrows have no frame.
/// </remarks>
internal sealed class CurrentStationPortrayal : IDynamicFeaturePortrayal
{
    /// <summary>The dashed station frame.</summary>
    public const string FrameSymbol = "CURPRED_FRAME";

    /// <summary>The frame with a slack bar.</summary>
    public const string SlackSymbol = "CURPRED_SLACK";

    /// <summary>The prefix of a band's arrow, rising from the station: <c>CURPRED_ARROW1</c> to <c>9</c>.</summary>
    public const string ArrowPrefix = "CURPRED_ARROW";

    /// <summary>The shared portrayal.</summary>
    public static CurrentStationPortrayal Instance { get; } = new();

    private static readonly InstructionBuilder Arrows = new() { ScaleMinimum = TideStationPortrayal.StationMaxScaleDenominator };
    private static readonly InstructionBuilder Frames = Arrows with { DrawingPriority = 1 };
    private static readonly InstructionBuilder Labels = Arrows with { DrawingPriority = 2 };
    private static readonly InstructionBuilder Names = Arrows with { ScaleMinimum = TideStationPortrayal.NameMaxScaleDenominator, DrawingPriority = 2 };
    private static readonly Func<string, string?> Provider = ResolveSymbol;

    // Left of the station: tide stations label to the right, so a current
    // station beside a tide gauge (Tacoma Narrows Bridge) doesn't print over it.
    private static readonly TextInstructionStyle LabelStyle = new()
    {
        FontSize = 10,
        FontColor = WeatherPalette.CurrentOutlineToken,
        FontWeight = TextFontWeight.Bold,
        HaloColor = WeatherPalette.IsobarHaloToken,
        HaloWidth = 0.3,
        HorizontalAlignment = TextHorizontalAlignment.End,
        VerticalAlignment = TextVerticalAlignment.Center,
        OffsetX = -2.6,
    };

    private static readonly TextInstructionStyle NameStyle = LabelStyle with
    {
        FontWeight = TextFontWeight.Medium,
        VerticalAlignment = TextVerticalAlignment.Top,
        OffsetY = 3.2,
    };

    /// <inheritdoc />
    public ApplicationPalette Palette => WeatherPalette.Palette;

    /// <inheritdoc />
    public Func<string, string?> SymbolProvider => Provider;

    /// <inheritdoc />
    public bool CanPortray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Kind == CurrentStationSource.FeatureKind && feature.Coordinates.Count > 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!CanPortray(feature) || CurrentStationSource.GetState(feature) is not { } state)
        {
            return [];
        }

        var instructions = new List<DrawingInstruction>(4);
        if (state.IsSlack)
        {
            instructions.Add(Frames.Point(feature.Id, SlackSymbol));
        }
        else
        {
            instructions.Add(Arrows.Point(
                feature.Id,
                ArrowSymbol(BandOf(state.SpeedKnots)),
                rotation: Normalise(state.DirectionDegrees),
                rotationCrs: SymbolRotationCrs.Geographic));
            instructions.Add(Frames.Point(feature.Id, FrameSymbol));
        }

        instructions.Add(Labels.Text(feature.Id, state.Label, LabelStyle));
        if (!string.IsNullOrWhiteSpace(state.Name))
        {
            instructions.Add(Names.Text(feature.Id, state.Name, NameStyle));
        }

        return instructions;
    }

    /// <summary>The arrow of a speed band, 1 (slowest) to 9.</summary>
    /// <param name="band">The band.</param>
    public static string ArrowSymbol(int band) => ArrowPrefix + band.ToString(CultureInfo.InvariantCulture);

    /// <summary>The S-111 speed band of a speed, 1 (slowest) to 9.</summary>
    /// <param name="knots">The speed, in knots.</param>
    public static int BandOf(double knots)
    {
        var metresPerSecond = knots * EncDotNet.S100.Tides.TidalCurrent.MetersPerSecondPerKnot;
        var bands = CurrentArrowStyle.Bands;
        for (var i = 0; i < bands.Count; i++)
        {
            if (metresPerSecond < bands[i].MaxValue)
            {
                return i + 1;
            }
        }

        return bands.Count;
    }

    /// <summary>Resolves a symbol: the frame, the slack frame or a station arrow.</summary>
    /// <param name="name">The symbol name.</param>
    /// <returns>The SVG, or <c>null</c>.</returns>
    public static string? ResolveSymbol(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == FrameSymbol)
        {
            return Frame(bar: false);
        }

        if (name == SlackSymbol)
        {
            return Frame(bar: true);
        }

        if (name.StartsWith(ArrowPrefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(ArrowPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var band)
            && band is >= 1 and <= 9
            && WeatherCoverageStyle.Symbols.GetSymbol(CurrentArrowStyle.SymbolName(band)) is { } arrow)
        {
            // Move the arrow's tail to the pivot, so it rises from the station.
            return arrow
                .Replace("viewBox=\"-5.5 -5.5 11 11\"", "viewBox=\"-5.5 -10.5 11 11\"", StringComparison.Ordinal)
                .Replace("x=\"-5.5\" y=\"-5.5\"", "x=\"-5.5\" y=\"-10.5\"", StringComparison.Ordinal)
                .Replace(
                    "d=\"M 0 5 L -0.5 5 L -1 -1.5 L -2 -1.5 L 0 -5 L 2 -1.5 L 1 -1.5 L 0.5 5 Z\"",
                    "d=\"M 0 0 L -0.5 0 L -1 -6.5 L -2 -6.5 L 0 -10 L 2 -6.5 L 1 -6.5 L 0.5 0 Z\"",
                    StringComparison.Ordinal);
        }

        return null;
    }

    private static string Frame(bool bar) =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"4mm\" height=\"4mm\" viewBox=\"-2 -2 4 4\">"
        + "<rect class=\"symbolBox layout\" fill=\"none\" x=\"-2\" y=\"-2\" width=\"4\" height=\"4\"/>"
        + $"<rect class=\"f{WeatherPalette.IsobarHaloToken} s{WeatherPalette.CurrentOutlineToken}\" stroke-width=\"0.3\" stroke-dasharray=\"0.6 0.35\" x=\"-1.6\" y=\"-1.6\" width=\"3.2\" height=\"3.2\" rx=\"0.5\"/>"
        + (bar ? $"<path class=\"sl f0 s{WeatherPalette.CurrentOutlineToken}\" stroke-width=\"0.4\" d=\"M -0.9 0 L 0.9 0\"/>" : string.Empty)
        + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/></svg>";

    private static double Normalise(double degrees)
    {
        var d = degrees % 360;
        return d < 0 ? d + 360 : d;
    }
}
