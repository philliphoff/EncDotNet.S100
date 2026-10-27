using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>
/// The code-defined drawing of CAP warnings (design note
/// <c>docs/design/weather-sources.md</c> §6.1), built with the portrayal
/// toolkit (<c>EncDotNet.S100.Portrayals.Application</c>): each area is
/// hatched and outlined by severity and labelled with its event.
/// </summary>
/// <remarks>
/// <para>By severity:</para>
/// <list type="table">
/// <listheader><term>Severity</term><description>Outline and hatching</description></listheader>
/// <item><term>Extreme</term><description>Deep violet, 0.9 mm, cross-hatched every 2 mm.</description></item>
/// <item><term>Severe</term><description>Red, 0.7 mm, hatched every 2.5 mm.</description></item>
/// <item><term>Moderate</term><description>Orange, 0.5 mm, hatched every 4 mm.</description></item>
/// <item><term>Minor</term><description>Ochre, 0.4 mm, hatched every 6 mm.</description></item>
/// <item><term>Unknown</term><description>Grey, 0.4 mm dashed, hatched every 6 mm.</description></item>
/// </list>
/// <para>
/// The spacing differs as well as the colour, so the severities stay apart
/// without colour vision and in the Night palette. More severe areas are
/// drawn above less severe ones (a higher drawing priority). The label is the
/// CAP <c>event</c> (for example "Gale Warning"), an ordinary text
/// instruction, so it follows the text scale and the renderer's label
/// declutter; it isn't drawn at scales smaller than 1:3,000,000
/// (<see cref="LabelScaleMinimum"/>). Everything is in viewing group
/// <see cref="ViewingGroup"/>.
/// </para>
/// </remarks>
public static class CapStyle
{
    /// <summary>The style's name in the tile cache scope.</summary>
    public const string Name = "CAP";

    /// <summary>The style's version: bump it whenever its symbols, palette or rules change.</summary>
    public const int Version = 1;

    /// <summary>The viewing group of every CAP instruction.</summary>
    public const int ViewingGroup = 31020;

    /// <summary>The largest scale denominator labels are drawn at.</summary>
    public const double LabelScaleMinimum = 3_000_000;

    /// <summary>Colour token of the label text.</summary>
    public const string LabelToken = "CAPTXT";

    /// <summary>Colour token of the label halo.</summary>
    public const string HaloToken = "CAPHALO";

    /// <summary>The tile disk-cache content scope of the style (<see cref="ApplicationContentScope"/>).</summary>
    public static string ContentScope { get; } = ApplicationContentScope.For(Name, Version);

    /// <summary>The hatching symbols, <c>CAPHATCHEXT</c>, <c>CAPHATCHSEV</c>, <c>CAPHATCHMOD</c>, <c>CAPHATCHMIN</c> and <c>CAPHATCHUNK</c>.</summary>
    public static SymbolSet Symbols { get; } = new(typeof(CapStyle).Assembly, "EncDotNet.S100.Datasets.Cap.Symbols");

    /// <summary>
    /// The palette: one ink per severity (<c>CAPEXT</c>, <c>CAPSEV</c>,
    /// <c>CAPMOD</c>, <c>CAPMIN</c>, <c>CAPUNK</c>) plus the label text and
    /// halo. Night colours stay dim.
    /// </summary>
    public static ApplicationPalette Palette { get; } = new("CAP", new Dictionary<string, PaletteColors>
    {
        ["CAPEXT"] = PaletteColors.FromHex("#6A1B9A", "#B47FD6", "#5C3A72"),
        ["CAPSEV"] = PaletteColors.FromHex("#C62828", "#E57373", "#7A2E2E"),
        ["CAPMOD"] = PaletteColors.FromHex("#E06C00", "#F0A050", "#7A4A1C"),
        ["CAPMIN"] = PaletteColors.FromHex("#A68A00", "#D4BE4A", "#5E5320"),
        ["CAPUNK"] = PaletteColors.FromHex("#6E6E6E", "#A0A0A0", "#4A4A4A"),
        [LabelToken] = PaletteColors.FromHex("#1A1A1A", "#E6E6E6", "#8A8A8A"),
        [HaloToken] = PaletteColors.FromHex("#FFFFFF", "#202020", "#000000"),
    });

    private static readonly IReadOnlyDictionary<CapSeverity, (string Token, string Fill, double Spacing, double Width)> Looks =
        new Dictionary<CapSeverity, (string, string, double, double)>
        {
            [CapSeverity.Extreme] = ("CAPEXT", "CAPHATCHEXT", 2.0, 0.9),
            [CapSeverity.Severe] = ("CAPSEV", "CAPHATCHSEV", 2.5, 0.7),
            [CapSeverity.Moderate] = ("CAPMOD", "CAPHATCHMOD", 4.0, 0.5),
            [CapSeverity.Minor] = ("CAPMIN", "CAPHATCHMIN", 6.0, 0.4),
            [CapSeverity.Unknown] = ("CAPUNK", "CAPHATCHUNK", 6.0, 0.4),
        };

    private static readonly Dictionary<string, AreaFill> Fills = Looks.Values.ToDictionary(
        look => look.Fill,
        look => new AreaFill
        {
            Name = look.Fill,
            PatternSymbol = look.Fill,
            V1X = look.Spacing,
            V1Y = 0,
            V2X = 0,
            V2Y = look.Spacing,
        },
        StringComparer.Ordinal);

    private static readonly TextInstructionStyle LabelStyle = new()
    {
        FontSize = 10,
        FontColor = LabelToken,
        FontWeight = TextFontWeight.Bold,
        HaloColor = HaloToken,
        HaloWidth = 0.6,
    };

    /// <summary>Resolves a hatching area fill by name, or <see langword="null"/>.</summary>
    /// <param name="name">The area fill's name, e.g. <c>"CAPHATCHSEV"</c>.</param>
    public static AreaFill? ResolveAreaFill(string name) => Fills.GetValueOrDefault(name);

    /// <summary>The colour token of <paramref name="severity"/>'s outline and hatching.</summary>
    /// <param name="severity">The severity.</param>
    public static string ColorToken(CapSeverity severity) => Looks[severity].Token;

    /// <summary>The area fill of <paramref name="severity"/>'s hatching.</summary>
    /// <param name="severity">The severity.</param>
    public static string AreaFillName(CapSeverity severity) => Looks[severity].Fill;

    /// <summary>
    /// The drawing instructions of <paramref name="areas"/>: per area a hatch
    /// fill, an outline and an event label, referencing each area's
    /// <see cref="CapWarningArea.Id"/>.
    /// </summary>
    /// <param name="areas">The areas to draw, usually <see cref="CapWarningSet.DrawnAt"/>.</param>
    /// <returns>The instructions.</returns>
    public static IReadOnlyList<DrawingInstruction> BuildInstructions(IEnumerable<CapWarningArea> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        var instructions = new List<DrawingInstruction>();
        foreach (var area in areas)
        {
            if (area.Surfaces.Count == 0)
                continue;
            var severity = area.Info.Severity;
            var (token, fill, _, width) = Looks[severity];
            var builder = new InstructionBuilder
            {
                ViewingGroup = ViewingGroup,
                DrawingPriority = 10 + (int)severity,
            };

            instructions.Add(builder.AreaPattern(area.Id, fill));
            instructions.Add(severity == CapSeverity.Unknown
                ? builder.Line(area.Id, token, width, dashLength: 3.6, gapLength: 1.8)
                : builder.Line(area.Id, token, width));
            if (!string.IsNullOrWhiteSpace(area.Info.Event))
                instructions.Add((builder with { ScaleMinimum = LabelScaleMinimum }).Text(area.Id, area.Info.Event, LabelStyle));
        }

        return instructions;
    }
}
