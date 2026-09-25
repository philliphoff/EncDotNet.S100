namespace EncDotNet.S100.Collections.Noaa;

/// <summary>One selectable value of a NOAA catalogue facet, with what selecting it would include.</summary>
/// <param name="Value">The facet value (a state code, or a district or region number as text).</param>
/// <param name="CellCount">The number of cells with that value.</param>
/// <param name="TotalBytes">The total download size of those cells.</param>
public sealed record NoaaEncFacetValue(string Value, int CellCount, long TotalBytes);

/// <summary>
/// The states, Coast Guard districts and regions present in a NOAA ENC
/// product catalogue, with cell counts and download sizes, for choosing a
/// <see cref="NoaaEncFilter"/>.
/// </summary>
/// <param name="States">Per-state counts, by state code.</param>
/// <param name="CoastGuardDistricts">Per-district counts, by district number.</param>
/// <param name="Regions">Per-region counts, by region number.</param>
public sealed record NoaaEncFacets(
    IReadOnlyList<NoaaEncFacetValue> States,
    IReadOnlyList<NoaaEncFacetValue> CoastGuardDistricts,
    IReadOnlyList<NoaaEncFacetValue> Regions)
{
    /// <summary>
    /// Computes the facets of <paramref name="catalog"/>, counting only cells
    /// that are not cancelled unless <paramref name="includeCancelled"/> is set.
    /// </summary>
    public static NoaaEncFacets Compute(NoaaEncProductCatalog catalog, bool includeCancelled = false)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var cells = catalog.Cells.Where(c => includeCancelled || !c.IsCancelled).ToArray();
        return new NoaaEncFacets(
            Tally(cells, c => c.States),
            Tally(cells, c => c.CoastGuardDistricts.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Tally(cells, c => c.Regions.Select(r => r.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// Summarises what <paramref name="filter"/> selects from
    /// <paramref name="catalog"/>: each cell counts once even when it lies in
    /// several selected states, districts or regions.
    /// </summary>
    public static (int CellCount, long TotalBytes) Summarize(NoaaEncProductCatalog catalog, NoaaEncFilter filter)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(filter);

        var selected = catalog.Cells.Where(filter.Matches).ToArray();
        return (selected.Length, selected.Sum(c => c.ZipSize ?? 0));
    }

    private static NoaaEncFacetValue[] Tally(IEnumerable<NoaaEncCell> cells, Func<NoaaEncCell, IEnumerable<string>> values) =>
        cells
            .SelectMany(c => values(c).Distinct(StringComparer.OrdinalIgnoreCase).Select(v => (Value: v, Cell: c)))
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NoaaEncFacetValue(g.Key, g.Count(), g.Sum(x => x.Cell.ZipSize ?? 0)))
            .OrderBy(f => f.Value.Length)
            .ThenBy(f => f.Value, StringComparer.Ordinal)
            .ToArray();
}
