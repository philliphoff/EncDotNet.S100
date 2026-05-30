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
                if (distSq > toleranceSquared) continue;

                hits.Add(new DynamicHit(source, feature, Math.Sqrt(distSq)));
            }
        }

        hits.Sort(static (a, b) => a.DistanceMapUnits.CompareTo(b.DistanceMapUnits));
        return hits;
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
