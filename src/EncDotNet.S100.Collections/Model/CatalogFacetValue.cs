namespace EncDotNet.S100.Collections;

/// <summary>
/// One selectable value of an online catalogue's facet (a NOAA state,
/// district or region, a USACE river, …) with what selecting it would include.
/// </summary>
/// <param name="Value">The facet value (e.g. a state code, a district number as text, or a river name).</param>
/// <param name="CellCount">The number of cells with that value.</param>
/// <param name="TotalBytes">The total download size of those cells.</param>
public sealed record CatalogFacetValue(string Value, int CellCount, long TotalBytes);
