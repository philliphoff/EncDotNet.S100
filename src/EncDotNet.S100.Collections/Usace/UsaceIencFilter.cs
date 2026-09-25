using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects cells from a USACE Inland ENC product catalogue by river. With no
/// river selected, every cell matches.
/// </summary>
public sealed record UsaceIencFilter
{
    /// <summary>A filter that matches every cell.</summary>
    public static UsaceIencFilter All { get; } = new();

    /// <summary>River names (e.g. <c>Ohio</c>), matched case-insensitively.</summary>
    public IReadOnlyList<string> Rivers { get; init; } = [];

    /// <summary>True when no river is selected.</summary>
    public bool IsUnscoped => Rivers.Count == 0;

    /// <summary>Returns true when <paramref name="cell"/> passes the filter.</summary>
    public bool Matches(UsaceIencCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return IsUnscoped || (cell.River is { } river && Rivers.Contains(river, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>A canonical text form, stable across list order and case, for fingerprints.</summary>
    public string ToCanonicalString() =>
        "r=" + string.Join(',', Rivers.Select(r => r.ToUpperInvariant()).Order(StringComparer.Ordinal));

    /// <inheritdoc/>
    public bool Equals(UsaceIencFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
