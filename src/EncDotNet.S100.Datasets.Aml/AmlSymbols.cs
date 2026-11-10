using System.Globalization;
using System.Text;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// The SVG symbols of the AML portrayal, written in code rather than shipped
/// as files: for each family, the framed point symbol of every glyph its
/// classes use, the boundary tick, the route chevron and the fraction bar
/// (AML design handoff decisions 15, 17 and 18).
/// </summary>
/// <remarks>
/// <para>
/// A framed symbol is a rounded square, 7.2 mm a side with a notch cut off
/// its top-right corner, filled with the halo and outlined in the family's
/// ink over a wider halo stroke, with the glyph inside. Point symbols are
/// drawn at 0.6 of their size, so the frame is about 4.3 mm on screen. No
/// chart point symbol has a frame.
/// </para>
/// <para>
/// The tick and chevron are line-style symbols, drawn at their own size. The
/// tick stands 1.1 mm off the line on its left (−y), so a rotation of 180°
/// puts it on the right.
/// </para>
/// </remarks>
public sealed class AmlSymbols
{
    private const string FramePath =
        "M -2.7 -3.6 L 2.0 -3.6 L 3.6 -2.0 L 3.6 2.7 Q 3.6 3.6 2.7 3.6 L -2.7 3.6 Q -3.6 3.6 -3.6 2.7 L -3.6 -2.7 Q -3.6 -3.6 -2.7 -3.6 Z";

    private readonly Dictionary<string, string> _svg = new(StringComparer.Ordinal);

    /// <summary>Writes the symbols of <paramref name="families"/>, with the framed symbols of <paramref name="glyphs"/> in each.</summary>
    /// <param name="families">The families.</param>
    /// <param name="glyphs">The glyphs used in each family.</param>
    public AmlSymbols(IEnumerable<AmlFamily> families, IEnumerable<(AmlFamily Family, AmlGlyph Glyph)> glyphs)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(glyphs);
        foreach (var family in families)
        {
            Add(AmlSignature.TickSymbol(family), Tick(family));
            Add(AmlSignature.ChevronSymbol(family), Chevron(family));
            Add(AmlSignature.BarSymbol(family), Bar(family));
        }

        foreach (var (family, glyph) in glyphs)
        {
            glyph.Validate();
            if (AmlSignature.ReservedNames.Contains(glyph.Name))
                throw new ArgumentException($"AML glyph name '{glyph.Name}' is reserved for the signature's own symbols.", nameof(glyphs));
            var name = AmlSignature.FramedSymbol(family, glyph);
            var svg = Framed(name, family, glyph);
            if (_svg.TryGetValue(name, out var existing))
            {
                if (!string.Equals(existing, svg, StringComparison.Ordinal))
                    throw new ArgumentException($"Two AML glyphs are named '{glyph.Name}'.", nameof(glyphs));
                continue;
            }

            _svg.Add(name, svg);
        }

        Provider = GetSymbol;
    }

    /// <summary>The names of every symbol.</summary>
    public IReadOnlyCollection<string> Names => _svg.Keys;

    /// <summary>The symbols by name, for a renderer's symbol provider.</summary>
    public Func<string, string?> Provider { get; }

    /// <summary>The SVG of the symbol <paramref name="name"/>, or <see langword="null"/>.</summary>
    /// <param name="name">The symbol's name.</param>
    public string? GetSymbol(string name) => _svg.TryGetValue(name, out var svg) ? svg : null;

    private void Add(string name, string svg)
    {
        if (!_svg.TryAdd(name, svg))
            throw new ArgumentException($"Two AML families make the symbol '{name}'.");
    }

    private static string Framed(string name, AmlFamily family, AmlGlyph glyph)
    {
        var body = glyph.Body.Replace("{ink}", family.ColorToken, StringComparison.Ordinal)
            .Replace("{halo}", AmlPalette.HaloToken, StringComparison.Ordinal);
        if (!glyph.Framed)
            return Svg(name, $"{glyph.Description} AML, {family.Name.ToLowerInvariant()} family.", 4.8, body);
        return Svg(
            name,
            $"{glyph.Description} In the AML frame, {family.Name.ToLowerInvariant()} family.",
            4.8,
            $"""<path class="s{AmlPalette.HaloToken} f0" stroke-width="1.4" stroke-linejoin="round" d="{FramePath}"/><path id="frame" class="f{AmlPalette.HaloToken} s{family.ColorToken}" stroke-width="0.5" stroke-linejoin="round" d="{FramePath}"/>{body}""");
    }

    private static string Tick(AmlFamily family) => Svg(
        AmlSignature.TickSymbol(family),
        $"An AML area boundary tick, {family.Name.ToLowerInvariant()} family: on the left of the line.",
        1.5,
        $"""<path class="s{family.ColorToken} f0" stroke-width="{Mm(AmlSignature.InkWidth)}" d="M 0 0 L 0 -1.1"/>""");

    private static string Chevron(AmlFamily family) => Svg(
        AmlSignature.ChevronSymbol(family),
        $"An AML route chevron, {family.Name.ToLowerInvariant()} family: pointing along the line.",
        1.5,
        $"""<path class="s{AmlPalette.HaloToken} f0" stroke-width="1.2" stroke-linejoin="round" d="M -0.7 -0.9 L 0.5 0 L -0.7 0.9"/><path class="s{family.ColorToken} f0" stroke-width="{Mm(AmlSignature.RouteInkWidth)}" stroke-linejoin="round" d="M -0.7 -0.9 L 0.5 0 L -0.7 0.9"/>""");

    private static string Bar(AmlFamily family) => Svg(
        AmlSignature.BarSymbol(family),
        $"The bar of an AML airspace limits fraction, {family.Name.ToLowerInvariant()} family.",
        5.0,
        $"""<path class="s{AmlPalette.HaloToken} f0" stroke-width="1.0" d="M -4.2 0 L 4.2 0"/><path class="s{family.ColorToken} f0" stroke-width="0.4" d="M -4.2 0 L 4.2 0"/>""");

    private static string Svg(string name, string description, double half, string content)
    {
        var size = Mm(2 * half);
        var min = Mm(-half);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" version="1.2" width="{size}mm" height="{size}mm" viewBox="{min} {min} {size} {size}">""");
        sb.Append(CultureInfo.InvariantCulture, $"<title>{name}</title><desc>{description}</desc>");
        sb.Append(CultureInfo.InvariantCulture, $"""<rect class="symbolBox layout" fill="none" x="{min}" y="{min}" width="{size}" height="{size}"/>""");
        sb.Append(content);
        sb.Append("""<circle class="pivotPoint layout" fill="none" cx="0" cy="0" r="0.5"/></svg>""");
        return sb.ToString();
    }

    private static string Mm(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
