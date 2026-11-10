namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// The inside of an AML point symbol: a shape drawn in a family's ink inside
/// the AML frame, a rounded square with a notch at the top right (AML design
/// handoff decisions 15 and 17). <see cref="AmlSymbols"/> draws one framed
/// symbol for each glyph and family that a class style uses.
/// </summary>
/// <remarks>
/// <para>
/// Glyphs are SoundCharts' own, or follow MIL-STD-2525C shapes. None comes
/// from the restricted official AML portrayal, and APP-6 isn't used.
/// </para>
/// <para>
/// <see cref="Body"/> is SVG markup in millimetres about the symbol's centre,
/// within ±2.4 mm, with two placeholders: <c>{ink}</c> for the family's colour
/// token and <c>{halo}</c> for <see cref="AmlPalette.HaloToken"/>. Use them in
/// S-100 SVG colour classes: <c>class="s{ink} f0"</c> strokes in the ink with
/// no fill, <c>class="f{ink}"</c> fills.
/// </para>
/// </remarks>
/// <param name="Name">The glyph's name, upper-case letters, such as <c>WPT</c>: the end of its symbols' names.</param>
/// <param name="Description">What the glyph shows, for the SVG's description.</param>
/// <param name="Body">The glyph's SVG markup.</param>
public sealed record AmlGlyph(string Name, string Description, string Body)
{
    /// <summary>The air waypoint: an open triangle.</summary>
    public static AmlGlyph Waypoint { get; } = new(
        "WPT",
        "An open triangle: an air waypoint.",
        """<path class="s{ink} f0" stroke-width="0.5" stroke-linejoin="round" d="M 0 -2.0 L 2.0 1.5 L -2.0 1.5 Z"/>""");

    /// <summary>The NAVAID: a hexagon with a centre dot.</summary>
    public static AmlGlyph Navaid { get; } = new(
        "NAVAID",
        "A hexagon with a centre dot: a radio navigation aid.",
        """<path class="s{ink} f0" stroke-width="0.5" stroke-linejoin="round" d="M -1.1 -1.9 L 1.1 -1.9 L 2.2 0 L 1.1 1.9 L -1.1 1.9 L -2.2 0 Z"/><circle class="f{ink}" cx="0" cy="0" r="0.55"/>""");

    /// <summary>The turning point: a ringed dot.</summary>
    public static AmlGlyph TurningPoint { get; } = new(
        "TURN",
        "A ringed dot: a turning point.",
        """<circle class="s{ink} f0" stroke-width="0.5" cx="0" cy="0" r="1.8"/><circle class="f{ink}" cx="0" cy="0" r="0.75"/>""");

    /// <summary>
    /// Validates the glyph's name: upper-case ASCII letters and digits, so
    /// <c>AML_{FAMILY}_{GLYPH}</c> names never collide.
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(Name) || Name.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c)))
            throw new ArgumentException($"AML glyph name '{Name}' must be upper-case ASCII letters and digits.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Body);
    }
}
