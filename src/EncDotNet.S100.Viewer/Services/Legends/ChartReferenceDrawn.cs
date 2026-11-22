using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// What a product draws in the map view, as Chart reference's <b>In view</b>
/// reads it (symbol dictionary design note §4.5, R5, #1638, #1653).
/// </summary>
/// <remarks>
/// The sets come from the display lists the stack already holds, through
/// the same extent test and scale as the Legend tab's catalogue key
/// (<see cref="CatalogueSymbolKey.Drawn"/>, <see cref="CatalogueLinework.Drawn"/>),
/// so the two agree. A catalogue item is matched by its kind as well as its
/// id (<see cref="Contains"/>): the S-101 catalogue names some point symbols
/// and line styles alike (<c>CTNARE51</c>).
/// </remarks>
/// <param name="Symbols">The point symbols drawn in view, by name (<c>BOYCAN01</c>), as the catalogue key counts them.</param>
/// <param name="LineStyles">The line styles drawn in view, by name (<c>ACHARE51</c>).</param>
/// <param name="AreaFills">The area fills drawn in view, by name (<c>DRGARE01</c>).</param>
/// <param name="FeatureTypes">The feature classes drawn in view, by the code in the processor's feature list, compared without regard to case.</param>
internal sealed record ChartReferenceDrawn(
    IReadOnlySet<string> Symbols,
    IReadOnlySet<string> LineStyles,
    IReadOnlySet<string> AreaFills,
    IReadOnlySet<string> FeatureTypes)
{
    /// <summary>Nothing drawn.</summary>
    public static ChartReferenceDrawn None { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether the catalogue item of <paramref name="kind"/> named <paramref name="name"/> is drawn.</summary>
    /// <param name="kind">The item's kind.</param>
    /// <param name="name">The item's id, or a feature class code.</param>
    public bool Contains(ReferenceKind kind, string name) => kind switch
    {
        ReferenceKind.Symbol => Symbols.Contains(name),
        ReferenceKind.LineStyle => LineStyles.Contains(name),
        ReferenceKind.AreaFill => AreaFills.Contains(name),
        ReferenceKind.FeatureType => FeatureTypes.Contains(name),
        _ => false,
    };

    /// <summary>Whether <paramref name="other"/> lists the same items.</summary>
    /// <param name="other">Another drawn set.</param>
    public bool SetEquals(ChartReferenceDrawn other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Symbols.SetEquals(other.Symbols)
            && LineStyles.SetEquals(other.LineStyles)
            && AreaFills.SetEquals(other.AreaFills)
            && FeatureTypes.SetEquals(other.FeatureTypes);
    }
}

/// <summary>
/// A Chart reference row hovered or focused, as hover to find rings it
/// (<see cref="LegendFindService.HoverReference"/>, legends D19): the
/// features of a product that draw any of <see cref="Symbols"/>,
/// <see cref="LineStyles"/> or <see cref="AreaFills"/>, or are of any of
/// <see cref="FeatureTypes"/>.
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
    /// <summary>Line style names: the curves and outlines drawn with them are ringed (#1653).</summary>
    public IReadOnlyCollection<string> LineStyles { get; init; } = [];

    /// <summary>Area fill names: the surfaces filled with them are ringed as outlines (#1653).</summary>
    public IReadOnlyCollection<string> AreaFills { get; init; } = [];

    /// <summary>Whether <paramref name="other"/> names the same row.</summary>
    /// <param name="other">Another target, or null.</param>
    public bool IsSameRow(ChartReferenceTarget? other) =>
        other is not null
        && string.Equals(Spec, other.Spec, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Key, other.Key, StringComparison.Ordinal);
}
