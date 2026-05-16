using System.Collections.Immutable;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Geometry helpers for <see cref="IGmlFeature"/> instances.
/// </summary>
/// <remarks>
/// All operations work in planar lat/lon space and match the precision
/// of the per-dataset bounding box. Surface geometry takes precedence
/// over curve, which takes precedence over point — matching the
/// preference order in
/// <see cref="EncDotNet.S100.Pipelines.Vector.GmlFeatureGeometryProvider{TFeature}"/>.
/// </remarks>
public static class GmlFeatureGeometry
{
    /// <summary>
    /// Computes the bounding box of <paramref name="feature"/>'s
    /// geometry. Returns <c>null</c> when the feature has no geometry
    /// at all (e.g. container-style features such as
    /// <c>S131:Authority</c>).
    /// </summary>
    public static BoundingBox? TryGetBoundingBox(IGmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var south = double.PositiveInfinity;
        var north = double.NegativeInfinity;
        var west = double.PositiveInfinity;
        var east = double.NegativeInfinity;
        var any = false;

        void Accumulate((double Latitude, double Longitude) p)
        {
            any = true;
            if (p.Latitude < south) south = p.Latitude;
            if (p.Latitude > north) north = p.Latitude;
            if (p.Longitude < west) west = p.Longitude;
            if (p.Longitude > east) east = p.Longitude;
        }

        if (!feature.ExteriorRing.IsDefaultOrEmpty)
        {
            foreach (var p in feature.ExteriorRing) Accumulate(p);
        }

        if (!feature.Curves.IsDefaultOrEmpty)
        {
            foreach (var curve in feature.Curves)
            {
                if (!curve.IsDefaultOrEmpty)
                {
                    foreach (var p in curve) Accumulate(p);
                }
            }
        }

        if (!feature.Points.IsDefaultOrEmpty)
        {
            foreach (var p in feature.Points) Accumulate(p);
        }

        return any ? new BoundingBox(south, west, north, east) : null;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="feature"/>'s bounding
    /// box intersects (or touches) the supplied <paramref name="query"/>'s
    /// coarse bounding box. Features without geometry never match.
    /// </summary>
    public static bool Intersects(IGmlFeature feature, GeoQuery query)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(query);

        var bounds = TryGetBoundingBox(feature);
        if (bounds is null) return false;

        return query switch
        {
            GeoQuery.Point p => SpatialPredicates.Contains(bounds, p.Value),
            _ => SpatialPredicates.Intersects(bounds, query),
        };
    }
}
