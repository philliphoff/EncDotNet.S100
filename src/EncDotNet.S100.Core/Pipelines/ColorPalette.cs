namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A named set of colours used for portrayal, keyed by S-100 colour token.
/// </summary>
public sealed class ColorPalette
{
    public static ColorPalette Default { get; } = new("Day", new Dictionary<string, string>());

    public string Name { get; }
    public IReadOnlyDictionary<string, string> Colors { get; }

    public ColorPalette(string name, IReadOnlyDictionary<string, string> colors)
    {
        Name = name;
        Colors = colors;
    }

    public string Resolve(string token) =>
        Colors.TryGetValue(token, out var hex) ? hex : "#000000";

    public bool TryResolve(string token, out string hex) =>
        Colors.TryGetValue(token, out hex!);

    public static ColorPalette FromType(PaletteType type) => type switch
    {
        PaletteType.Day => Default,
        PaletteType.Dusk => new("Dusk", new Dictionary<string, string>()),
        PaletteType.Night => new("Night", new Dictionary<string, string>()),
        _ => Default,
    };
}

public enum PaletteType
{
    Day,
    Dusk,
    Night
}
