using EncDotNet.S100.Collections.Noaa;

namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects cells from the NOAA ENC product catalogue by state, Coast Guard
/// district or region.
/// </summary>
/// <remarks>
/// A cell matches when it lies in <em>any</em> selected state, district or
/// region (the selections are unioned). With nothing selected, every cell
/// matches. Cancelled cells are excluded unless
/// <see cref="IncludeCancelled"/> is set.
/// </remarks>
public sealed record NoaaEncFilter
{
    /// <summary>A filter that matches every active cell.</summary>
    public static NoaaEncFilter All { get; } = new();

    /// <summary>Two-letter state or territory codes (e.g. <c>AK</c>).</summary>
    public IReadOnlyList<string> States { get; init; } = [];

    /// <summary>US Coast Guard district numbers.</summary>
    public IReadOnlyList<int> CoastGuardDistricts { get; init; } = [];

    /// <summary>NOAA region numbers.</summary>
    public IReadOnlyList<int> Regions { get; init; } = [];

    /// <summary>Whether cancelled cells are included.</summary>
    public bool IncludeCancelled { get; init; }

    /// <summary>True when no state, district or region is selected.</summary>
    public bool IsUnscoped => States.Count == 0 && CoastGuardDistricts.Count == 0 && Regions.Count == 0;

    /// <summary>Returns true when <paramref name="cell"/> passes the filter.</summary>
    public bool Matches(NoaaEncCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.IsCancelled && !IncludeCancelled)
            return false;

        if (IsUnscoped)
            return true;

        return cell.States.Any(s => States.Contains(s, StringComparer.OrdinalIgnoreCase))
            || cell.CoastGuardDistricts.Any(CoastGuardDistricts.Contains)
            || cell.Regions.Any(Regions.Contains);
    }

    /// <summary>A canonical text form, stable across list order, for fingerprints.</summary>
    public string ToCanonicalString() =>
        string.Join(';',
            "s=" + string.Join(',', States.Select(s => s.ToUpperInvariant()).Order(StringComparer.Ordinal)),
            "d=" + string.Join(',', CoastGuardDistricts.Order()),
            "r=" + string.Join(',', Regions.Order()),
            "c=" + (IncludeCancelled ? "1" : "0"));

    /// <inheritdoc/>
    public bool Equals(NoaaEncFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
