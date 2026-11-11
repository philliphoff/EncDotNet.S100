using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// The extra drawing resources of an <see cref="IAmlStyleModule"/>: SVG
/// symbols, line styles and area fills that its classes'
/// <see cref="AmlClassStyle.Build"/> functions draw with, beyond the shared
/// AML signature, such as the sediment patterns of the environmental family.
/// The catalogue serves them with its own symbols, line styles and area
/// fills.
/// </summary>
/// <remarks>
/// <para>
/// Name them after the family's <see cref="AmlFamily.SymbolStem"/> and an
/// underscore, such as <c>AML_ENVIRONMENTAL_SCOUR</c>: a glyph's name has no
/// underscore, so they never collide with the framed symbols.
/// </para>
/// <para>
/// A family of symbols or fills too large to list, such as one pattern per
/// sediment mixture, is made on demand by <see cref="SymbolResolver"/> and
/// <see cref="AreaFillResolver"/>, which the catalogue asks for any name it
/// doesn't hold. They return <see langword="null"/> for a name that isn't
/// theirs.
/// </para>
/// </remarks>
public sealed record AmlStyleResources
{
    /// <summary>No resources.</summary>
    public static AmlStyleResources Empty { get; } = new();

    /// <summary>SVG symbols by name, with S-100 colour classes.</summary>
    public IReadOnlyDictionary<string, string> Symbols { get; init; } = new Dictionary<string, string>();

    /// <summary>Line styles, referenced by name from complex-line instructions.</summary>
    public IReadOnlyList<LineStyle> LineStyles { get; init; } = [];

    /// <summary>Makes a symbol the module draws with by name, or <see langword="null"/> when the name isn't the module's.</summary>
    public Func<string, string?>? SymbolResolver { get; init; }

    /// <summary>Makes an area fill the module draws with by name, or <see langword="null"/> when the name isn't the module's.</summary>
    public Func<string, AreaFill?>? AreaFillResolver { get; init; }
}
