using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// The feature an entry was opened from (symbol dictionary design note
/// §4.4, C3, R13c, #1722). An attribute entry opened with one shows the
/// symbol each value gives that feature type. It comes from a pick, with the
/// feature's own values, or from a link inside a feature type's entry,
/// without values.
/// </summary>
/// <param name="FeatureType">The feature type's code in the index, e.g. <c>LateralBeacon</c>.</param>
/// <param name="Values">
/// The picked feature's top-level attributes by their codes in the index
/// (<c>colour</c> → <c>3</c>; a list joined with commas), or
/// <see langword="null"/> when the context comes from a link and has no feature.
/// </param>
/// <param name="Primitive">The picked feature's geometry (<c>Point</c>, <c>Curve</c>, <c>Surface</c>), or <see langword="null"/>.</param>
internal sealed record ReferenceFeatureContext(
    string FeatureType,
    IReadOnlyDictionary<string, string>? Values = null,
    string? Primitive = null)
{
    /// <summary>Whether it comes from a pick, so it has the feature's own values.</summary>
    public bool IsPick => Values is not null;

    /// <summary>
    /// The context an entry <paramref name="key"/> opens with from an entry
    /// shown with <paramref name="context"/>: kept for an attribute and for
    /// the same feature type, dropped for anything else.
    /// </summary>
    /// <param name="key">The entry being opened.</param>
    /// <param name="context">The context of the entry it is opened from, or <see langword="null"/>.</param>
    /// <returns>The context, or <see langword="null"/>.</returns>
    public static ReferenceFeatureContext? For(ReferenceKey key, ReferenceFeatureContext? context) =>
        context is not null && (key.Kind == ReferenceKind.Attribute
            || (key.Kind == ReferenceKind.FeatureType && string.Equals(key.Code, context.FeatureType, StringComparison.OrdinalIgnoreCase)))
            ? context
            : null;

    /// <summary>The picked feature's value of <paramref name="attribute"/>, or <see langword="null"/> when it has none or there is no pick.</summary>
    /// <param name="attribute">The attribute's code in the index.</param>
    public string? ValueOf(string attribute) =>
        Values is not null && Values.TryGetValue(attribute, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Two contexts are the same when they name the same feature type, the
    /// same geometry and the same values.
    /// </summary>
    /// <param name="other">The other context.</param>
    public bool Equals(ReferenceFeatureContext? other) =>
        other is not null
        && string.Equals(FeatureType, other.FeatureType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Primitive, other.Primitive, StringComparison.Ordinal)
        && SameValues(Values, other.Values);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(FeatureType);

    private static bool SameValues(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        return a.Count == b.Count
            && a.All(p => b.TryGetValue(p.Key, out var v) && string.Equals(v, p.Value, StringComparison.Ordinal));
    }
}
