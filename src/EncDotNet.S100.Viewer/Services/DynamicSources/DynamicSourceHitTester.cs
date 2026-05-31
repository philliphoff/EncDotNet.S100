using System;
using System.Collections.Generic;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Pure, stateless hit-tester for dynamic features.
/// </summary>
/// <remarks>
/// <para>
/// Inputs: a click <see cref="MPoint"/> in Spherical Mercator world
/// units, the current viewport <c>resolution</c> (map units per
/// device pixel), and the candidate sources to walk.
/// </para>
/// <para>
/// Tolerance is fixed at <see cref="ToleranceDevicePixels"/> device
/// pixels (12, matching the AIS pictogram outer-disc radius). The
/// effective hit radius in map units is
/// <c>ToleranceDevicePixels * resolution</c>.
/// </para>
/// <para>
/// v1 treats every dynamic feature as a point regardless of its
/// declared <see cref="GeometryType"/> — only point features ship
/// today. Line / polygon dynamic features will get their own paths
/// when a producer needs them; see
/// <c>docs/design/dynamic-source-pick.md</c> §1 Q2.
/// </para>
/// </remarks>
internal static class DynamicSourceHitTester
{
    /// <summary>
    /// Hit-test radius in device pixels. Matches the AIS pictogram's
    /// outer disc so a click "on" the symbol picks reliably.
    /// </summary>
    public const double ToleranceDevicePixels = 12.0;

    /// <summary>
    /// Returns hits ordered by ascending distance from
    /// <paramref name="mapPoint"/>. Sources with no candidate features
    /// (or zero matches) are silently skipped.
    /// </summary>
    /// <param name="mapPoint">Click position in Spherical Mercator world units.</param>
    /// <param name="resolution">Map units per device pixel at the current zoom.</param>
    /// <param name="sources">
    /// Sources to walk. Callers are expected to filter to currently
    /// visible sources; the tester does not consult the registry
    /// itself.
    /// </param>
    public static IReadOnlyList<DynamicHit> HitTest(
        MPoint mapPoint,
        double resolution,
        IEnumerable<IDynamicFeatureSource> sources)
    {
        ArgumentNullException.ThrowIfNull(mapPoint);
        ArgumentNullException.ThrowIfNull(sources);

        if (resolution <= 0 || double.IsNaN(resolution) || double.IsInfinity(resolution))
        {
            return Array.Empty<DynamicHit>();
        }

        var toleranceMapUnits = ToleranceDevicePixels * resolution;
        var toleranceSquared = toleranceMapUnits * toleranceMapUnits;

        var hits = new List<DynamicHit>();
        foreach (var source in sources)
        {
            if (source is null) continue;
            foreach (var feature in source.CurrentFeatures)
            {
                if (feature.Coordinates is null || feature.Coordinates.Count == 0)
                    continue;

                // v1: point-only. Take the first coordinate as the
                // representative point regardless of GeometryType.
                var (lat, lon) = feature.Coordinates[0];
                if (double.IsNaN(lat) || double.IsNaN(lon)) continue;
                if (lat < -90.0 || lat > 90.0) continue;

                var (x, y) = SphericalMercator.FromLonLat(lon, lat);
                var dx = x - mapPoint.X;
                var dy = y - mapPoint.Y;
                var distSq = dx * dx + dy * dy;
                var distance = Math.Sqrt(distSq);

                // Try the vessel-hull polygon first when present —
                // matches the rendered shape so a click anywhere
                // inside the drawn hull picks the vessel even when
                // the antenna is far from the click. Distance reports
                // 0 inside the polygon so closer-to-antenna hits
                // still order ahead of edge hits.
                var insideHull = feature.VesselGeometry is { } geom
                    && IsInsideVesselHull(mapPoint, lat, lon, geom, feature.Motion?.HeadingDeg ?? 0.0);

                if (insideHull)
                {
                    hits.Add(new DynamicHit(source, feature, 0.0));
                    continue;
                }

                if (distSq <= toleranceSquared)
                {
                    hits.Add(new DynamicHit(source, feature, distance));
                }
            }
        }

        hits.Sort(static (a, b) => a.DistanceMapUnits.CompareTo(b.DistanceMapUnits));
        return hits;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="mapPoint"/>
    /// (Spherical Mercator) lies inside the vessel hull described by
    /// <paramref name="geometry"/> at antenna position
    /// (<paramref name="lat"/>, <paramref name="lon"/>) with heading
    /// <paramref name="headingDeg"/>. Mirrors the 5-vertex hull polygon
    /// produced by <c>VesselSymbology</c>.
    /// </summary>
    private static bool IsInsideVesselHull(
        MPoint mapPoint, double lat, double lon, DynamicVesselGeometry geometry, double headingDeg)
    {
        if (geometry.LengthMetres <= 0 || geometry.BeamMetres <= 0)
            return false;

        var theta = headingDeg * Math.PI / 180.0;
        var sinT = Math.Sin(theta);
        var cosT = Math.Cos(theta);

        var antX = -geometry.BeamMetres / 2.0 + geometry.PortOffsetMetres;
        var antY = geometry.LengthMetres - geometry.BowOffsetMetres;
        var halfBeam = geometry.BeamMetres / 2.0;
        const double bowTaperRatio = 0.7;
        var taperY = geometry.LengthMetres * bowTaperRatio;

        var local = new (double X, double Y)[]
        {
            (         0,  geometry.LengthMetres),
            (+halfBeam,   taperY),
            (+halfBeam,             0),
            (-halfBeam,             0),
            (-halfBeam,   taperY),
        };

        var ring = new (double X, double Y)[local.Length];
        const double metresPerDegLat = 111_320.0;
        var cosLat = Math.Cos(lat * Math.PI / 180.0);
        for (var i = 0; i < local.Length; i++)
        {
            var lx = local[i].X - antX;
            var ly = local[i].Y - antY;
            var east = lx * cosT + ly * sinT;
            var north = -lx * sinT + ly * cosT;
            var dLat = north / metresPerDegLat;
            var dLon = cosLat == 0 ? 0.0 : east / (metresPerDegLat * cosLat);
            var (mx, my) = SphericalMercator.FromLonLat(lon + dLon, lat + dLat);
            ring[i] = (mx, my);
        }

        return PointInPolygon(mapPoint.X, mapPoint.Y, ring);
    }

    private static bool PointInPolygon(double px, double py, (double X, double Y)[] ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var xi = ring[i].X; var yi = ring[i].Y;
            var xj = ring[j].X; var yj = ring[j].Y;
            var intersect = ((yi > py) != (yj > py)) &&
                            (px < (xj - xi) * (py - yi) / (yj - yi) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }
}

/// <summary>
/// Single hit returned by <see cref="DynamicSourceHitTester"/>. Carries
/// the owning source and the picked feature so the pick service can
/// resolve display metadata.
/// </summary>
internal sealed record DynamicHit(
    IDynamicFeatureSource Source,
    DynamicFeature Feature,
    double DistanceMapUnits);
