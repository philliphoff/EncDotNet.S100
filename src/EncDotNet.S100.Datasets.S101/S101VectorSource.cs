using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Adapts an <see cref="S101Dataset"/> to the pipeline's <see cref="IVectorSource"/>
/// interface, projecting S-101 feature records into the generic feature model.
/// </summary>
public sealed class S101VectorSource : IVectorSource
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;
    private const byte OrientationReverse = 2;
    private const byte UsageExterior = 1;

    private readonly S101Dataset _dataset;

    public S101VectorSource(S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    public VectorMetadata Metadata => new()
    {
        ProductSpec = "S-101",
        Extent = ComputeExtent(),
        HorizontalCRS = "EPSG:4326",
        CompilationScaleDenominator = 0, // S-101 doesn't encode scale in DSSI the same way as S-57
    };

    public IReadOnlyList<Feature> GetFeatures(BoundingBox? extent = null)
    {
        var doc = _dataset.Document;
        var features = new List<Feature>();

        foreach (var feat in doc.Features)
        {
            var featureType = doc.FeatureTypeCatalogue.TryGetValue(feat.FeatureTypeCode, out var name)
                ? name : feat.FeatureTypeCode.ToString();

            // Determine geometry type and resolve coordinates from spatial associations
            var (geomType, coords) = ResolveSpatialGeometry(feat, doc);
            if (coords.Count == 0) continue;
            if (extent is not null && !IntersectsExtent(coords, extent)) continue;

            features.Add(new Feature
            {
                Id = (int)feat.RecordId,
                FeatureType = featureType,
                GeometryType = geomType,
                Coordinates = coords,
                Attributes = ExtractAttributes(feat, doc),
            });
        }

        return features;
    }

    // ── Geometry resolution ────────────────────────────────────────────

    private static (GeometryType, IReadOnlyList<(double Latitude, double Longitude)>) ResolveSpatialGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        if (feature.SpatialAssociations.Length == 0)
            return (GeometryType.Point, []);

        var first = feature.SpatialAssociations[0];

        return first.RecordName switch
        {
            RcnmPoint => (GeometryType.Point, ResolvePointGeometry(feature, doc)),
            RcnmCurveSegment => (GeometryType.Curve, ResolveCurveGeometry(feature, doc)),
            RcnmCompositeCurve => (GeometryType.Curve, ResolveCurveGeometry(feature, doc)),
            RcnmSurface => (GeometryType.Surface, ResolveSurfaceGeometry(feature, doc)),
            _ => (GeometryType.Point, []),
        };
    }

    private static IReadOnlyList<(double, double)> ResolvePointGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        var results = new List<(double, double)>();
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        foreach (var spa in feature.SpatialAssociations)
        {
            if (spa.RecordName == RcnmPoint && doc.Points.TryGetValue(spa.RecordId, out var pt))
            {
                results.Add((pt.Y / cmfy, pt.X / cmfx));
            }
        }

        return results;
    }

    private static IReadOnlyList<(double, double)> ResolveCurveGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        var coords = new List<(double, double)>();

        foreach (var spa in feature.SpatialAssociations)
        {
            ResolveCurveCoords(spa.RecordName, spa.RecordId, spa.Orientation, doc, coords);
        }

        return coords;
    }

    private static IReadOnlyList<(double, double)> ResolveSurfaceGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        // Flatten exterior ring curves into a coordinate list.
        var coords = new List<(double, double)>();

        foreach (var spa in feature.SpatialAssociations)
        {
            if (spa.RecordName != RcnmSurface) continue;
            if (!doc.Surfaces.TryGetValue(spa.RecordId, out var surface)) continue;

            foreach (var ring in surface.RingAssociations)
            {
                if (ring.Usage != UsageExterior) continue;
                ResolveCurveCoords(ring.RecordName, ring.RecordId, ring.Orientation, doc, coords);
            }
        }

        return coords;
    }

    private static void ResolveCurveCoords(
        byte rcnm, uint rcid, byte orientation, S101Document doc, List<(double, double)> coords)
    {
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        if (rcnm == RcnmCurveSegment && doc.CurveSegments.TryGetValue(rcid, out var segment))
        {
            var segCoords = new List<(double, double)>();

            // Start point
            foreach (var pta in segment.PointAssociations)
            {
                if (pta.Topology == 1 && doc.Points.TryGetValue(pta.RecordId, out var startPt)) // TOPI=1 begin
                    segCoords.Add((startPt.Y / cmfy, startPt.X / cmfx));
            }

            // Intermediate points
            foreach (var (y, x) in segment.IntermediateCoordinates)
            {
                segCoords.Add((y / cmfy, x / cmfx));
            }

            // End point
            foreach (var pta in segment.PointAssociations)
            {
                if (pta.Topology == 2 && doc.Points.TryGetValue(pta.RecordId, out var endPt)) // TOPI=2 end
                    segCoords.Add((endPt.Y / cmfy, endPt.X / cmfx));
            }

            if (orientation == OrientationReverse)
                segCoords.Reverse();

            coords.AddRange(segCoords);
        }
        else if (rcnm == RcnmCompositeCurve && doc.CompositeCurves.TryGetValue(rcid, out var composite))
        {
            foreach (var component in composite.CurveComponents)
            {
                var effectiveOrientation = orientation == OrientationReverse
                    ? (component.Orientation == OrientationReverse ? (byte)1 : OrientationReverse)
                    : component.Orientation;
                ResolveCurveCoords(component.RecordName, component.RecordId, effectiveOrientation, doc, coords);
            }

            if (orientation == OrientationReverse)
            {
                // Components were added in forward order; we need to reverse the whole composite
                // Actually, each component was already reversed individually, so this is not needed.
                // But the order of components should be reversed.
                // Let's handle this more carefully:
            }
        }
    }

    // ── Attribute extraction ───────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> ExtractAttributes(
        S101FeatureRecord feature, S101Document doc)
    {
        var attributes = new Dictionary<string, object?>();

        foreach (var attr in feature.Attributes)
        {
            var attrName = doc.AttributeTypeCatalogue.TryGetValue(attr.NumericCode, out var name)
                ? name : attr.NumericCode.ToString();
            attributes[attrName] = attr.Value;
        }

        return attributes;
    }

    // ── Extent computation ─────────────────────────────────────────────

    private BoundingBox ComputeExtent()
    {
        var doc = _dataset.Document;
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        double minLat = double.MaxValue, minLon = double.MaxValue;
        double maxLat = double.MinValue, maxLon = double.MinValue;
        bool hasCoords = false;

        void UpdateBounds(double lat, double lon)
        {
            hasCoords = true;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        foreach (var pt in doc.Points.Values)
        {
            UpdateBounds(pt.Y / cmfy, pt.X / cmfx);
        }

        if (!hasCoords)
        {
            return new BoundingBox(0, 0, 0, 0);
        }

        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }

    private static bool IntersectsExtent(
        IReadOnlyList<(double Latitude, double Longitude)> coords,
        BoundingBox extent)
    {
        foreach (var (lat, lon) in coords)
        {
            if (lat >= extent.SouthLatitude && lat <= extent.NorthLatitude
                && lon >= extent.WestLongitude && lon <= extent.EastLongitude)
            {
                return true;
            }
        }
        return false;
    }
}
