using EncDotNet.S100.Collections.ChartCatalogs;

namespace EncDotNet.S100.Collections;

/// <summary>
/// Selects entries from a community chart list by number. With no entry
/// selected, every entry matches.
/// </summary>
public sealed record ChartCatalogsFilter
{
    /// <summary>A filter that matches every entry.</summary>
    public static ChartCatalogsFilter All { get; } = new();

    /// <summary>Entry numbers (<see cref="ChartCatalogsChart.Number"/>), matched case-insensitively.</summary>
    public IReadOnlyList<string> Charts { get; init; } = [];

    /// <summary>True when no entry is selected.</summary>
    public bool IsUnscoped => Charts.Count == 0;

    /// <summary>Returns true when <paramref name="chart"/> passes the filter.</summary>
    public bool Matches(ChartCatalogsChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return IsUnscoped || Charts.Contains(chart.Number, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A canonical text form, stable across list order and case, for fingerprints.</summary>
    public string ToCanonicalString() =>
        "c=" + string.Join(',', Charts.Select(c => c.ToUpperInvariant()).Order(StringComparer.Ordinal));

    /// <inheritdoc/>
    public bool Equals(ChartCatalogsFilter? other) =>
        other is not null && ToCanonicalString() == other.ToCanonicalString();

    /// <inheritdoc/>
    public override int GetHashCode() => ToCanonicalString().GetHashCode(StringComparison.Ordinal);
}
