namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects groups from a collection manifest. With no group selected, every
/// group matches — including groups added to the manifest later.
/// </summary>
public sealed record LocalManifestFilter
{
    /// <summary>A filter that matches every group.</summary>
    public static LocalManifestFilter All { get; } = new();

    /// <summary>Group ids (e.g. <c>AU</c>), matched case-insensitively.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>True when no group is selected.</summary>
    public bool IsUnscoped => Groups.Count == 0;

    /// <summary>Returns true when the group with <paramref name="groupId"/> passes the filter.</summary>
    public bool Matches(string groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return IsUnscoped || Groups.Contains(groupId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A canonical text form, stable across list order and case, for fingerprints.</summary>
    public string ToCanonicalString() =>
        "g=" + string.Join(',', Groups.Select(g => g.ToUpperInvariant()).Order(StringComparer.Ordinal));

    /// <inheritdoc/>
    public bool Equals(LocalManifestFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
