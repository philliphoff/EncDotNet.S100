using System.Runtime.CompilerServices;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.RemoteCatalogues;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// One area of a remote S-100 catalogue (a folder such as
/// <c>Northeast/Boston</c>) drawn as a single outline when zoomed out (#685,
/// handoff E2): the union of its tiles' coverage, in Web Mercator metres.
/// </summary>
/// <param name="Folder">The area's folder (its group id).</param>
/// <param name="Shape">The union of the area's tiles.</param>
/// <param name="Rings">The shape's rings (exteriors and holes), for drawing.</param>
internal sealed record LibraryArea(string Folder, Geometry Shape, IReadOnlyList<(double X, double Y)[]> Rings)
{
    /// <summary>For a forecast model's domain (#685, handoff E1), the model (e.g. <c>cbofs</c>); otherwise <see langword="null"/>.</summary>
    public string? Model { get; init; }

    /// <summary>True when the Web Mercator point (<paramref name="x"/>, <paramref name="y"/>) lies in the area.</summary>
    public bool Contains(double x, double y) => Shape.Contains(Shape.Factory.CreatePoint(new Coordinate(x, y)));

    /// <summary>The area's extent in Web Mercator metres.</summary>
    public Envelope Extent => Shape.EnvelopeInternal;
}

/// <summary>Builds (and caches, per source index) the outlines of a remote catalogue's areas.</summary>
internal static class CoverageAreas
{
    private static readonly GeometryFactory Factory = new();

    private static readonly ConditionalWeakTable<SourceIndex, Dictionary<string, LibraryArea>> Cache = new();

    /// <summary>
    /// The outline of the area <paramref name="folder"/> of <paramref name="index"/>:
    /// the union of every tile in it, whatever is listed, so the shape does not
    /// change with the panel's filters. <see langword="null"/> when no tile has coverage.
    /// </summary>
    public static LibraryArea? Get(SourceIndex index, string folder)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(folder);
        return Get(index, "folder:" + folder, folder, null, i => RemoteS100Catalogue.FolderOf(i) == folder);
    }

    /// <summary>
    /// The domain of the forecast <paramref name="model"/> in <paramref name="index"/>:
    /// the union of its tiles (or of its one file's coverage). <see langword="null"/>
    /// when nothing in it has coverage.
    /// </summary>
    public static LibraryArea? GetModel(SourceIndex index, string model)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(model);
        return Get(index, "model:" + model, model, model, i => ForecastRuns.ModelOf(i) == model);
    }

    private static LibraryArea? Get(SourceIndex index, string cacheKey, string folder, string? model, Func<CollectionItem, bool> member)
    {
        var areas = Cache.GetValue(index, static _ => new Dictionary<string, LibraryArea>(StringComparer.Ordinal));
        lock (areas)
        {
            if (areas.TryGetValue(cacheKey, out var cached))
                return cached;

            var polygons = index.Items
                .Where(member)
                .SelectMany(Exteriors)
                .ToArray();
            if (polygons.Length == 0)
                return null;

            Geometry shape;
            try
            {
                shape = CascadedPolygonUnion.Union(polygons);
            }
            catch (TopologyException)
            {
                shape = Factory.BuildGeometry(polygons).Buffer(0);
            }

            var area = new LibraryArea(folder, shape, RingsOf(shape)) { Model = model };
            areas[cacheKey] = area;
            return area;
        }
    }

    /// <summary>A tile's coverage exteriors (or its bounds) as Web Mercator polygons.</summary>
    private static IEnumerable<Polygon> Exteriors(CollectionItem item)
    {
        // ToMercatorRings lists exteriors and holes; a tile's are all exteriors in practice,
        // and a hole would only shrink the union, so closed rings are taken as exteriors.
        foreach (var ring in CoverageGeometry.ToMercatorRings(item))
        {
            if (ring.Length < 4 || ring[0] != ring[^1])
                continue;
            var coordinates = ring.Select(p => new Coordinate(p.X, p.Y)).ToArray();
            var polygon = Factory.CreatePolygon(coordinates);
            yield return polygon.IsValid ? polygon : (Polygon)polygon.Buffer(0).GetGeometryN(0);
        }
    }

    private static IReadOnlyList<(double X, double Y)[]> RingsOf(Geometry shape)
    {
        var rings = new List<(double X, double Y)[]>();
        for (var i = 0; i < shape.NumGeometries; i++)
        {
            if (shape.GetGeometryN(i) is not Polygon polygon)
                continue;
            rings.Add(polygon.ExteriorRing.Coordinates.Select(c => (c.X, c.Y)).ToArray());
            foreach (var hole in polygon.InteriorRings)
                rings.Add(hole.Coordinates.Select(c => (c.X, c.Y)).ToArray());
        }

        return rings;
    }
}
