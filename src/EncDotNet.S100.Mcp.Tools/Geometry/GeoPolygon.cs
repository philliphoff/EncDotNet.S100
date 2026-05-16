using System.Collections.Immutable;

namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// A simple (non-self-intersecting) geographic polygon, expressed as a
/// closed ring of WGS-84 points in <c>lat lon</c> order.
/// </summary>
/// <param name="Ring">
/// At least four points (the first and last must be equal). Points are
/// interpreted as a planar ring on the lat/lon graticule — no
/// great-circle interpolation is performed.
/// </param>
/// <remarks>
/// Polygon membership in this layer is computed via a planar
/// ray-casting test against the supplied ring. This is consistent with
/// the bbox-only spatial filters used elsewhere in the tools surface
/// and intentionally avoids pulling in a heavier geometry library.
/// Polygons that span large arcs of latitude or cross the antimeridian
/// are not supported.
/// </remarks>
public sealed record GeoPolygon(ImmutableArray<GeoPoint> Ring);
