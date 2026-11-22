namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// What a product draws in the map view, as Chart reference's <b>In view</b>
/// reads it (symbol dictionary design note §4.5, R5, #1638).
/// </summary>
/// <remarks>
/// Both sets come from the display lists the stack already holds, through
/// the same extent test and scale as the Legend tab's catalogue key
/// (<see cref="CatalogueSymbolKey.Drawn"/>), so the two agree.
/// </remarks>
/// <param name="Symbols">The point symbols drawn in view, by name (<c>BOYCAN01</c>), as the catalogue key counts them.</param>
/// <param name="FeatureTypes">The feature classes drawn in view, by the code in the processor's feature list, compared without regard to case.</param>
internal sealed record ChartReferenceDrawn(IReadOnlySet<string> Symbols, IReadOnlySet<string> FeatureTypes)
{
    /// <summary>Nothing drawn.</summary>
    public static ChartReferenceDrawn None { get; } =
        new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// A Chart reference row hovered or focused, as hover to find rings it
/// (<see cref="LegendFindService.HoverReference"/>, legends D19): the
/// features of a product that draw any of <see cref="Symbols"/> or are of
/// any of <see cref="FeatureTypes"/>.
/// </summary>
/// <param name="Spec">The product's portrayal spec name (<c>S-101</c>).</param>
/// <param name="Key">The row's entry key, which tells two targets apart.</param>
/// <param name="Symbols">Point symbol names: a symbol's own, or a family's members.</param>
/// <param name="FeatureTypes">Feature class codes: a feature type's code and its S-57 acronym.</param>
internal sealed record ChartReferenceTarget(
    string Spec,
    string Key,
    IReadOnlyCollection<string> Symbols,
    IReadOnlyCollection<string> FeatureTypes)
{
    /// <summary>Whether <paramref name="other"/> names the same row.</summary>
    /// <param name="other">Another target, or null.</param>
    public bool IsSameRow(ChartReferenceTarget? other) =>
        other is not null
        && string.Equals(Spec, other.Spec, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Key, other.Key, StringComparison.Ordinal);
}
