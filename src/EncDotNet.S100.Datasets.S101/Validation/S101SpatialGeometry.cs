using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.S101.Validation;

/// <summary>
/// Vertex walking over the spatial records of an <see cref="S101Document"/>,
/// shared by the geometry rules of <see cref="S101DatasetRules"/> and by the
/// finding locator of <see cref="S101DatasetView"/>.
/// </summary>
/// <remarks>
/// Curve orientation follows S-100 Part 10a §4.3.3: a curve segment runs from
/// its begin node (topology 1) through its intermediate coordinates to its end
/// node (topology 2); a composite curve component, a ring association, or a
/// spatial association with orientation 2 traverses its target in reverse.
/// </remarks>
internal static class S101SpatialGeometry
{
    private const byte RcnmPoint = 110;
    private const byte RcnmMultiPoint = 115;
    private const byte RcnmCurve = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;
    private const byte OrientationReverse = 2;

    // Composite curves may nest; a bound keeps a malformed self-reference
    // from recursing without end.
    private const int MaxCompositeDepth = 32;

    /// <summary>
    /// Appends the vertices of the curve (RCNM 120) or composite curve
    /// (RCNM 125) <paramref name="recordId"/> to <paramref name="sink"/>, in
    /// traversal order, honouring each composite component's orientation and
    /// reversing the whole path when <paramref name="reverse"/> is set.
    /// </summary>
    /// <returns><c>false</c> when a referenced record is missing.</returns>
    internal static bool AppendCurvePath(
        S101Document document, byte recordName, uint recordId, bool reverse, List<(int Y, int X)> sink)
        => AppendCurvePath(document, recordName, recordId, reverse, sink, 0);

    private static bool AppendCurvePath(
        S101Document document, byte recordName, uint recordId, bool reverse, List<(int Y, int X)> sink, int depth)
    {
        if (recordName == RcnmCurve)
        {
            if (!document.CurveSegments.TryGetValue(recordId, out var segment)) return false;
            var start = sink.Count;
            (int Y, int X)? begin = null, end = null;
            foreach (var pa in segment.PointAssociations)
            {
                if (!document.Points.TryGetValue(pa.RecordId, out var p)) continue;
                if (pa.Topology == 1) begin = (p.Y, p.X);
                else if (pa.Topology == 2) end = (p.Y, p.X);
            }
            if (begin is { } b) sink.Add(b);
            foreach (var ic in segment.IntermediateCoordinates) sink.Add((ic.Y, ic.X));
            if (end is { } e) sink.Add(e);
            if (reverse) sink.Reverse(start, sink.Count - start);
            return true;
        }

        if (recordName == RcnmCompositeCurve && depth < MaxCompositeDepth)
        {
            if (!document.CompositeCurves.TryGetValue(recordId, out var composite)) return false;
            var components = composite.CurveComponents;
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[reverse ? components.Count - 1 - i : i];
                var componentReverse = (component.Orientation == OrientationReverse) != reverse;
                if (!AppendCurvePath(document, component.RecordName, component.RecordId, componentReverse, sink, depth + 1))
                    return false;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Appends every vertex of the spatial record a spatial association
    /// references — the point, the points of a multi-point, the vertices of a
    /// curve or composite curve, or the vertices of every ring of a surface.
    /// Missing records contribute nothing.
    /// </summary>
    internal static void AppendVertices(S101Document document, byte recordName, uint recordId, List<(int Y, int X)> sink)
    {
        switch (recordName)
        {
            case RcnmPoint:
                if (document.Points.TryGetValue(recordId, out var p)) sink.Add((p.Y, p.X));
                break;
            case RcnmMultiPoint:
                if (document.MultiPoints.TryGetValue(recordId, out var mp))
                {
                    foreach (var pt in mp.Points) sink.Add((pt.Y, pt.X));
                }
                break;
            case RcnmCurve:
            case RcnmCompositeCurve:
                AppendCurvePath(document, recordName, recordId, reverse: false, sink);
                break;
            case RcnmSurface:
                if (document.Surfaces.TryGetValue(recordId, out var surface))
                {
                    foreach (var ring in surface.RingAssociations)
                        AppendCurvePath(document, ring.RecordName, ring.RecordId, reverse: false, sink);
                }
                break;
        }
    }

    /// <summary>
    /// The dataset's coordinate multiplication factors, defaulting to
    /// 10<sup>7</sup> when the DSSI value is zero (S-100 Part 10a §4.3.1).
    /// </summary>
    internal static (uint CmfX, uint CmfY) EffectiveCmf(S101DatasetStructureInfo si)
    {
        var cmfX = si.CoordinateMultiplicationFactorX == 0 ? 10_000_000u : si.CoordinateMultiplicationFactorX;
        var cmfY = si.CoordinateMultiplicationFactorY == 0 ? 10_000_000u : si.CoordinateMultiplicationFactorY;
        return (cmfX, cmfY);
    }

    /// <summary>Converts an encoded (Y, X) vertex to a WGS-84 position.</summary>
    internal static GeoPosition ToGeoPosition((int Y, int X) vertex, (uint CmfX, uint CmfY) cmf)
        => new((double)vertex.Y / cmf.CmfY, (double)vertex.X / cmf.CmfX);

    /// <summary>
    /// The WGS-84 envelope of <paramref name="vertices"/>, or <c>null</c> when
    /// the list is empty.
    /// </summary>
    internal static BoundingBox? Envelope(List<(int Y, int X)> vertices, (uint CmfX, uint CmfY) cmf)
    {
        if (vertices.Count == 0) return null;
        int minY = int.MaxValue, minX = int.MaxValue, maxY = int.MinValue, maxX = int.MinValue;
        foreach (var (y, x) in vertices)
        {
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
        }
        return new BoundingBox(
            (double)minY / cmf.CmfY,
            (double)minX / cmf.CmfX,
            (double)maxY / cmf.CmfY,
            (double)maxX / cmf.CmfX);
    }
}
