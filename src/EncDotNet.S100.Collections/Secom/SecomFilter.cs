namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects objects from a SECOM service: by product specification (matched
/// on this side, since services spell product types differently) and,
/// optionally, by an area the service filters on. With nothing selected,
/// every object matches.
/// </summary>
public sealed record SecomFilter
{
    /// <summary>A filter that matches every object.</summary>
    public static SecomFilter All { get; } = new();

    /// <summary>Product specifications (e.g. <c>S-124</c>), matched case-insensitively.</summary>
    public IReadOnlyList<string> ProductSpecs { get; init; } = [];

    /// <summary>
    /// A WKT polygon the objects must intersect, sent to the service as the
    /// <c>geometry</c> query parameter; <see langword="null"/> for no area.
    /// </summary>
    public string? GeometryWkt { get; init; }

    /// <summary>True when no product is selected.</summary>
    public bool IsUnscoped => ProductSpecs.Count == 0;

    /// <summary>Returns true when <paramref name="item"/> is one of the selected products.</summary>
    public bool Matches(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsUnscoped || ProductSpecs.Contains(item.ProductSpec, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A canonical text form, stable across list order and case, for fingerprints.</summary>
    public string ToCanonicalString() =>
        "p=" + string.Join(',', ProductSpecs.Select(p => p.ToUpperInvariant()).Order(StringComparer.Ordinal))
        + ";g=" + (GeometryWkt?.Trim() ?? string.Empty);

    /// <inheritdoc/>
    public bool Equals(SecomFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
