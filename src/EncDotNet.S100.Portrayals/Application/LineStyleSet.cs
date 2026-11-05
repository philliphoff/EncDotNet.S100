using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// The line styles of a portrayal defined in code, served by name through
/// <see cref="Provider"/>: what a catalogue's <c>LineStyles</c> folder is to a
/// catalogue-backed product.
/// </summary>
/// <remarks>
/// Pass <see cref="Provider"/> as the portrayal result's line style provider
/// (<c>VectorPortrayalResult.LineStyleProvider</c>), and draw a style with
/// <see cref="InstructionBuilder.ComplexLine"/>. The renderers resolve the
/// style's colour token against the portrayal's palette and its symbols
/// through the symbol provider, as they do a catalogue's line styles.
/// </remarks>
public sealed class LineStyleSet
{
    private readonly Dictionary<string, (ComplexLineStyle Definition, LineStyle Style)> _styles = new(StringComparer.Ordinal);

    /// <summary>Creates a line style set.</summary>
    /// <param name="styles">The styles. Each is checked and converted now, so a bad style fails here.</param>
    /// <exception cref="ArgumentException">A style is not valid, or two share a name.</exception>
    public LineStyleSet(IEnumerable<ComplexLineStyle> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        foreach (var definition in styles)
        {
            ArgumentNullException.ThrowIfNull(definition, nameof(styles));
            var style = definition.ToLineStyle();
            if (!_styles.TryAdd(definition.Name, (definition, style)))
                throw new ArgumentException($"Two line styles are named '{definition.Name}'.", nameof(styles));
        }

        Provider = GetLineStyle;
    }

    /// <summary>The style names.</summary>
    public IReadOnlyCollection<string> Names => _styles.Keys;

    /// <summary>
    /// Resolves a style name to its line style, or <see langword="null"/> for
    /// a name the set doesn't hold. It is the same delegate on every call.
    /// </summary>
    public Func<string, LineStyle?> Provider { get; }

    /// <summary>Whether the set holds the style <paramref name="name"/>.</summary>
    /// <param name="name">The style name.</param>
    public bool Contains(string name) => _styles.ContainsKey(name);

    /// <summary>The line style <paramref name="name"/>, or <see langword="null"/> for a name the set doesn't hold.</summary>
    /// <param name="name">The style name, matched ordinally.</param>
    public LineStyle? GetLineStyle(string name) =>
        _styles.TryGetValue(name, out var entry) ? entry.Style : null;

    /// <summary>The definition of the style <paramref name="name"/>, or <see langword="null"/> for a name the set doesn't hold.</summary>
    /// <param name="name">The style name, matched ordinally.</param>
    public ComplexLineStyle? GetDefinition(string name) =>
        _styles.TryGetValue(name, out var entry) ? entry.Definition : null;
}
