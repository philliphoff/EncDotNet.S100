namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects items from an S-100 feed by product specification. With no
/// product selected, every item matches.
/// </summary>
public sealed record S100FeedFilter
{
    /// <summary>A filter that matches every item.</summary>
    public static S100FeedFilter All { get; } = new();

    /// <summary>Product specifications (e.g. <c>S-101</c>), matched case-insensitively.</summary>
    public IReadOnlyList<string> ProductSpecs { get; init; } = [];

    /// <summary>True when no product is selected.</summary>
    public bool IsUnscoped => ProductSpecs.Count == 0;

    /// <summary>Returns true when <paramref name="item"/> passes the filter.</summary>
    public bool Matches(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsUnscoped || ProductSpecs.Contains(item.ProductSpec, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A canonical text form, stable across list order and case, for fingerprints.</summary>
    public string ToCanonicalString() =>
        "p=" + string.Join(',', ProductSpecs.Select(p => p.ToUpperInvariant()).Order(StringComparer.Ordinal));

    /// <inheritdoc/>
    public bool Equals(S100FeedFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
