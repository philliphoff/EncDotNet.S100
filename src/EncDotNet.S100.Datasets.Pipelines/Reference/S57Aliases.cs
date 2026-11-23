using EncDotNet.S100.Core;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Maps S-57 object class and attribute acronyms to the entries of a
/// <see cref="ChartReferenceIndex"/> through the feature catalogue's S-57
/// aliases: <c>BCNLAT</c> to Lateral Beacon, <c>COLOUR</c> to Colour, and an
/// S-57 listed value to the S-101 listed value with the same code.
/// </summary>
/// <remarks>
/// <para>
/// S-57 cells are drawn with the S-101 catalogue, so their picks keep their
/// S-57 codes while the chart reference describes S-101 entries (symbol
/// dictionary design note §4.3, R9). An acronym the catalogue gives no
/// entry, or gives to more than one (S-101 splits <c>LIGHTS</c> into
/// all-round, sectored and air obstruction lights), maps to nothing.
/// </para>
/// <para>
/// Read one with <see cref="ChartReferenceIndex.S57Aliases"/>. It is
/// immutable and safe to read from any thread.
/// </para>
/// </remarks>
public sealed class S57Aliases
{
    private readonly Dictionary<string, FeatureTypeEntry?> _featureTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AttributeEntry?> _attributes = new(StringComparer.OrdinalIgnoreCase);

    internal S57Aliases(ChartReferenceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        Spec = index.Spec;
        foreach (var feature in index.FeatureTypes)
            Add(_featureTypes, feature.Alias, feature);
        foreach (var attribute in index.Attributes)
            Add(_attributes, attribute.Alias, attribute);
    }

    /// <summary>The product specification whose entries the aliases name.</summary>
    public SpecRef Spec { get; }

    /// <summary>The feature type an S-57 object class acronym names, such as Lateral Beacon for <c>BCNLAT</c>.</summary>
    /// <param name="acronym">The acronym, without regard to case.</param>
    /// <returns>The entry, or <see langword="null"/> when no entry, or more than one, has the alias.</returns>
    public FeatureTypeEntry? FeatureType(string? acronym) =>
        acronym is not null && _featureTypes.TryGetValue(acronym, out var entry) ? entry : null;

    /// <summary>The attribute an S-57 attribute acronym names, such as Colour for <c>COLOUR</c>.</summary>
    /// <param name="acronym">The acronym, without regard to case.</param>
    /// <returns>The entry, or <see langword="null"/> when no entry, or more than one, has the alias.</returns>
    public AttributeEntry? Attribute(string? acronym) =>
        acronym is not null && _attributes.TryGetValue(acronym, out var entry) ? entry : null;

    /// <summary>
    /// The listed values an S-57 attribute value names: one per code of a
    /// list such as <c>3,1</c> (red, white), matched by code.
    /// </summary>
    /// <param name="acronym">The attribute's acronym.</param>
    /// <param name="value">The value as encoded.</param>
    /// <returns>
    /// The attribute's entry and its listed values, in order;
    /// <see langword="null"/> when the acronym maps to no attribute or the
    /// value to no listed values (<see cref="AttributeEntry.ListedValuesOf"/>).
    /// </returns>
    public (AttributeEntry Attribute, IReadOnlyList<ListedValue> Values)? ListedValues(string? acronym, string? value) =>
        Attribute(acronym) is { } attribute && attribute.ListedValuesOf(value) is { } values
            ? (attribute, values)
            : null;

    // A second entry with the same alias makes it ambiguous: null.
    private static void Add<T>(Dictionary<string, T?> map, string? alias, T entry)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(alias))
            return;
        alias = alias.Trim();
        map[alias] = map.ContainsKey(alias) ? null : entry;
    }
}
