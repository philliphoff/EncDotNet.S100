using System.Text.Json.Serialization;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections;

/// <summary>
/// One coverage polygon: an exterior ring and zero or more holes, with
/// (latitude, longitude) vertices in EPSG:4326.
/// </summary>
/// <param name="Exterior">The exterior ring's vertices.</param>
/// <param name="Holes">The interior rings' vertices.</param>
[method: JsonConstructor]
public sealed record GeoPolygon(
    IReadOnlyList<GeoPosition> Exterior,
    IReadOnlyList<IReadOnlyList<GeoPosition>> Holes)
{
    /// <summary>Creates a polygon with no holes.</summary>
    public GeoPolygon(IReadOnlyList<GeoPosition> exterior)
        : this(exterior, [])
    {
    }
}

/// <summary>
/// The coverage of a collection item as a multi-polygon in EPSG:4326.
/// </summary>
/// <remarks>
/// Rings are stored as published by the source, including any that cross the
/// antimeridian; display code is responsible for normalising them.
/// </remarks>
/// <param name="Polygons">The coverage polygons.</param>
public sealed record GeoCoverage(IReadOnlyList<GeoPolygon> Polygons)
{
    /// <summary>
    /// The bounds of every exterior ring, or <see langword="null"/> when the
    /// coverage has no vertices.
    /// </summary>
    public GeoBounds? ComputeBounds() =>
        GeoBounds.UnionAll(Polygons
            .Select(p => GeoBounds.FromPositions(p.Exterior))
            .OfType<GeoBounds>());

    /// <summary>
    /// Builds a coverage from polygons, returning <see langword="null"/> when
    /// none has an exterior ring with at least three vertices.
    /// </summary>
    public static GeoCoverage? FromPolygons(IEnumerable<GeoPolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);

        var valid = polygons.Where(p => p.Exterior.Count >= 3).ToArray();
        return valid.Length == 0 ? null : new GeoCoverage(valid);
    }
}
