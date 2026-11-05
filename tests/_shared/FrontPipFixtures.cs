using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Synthetic front-like line styles for the complex line style toolkit
/// (#1293): a pip triangle and semicircle drawn above their x axis (so on the
/// left of a line), a two-ink palette, and the cold, warm and stationary
/// styles built from them. Test fixtures only: the real fronts portrayal is
/// #1300.
/// </summary>
internal static class FrontPipFixtures
{
    public const string Ink = "TPIPINK";
    public const string WarmInk = "TPIPWARM";
    public const string Triangle = "TPIP_TRIANGLE";
    public const string Semicircle = "TPIP_SEMICIRCLE";
    public const string WarmSemicircle = "TPIP_SEMICIRCLE_WARM";

    public static ApplicationPalette Palette { get; } = new("Pip test", new Dictionary<string, PaletteColors>
    {
        [Ink] = PaletteColors.FromHex("#1A2030", "#E3E6EE", "#646A76"),
        [WarmInk] = PaletteColors.FromHex("#B0342A", "#E88A80", "#6E3A36"),
    });

    /// <summary>The background of a render in <paramref name="palette"/>.</summary>
    public static RgbaColor Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => new RgbaColor(0x2A, 0x2E, 0x38),
        PaletteType.Night => new RgbaColor(0x08, 0x0A, 0x0E),
        _ => new RgbaColor(0xFF, 0xFF, 0xFF),
    };

    // A filled triangle on the x axis, apex up (negative y): 3 mm base, 2.4 mm tall.
    public static string TriangleSvg { get; } = Svg(Triangle, $"<path class=\"f{Ink}\" d=\"M -1.5 0 L 0 -2.4 L 1.5 0 Z\"/>");

    // A filled semicircle on the x axis, bulging up: 1.5 mm radius.
    public static string SemicircleSvg { get; } = Svg(Semicircle, $"<path class=\"f{Ink}\" d=\"M -1.5 0 A 1.5 1.5 0 0 1 1.5 0 Z\"/>");

    public static string WarmSemicircleSvg { get; } = Svg(WarmSemicircle, $"<path class=\"f{WarmInk}\" d=\"M -1.5 0 A 1.5 1.5 0 0 1 1.5 0 Z\"/>");

    /// <summary>Resolves the fixture symbols by name, as a <see cref="SymbolSet.Provider"/> would.</summary>
    public static Func<string, string?> Symbols { get; } = name => name switch
    {
        Triangle => TriangleSvg,
        Semicircle => SemicircleSvg,
        WarmSemicircle => WarmSemicircleSvg,
        _ => null,
    };

    /// <summary>Triangles on the left, one every 9 mm from 4 mm in.</summary>
    public static ComplexLineStyle Cold { get; } = new()
    {
        Name = "TPIP_COLD",
        ColorToken = Ink,
        Width = 0.7,
        Symbols = [Triangle],
    };

    /// <summary>Semicircles on the right.</summary>
    public static ComplexLineStyle Warm { get; } = new()
    {
        Name = "TPIP_WARM",
        ColorToken = Ink,
        Width = 0.7,
        Symbols = [Semicircle],
        Side = LineSymbolSide.Right,
    };

    /// <summary>A triangle and a semicircle in another ink, alternating sides.</summary>
    public static ComplexLineStyle Stationary { get; } = new()
    {
        Name = "TPIP_STATIONARY",
        ColorToken = Ink,
        Width = 0.7,
        Symbols = [Triangle, WarmSemicircle],
        AlternateSides = true,
    };

    /// <summary>A dashed pen with triangles.</summary>
    public static ComplexLineStyle Dashed { get; } = new()
    {
        Name = "TPIP_DASHED",
        ColorToken = Ink,
        Width = 0.5,
        DashLength = 1.5,
        GapLength = 1,
        Symbols = [Triangle],
    };

    public static LineStyleSet LineStyles { get; } = new([Cold, Warm, Stationary, Dashed]);

    private static string Svg(string name, string body) =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"6mm\" height=\"6mm\" viewBox=\"-3 -3 6 6\">"
        + $"<title>{name}</title>"
        + "<rect class=\"symbolBox layout\" fill=\"none\" x=\"-3\" y=\"-3\" width=\"6\" height=\"6\"/>"
        + body
        + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/>"
        + "</svg>";
}
