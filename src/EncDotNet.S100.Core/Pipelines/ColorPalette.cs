namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A named set of colours used for portrayal, keyed by S-100 colour token.
/// </summary>
/// <remarks>
/// Portrayal catalogues load one palette per <see cref="PaletteType"/> from
/// their colour profile and expose the active one through
/// <see cref="IPortrayalCatalogue.ActivePalette"/>.
/// </remarks>
public sealed class ColorPalette
{
    /// <summary>
    /// An empty palette named <c>"Day"</c>. Every token resolves to the
    /// <see cref="Resolve"/> fallback; catalogues use it as the active
    /// palette until a real one has been loaded.
    /// </summary>
    public static ColorPalette Default { get; } = new("Day", new Dictionary<string, string>());

    /// <summary>The palette name (e.g. <c>"Day"</c>, <c>"Dusk"</c>, <c>"Night"</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// Colours keyed by S-100 colour token (e.g. <c>"DEPDW"</c>), with values
    /// as <c>#RRGGBB</c> hex strings. Token matching follows the supplied
    /// dictionary's comparer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Colors { get; }

    /// <summary>Creates a palette.</summary>
    /// <param name="name">The palette name.</param>
    /// <param name="colors">Colours keyed by colour token; the dictionary is used as-is, not copied.</param>
    public ColorPalette(string name, IReadOnlyDictionary<string, string> colors)
    {
        Name = name;
        Colors = colors;
    }

    /// <summary>Resolves a colour token to its hex colour.</summary>
    /// <param name="token">The S-100 colour token.</param>
    /// <returns>The token's hex colour, or <c>"#000000"</c> when the palette does not define it.</returns>
    public string Resolve(string token) =>
        Colors.TryGetValue(token, out var hex) ? hex : "#000000";

    /// <summary>Attempts to resolve a colour token to its hex colour.</summary>
    /// <param name="token">The S-100 colour token.</param>
    /// <param name="hex">When this method returns <see langword="true"/>, the token's hex colour.</param>
    /// <returns><see langword="true"/> when the palette defines <paramref name="token"/>.</returns>
    public bool TryResolve(string token, out string hex) =>
        Colors.TryGetValue(token, out hex!);

    /// <summary>
    /// Returns an empty placeholder palette named after <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The palette type.</param>
    /// <returns>
    /// <see cref="Default"/> for <see cref="PaletteType.Day"/> (and unknown
    /// values); otherwise a new empty palette named <c>"Dusk"</c> or
    /// <c>"Night"</c>. The result carries no colours; real palettes come from
    /// a portrayal catalogue's colour profile.
    /// </returns>
    public static ColorPalette FromType(PaletteType type) => type switch
    {
        PaletteType.Day => Default,
        PaletteType.Dusk => new("Dusk", new Dictionary<string, string>()),
        PaletteType.Night => new("Night", new Dictionary<string, string>()),
        _ => Default,
    };
}

/// <summary>
/// The S-100 Part 9 colour-profile palettes, matched to ambient bridge
/// lighting conditions.
/// </summary>
public enum PaletteType
{
    /// <summary>Daylight palette (bright ambient light).</summary>
    Day,

    /// <summary>Dusk palette (twilight / reduced ambient light).</summary>
    Dusk,

    /// <summary>Night palette (dark bridge; dimmed colours to preserve night vision).</summary>
    Night
}
