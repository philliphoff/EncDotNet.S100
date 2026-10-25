using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// The Day, Dusk and Night values of one colour token in an
/// <see cref="ApplicationPalette"/>.
/// </summary>
/// <param name="Day">The colour in the Day palette.</param>
/// <param name="Dusk">The colour in the Dusk palette.</param>
/// <param name="Night">The colour in the Night palette.</param>
public readonly record struct PaletteColors(RgbaColor Day, RgbaColor Dusk, RgbaColor Night)
{
    /// <summary>The colour in <paramref name="palette"/>; Day for an unknown palette.</summary>
    /// <param name="palette">The palette.</param>
    public RgbaColor this[PaletteType palette] => palette switch
    {
        PaletteType.Dusk => Dusk,
        PaletteType.Night => Night,
        _ => Day,
    };

    /// <summary>Creates the colours from <c>#RRGGBB</c> hex strings.</summary>
    /// <param name="day">The Day colour.</param>
    /// <param name="dusk">The Dusk colour.</param>
    /// <param name="night">The Night colour.</param>
    public static PaletteColors FromHex(string day, string dusk, string night) =>
        new(RgbaColor.FromHex(day), RgbaColor.FromHex(dusk), RgbaColor.FromHex(night));
}

/// <summary>
/// The colours of a portrayal defined in code rather than by an S-100
/// portrayal catalogue: named colour tokens, each with a Day, Dusk and Night
/// value. It produces the <see cref="ColorPalette"/> that drawing
/// instructions' colour tokens and SVG palette classes resolve against.
/// </summary>
/// <remarks>
/// A catalogue-backed product reads its palettes from the catalogue's colour
/// profile (S-100 Part 9 §9-13). A code-defined portrayal, such as the
/// SoundCharts S-104 water level, has no colour profile, so it declares its
/// tokens here. Pick token names that don't clash with S-100 Part 9 tokens
/// (prefix them with the product, e.g. <c>S104INK</c>), so a symbol never
/// resolves against the wrong colour.
/// </remarks>
public sealed class ApplicationPalette
{
    private readonly Dictionary<string, PaletteColors> _tokens;
    private readonly ColorPalette _day;
    private readonly ColorPalette _dusk;
    private readonly ColorPalette _night;

    /// <summary>Creates a palette.</summary>
    /// <param name="name">
    /// The palette's name; each <see cref="ColorPalette"/> it produces is named
    /// <c>"{name} {palette}"</c>, e.g. <c>"S-104 Day"</c>.
    /// </param>
    /// <param name="tokens">The colours keyed by colour token. Tokens are matched ordinally.</param>
    /// <exception cref="ArgumentException">A token is null, empty or whitespace.</exception>
    public ApplicationPalette(string name, IReadOnlyDictionary<string, PaletteColors> tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tokens);

        _tokens = new Dictionary<string, PaletteColors>(tokens.Count, StringComparer.Ordinal);
        foreach (var (token, colors) in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("A colour token must not be empty.", nameof(tokens));
            _tokens.Add(token, colors);
        }

        Name = name;
        _day = Build(PaletteType.Day);
        _dusk = Build(PaletteType.Dusk);
        _night = Build(PaletteType.Night);
    }

    /// <summary>The palette's name.</summary>
    public string Name { get; }

    /// <summary>The colours keyed by colour token.</summary>
    public IReadOnlyDictionary<string, PaletteColors> Tokens => _tokens;

    /// <summary>The colour of <paramref name="token"/> in <paramref name="palette"/>.</summary>
    /// <param name="token">The colour token.</param>
    /// <param name="palette">The palette; Day for an unknown palette.</param>
    /// <exception cref="KeyNotFoundException">The palette doesn't define <paramref name="token"/>.</exception>
    public RgbaColor Resolve(string token, PaletteType palette)
    {
        ArgumentNullException.ThrowIfNull(token);
        return _tokens.TryGetValue(token, out var colors)
            ? colors[palette]
            : throw new KeyNotFoundException($"The {Name} palette has no colour token '{token}'.");
    }

    /// <summary>
    /// The <see cref="ColorPalette"/> for <paramref name="palette"/>: every
    /// token's colour as a <c>#RRGGBB</c> hex string. The Day, Dusk and Night
    /// palettes are built once and shared.
    /// </summary>
    /// <param name="palette">The palette; an unknown value gets the Day colours.</param>
    public ColorPalette GetColorPalette(PaletteType palette) => palette switch
    {
        PaletteType.Day => _day,
        PaletteType.Dusk => _dusk,
        PaletteType.Night => _night,
        _ => Build(palette),
    };

    private ColorPalette Build(PaletteType palette)
    {
        var colors = new Dictionary<string, string>(_tokens.Count, StringComparer.Ordinal);
        foreach (var (token, values) in _tokens)
            colors[token] = values[palette].ToHex();
        return new ColorPalette($"{Name} {palette}", colors);
    }
}
