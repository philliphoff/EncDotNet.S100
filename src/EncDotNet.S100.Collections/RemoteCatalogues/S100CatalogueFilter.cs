namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects datasets from a remote S-100 exchange catalogue by folder (for
/// NOAA's S-102, a region such as <c>Northeast</c> or an area such as
/// <c>Northeast/Long_Island_Sound</c>) and by navigation purpose (e.g.
/// <c>port</c>, <c>transit</c>). An empty list leaves that facet unscoped.
/// </summary>
public sealed record S100CatalogueFilter
{
    /// <summary>A filter that matches every dataset.</summary>
    public static S100CatalogueFilter All { get; } = new();

    /// <summary>
    /// Folders relative to the exchange set's root (forward slashes); a
    /// dataset matches a folder it lies in or below. Matched case-insensitively.
    /// </summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>Navigation purposes (the catalogue's <c>navigationPurpose</c>), matched case-insensitively.</summary>
    public IReadOnlyList<string> NavigationPurposes { get; init; } = [];

    /// <summary>True when neither folders nor purposes are selected.</summary>
    public bool IsUnscoped => Folders.Count == 0 && NavigationPurposes.Count == 0;

    /// <summary>
    /// Returns true when a dataset in <paramref name="folder"/> with
    /// <paramref name="navigationPurpose"/> passes the filter.
    /// </summary>
    public bool Matches(string folder, string? navigationPurpose)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return MatchesFolder(folder)
            && (NavigationPurposes.Count == 0
                || (navigationPurpose is not null && NavigationPurposes.Contains(navigationPurpose, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>Returns true when <paramref name="folder"/> is, or lies below, a selected folder (or none is selected).</summary>
    public bool MatchesFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return Folders.Count == 0 || Folders.Any(f =>
        {
            var selected = f.Trim('/');
            return folder.Equals(selected, StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith(selected + "/", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>A canonical text form, stable across list order and case, for fingerprints.</summary>
    public string ToCanonicalString() =>
        "f=" + string.Join(',', Folders.Select(f => f.Trim('/').ToUpperInvariant()).Order(StringComparer.Ordinal))
        + ";n=" + string.Join(',', NavigationPurposes.Select(p => p.ToUpperInvariant()).Order(StringComparer.Ordinal));

    /// <inheritdoc/>
    public bool Equals(S100CatalogueFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
