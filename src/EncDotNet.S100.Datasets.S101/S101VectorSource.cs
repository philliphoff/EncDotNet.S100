using System.Runtime.CompilerServices;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Spatial;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Adapts an <see cref="S101Dataset"/> to the pipeline's <see cref="IVectorSource"/>
/// interface, projecting S-101 feature records into the generic feature model.
/// </summary>
public sealed class S101VectorSource : IVectorSource, IVectorSourceWithIndex
{
    private const byte RcnmPoint = 110;
    private const byte RcnmMultiPoint = 115;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;
    private const byte OrientationReverse = 2;
    private const byte UsageExterior = 1;

    private const string ProductTag = "S-101";

    /// <summary>
    /// Cache of per-dataset feature materialisation + spatial index,
    /// keyed by <see cref="S101Dataset"/>. Weak-keyed so a dataset
    /// eligible for GC takes its <see cref="FeatureCache"/> with it —
    /// no artificial lifetime extension for the cache to leak.
    /// </summary>
    /// <remarks>
    /// This lets the identify path
    /// (<c>FeatureAccessor.GetFeatures(dataset) → new S101VectorSource(dataset)</c>)
    /// pay the geometry-resolution + index-build cost exactly once per
    /// dataset instance, even though each call constructs a fresh
    /// <see cref="S101VectorSource"/>. See issue #490.
    /// </remarks>
    private static readonly ConditionalWeakTable<S101Dataset, FeatureCache> FeatureCaches = new();

    private readonly S101Dataset _dataset;
    private readonly FeatureCache _cache;

    public S101VectorSource(S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
        _cache = FeatureCaches.GetValue(dataset, static ds => new FeatureCache(ds));
    }

    public VectorMetadata Metadata => new()
    {
        Spec = BuildSpec(_dataset.Document),
        Extent = ComputeExtent(),
        HorizontalCRS = "EPSG:4326",
        CompilationScaleDenominator = 0, // S-101 doesn't encode scale in DSSI the same way as S-57
    };

    /// <inheritdoc />
    public IVectorSpatialIndex Index => _cache.Index;

    private static SpecRef BuildSpec(S101Document doc)
    {
        var edition = doc.Identification?.ProductSpecificationEdition;
        if (!string.IsNullOrWhiteSpace(edition)
            && SpecVersion.TryParse(edition, out var v))
        {
            return new SpecRef("S-101", v);
        }
        return new SpecRef("S-101", default);
    }

    /// <summary>
    /// Returns all features (when <paramref name="extent"/> is
    /// <see langword="null"/>) or every feature whose geometry MBR
    /// overlaps <paramref name="extent"/>. Extent queries are answered
    /// from the lazily-built spatial index (see <see cref="Index"/>);
    /// the whole-dataset case returns the cached feature list without
    /// touching the tree.
    /// </summary>
    public IReadOnlyList<Feature> GetFeatures(BoundingBox? extent = null)
    {
        return extent is null
            ? _cache.Features
            : _cache.Index.Query(extent);
    }

    /// <summary>
    /// Per-dataset cache of the materialised feature list and its
    /// spatial index. Both are built lazily and only once per
    /// <see cref="S101Dataset"/> instance; concurrent callers race
    /// once and then share the result via <see cref="Lazy{T}"/>.
    /// </summary>
    private sealed class FeatureCache
    {
        private readonly Lazy<IReadOnlyList<Feature>> _features;
        private readonly Lazy<IVectorSpatialIndex> _index;

        public FeatureCache(S101Dataset dataset)
        {
            _features = new Lazy<IReadOnlyList<Feature>>(
                () => MaterialiseFeatures(dataset),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _index = new Lazy<IVectorSpatialIndex>(
                () => IVectorSpatialIndex.Build(Features, ProductTag),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public IReadOnlyList<Feature> Features => _features.Value;
        public IVectorSpatialIndex Index => _index.Value;
    }

    private static IReadOnlyList<Feature> MaterialiseFeatures(S101Dataset dataset)
    {
        var doc = dataset.Document;
        var features = new List<Feature>(doc.Features.Count);

        foreach (var feat in doc.Features)
        {
            var featureType = doc.FeatureTypeCatalogue.TryGetValue(feat.FeatureTypeCode, out var name)
                ? name : feat.FeatureTypeCode.ToString();

            // Determine geometry type and resolve coordinates from spatial associations
            var (geomType, coords) = ResolveSpatialGeometry(feat, doc);
            if (coords.Count == 0) continue;

            var interiorRings = geomType == GeometryType.Surface
                ? ResolveSurfaceInteriorRings(feat, doc)
                : [];

            features.Add(new Feature
            {
                Id = (int)feat.RecordId,
                FeatureType = featureType,
                GeometryType = geomType,
                Coordinates = coords,
                InteriorRings = interiorRings,
                Attributes = ExtractAttributes(feat, doc),
            });
        }

        return features;
    }

    // ── Geometry resolution ────────────────────────────────────────────

    private static (GeometryType, IReadOnlyList<GeoPosition>) ResolveSpatialGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        if (feature.SpatialAssociations.Count == 0)
            return (GeometryType.Point, []);

        var first = feature.SpatialAssociations[0];

        return first.RecordName switch
        {
            RcnmPoint => (GeometryType.Point, ResolvePointGeometry(feature, doc)),
            RcnmMultiPoint => (GeometryType.Point, ResolveMultiPointGeometry(feature, doc)),
            RcnmCurveSegment => (GeometryType.Curve, ResolveCurveGeometry(feature, doc)),
            RcnmCompositeCurve => (GeometryType.Curve, ResolveCurveGeometry(feature, doc)),
            RcnmSurface => (GeometryType.Surface, ResolveSurfaceGeometry(feature, doc)),
            _ => (GeometryType.Point, []),
        };
    }

    private static IReadOnlyList<GeoPosition> ResolvePointGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        var results = new List<GeoPosition>();
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        // Defensive divide-by-zero guards only; valid datasets supply COMF (typically
        // 1e7) in the DSSI record. See HostGetSpatialData in S101LuaDataProvider.
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        foreach (var spa in feature.SpatialAssociations)
        {
            if (spa.RecordName == RcnmPoint && doc.Points.TryGetValue(spa.RecordId, out var pt))
            {
                results.Add(new GeoPosition(pt.Y / cmfy, pt.X / cmfx));
            }
        }

        return results;
    }

    private static IReadOnlyList<GeoPosition> ResolveMultiPointGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        var results = new List<GeoPosition>();
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        // Defensive divide-by-zero guards only; valid datasets supply COMF (typically
        // 1e7) in the DSSI record. See HostGetSpatialData in S101LuaDataProvider.
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        foreach (var spa in feature.SpatialAssociations)
        {
            if (spa.RecordName != RcnmMultiPoint) continue;
            if (!doc.MultiPoints.TryGetValue(spa.RecordId, out var mp)) continue;

            foreach (var (y, x, _) in mp.Points)
            {
                results.Add(new GeoPosition(y / cmfy, x / cmfx));
            }
        }

        return results;
    }

    private static IReadOnlyList<GeoPosition> ResolveCurveGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        var coords = new List<GeoPosition>();

        foreach (var spa in feature.SpatialAssociations)
        {
            ResolveCurveCoords(spa.RecordName, spa.RecordId, spa.Orientation, doc, coords);
        }

        return coords;
    }

    private static IReadOnlyList<GeoPosition> ResolveSurfaceGeometry(
        S101FeatureRecord feature, S101Document doc)
    {
        // Flatten exterior ring curves into a coordinate list.
        var coords = new List<GeoPosition>();

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

    private static IReadOnlyList<IReadOnlyList<GeoPosition>> ResolveSurfaceInteriorRings(
        S101FeatureRecord feature, S101Document doc)
    {
        // S-100 Part 10a surface topology: each RIAS association with USAG = 2
        // (interior) bounds one hole. Resolve each independently into its own
        // closed ring so renderers can subtract it from the exterior fill
        // (e.g. a sea/depth area encoded around islands cut out as holes).
        List<IReadOnlyList<GeoPosition>>? rings = null;

        foreach (var spa in feature.SpatialAssociations)
        {
            if (spa.RecordName != RcnmSurface) continue;
            if (!doc.Surfaces.TryGetValue(spa.RecordId, out var surface)) continue;

            foreach (var ring in surface.RingAssociations)
            {
                if (ring.Usage == UsageExterior) continue;

                var ringCoords = new List<GeoPosition>();
                ResolveCurveCoords(ring.RecordName, ring.RecordId, ring.Orientation, doc, ringCoords);
                if (ringCoords.Count >= 3)
                {
                    rings ??= [];
                    rings.Add(ringCoords);
                }
            }
        }

        return rings ?? (IReadOnlyList<IReadOnlyList<GeoPosition>>)[];
    }

    private static void ResolveCurveCoords(
        byte rcnm, uint rcid, byte orientation, S101Document doc, List<GeoPosition> coords)
    {
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        // Defensive divide-by-zero guards only; valid datasets supply COMF (typically
        // 1e7) in the DSSI record. See HostGetSpatialData in S101LuaDataProvider.
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        if (rcnm == RcnmCurveSegment && doc.CurveSegments.TryGetValue(rcid, out var segment))
        {
            var segCoords = new List<GeoPosition>();

            // Start point
            foreach (var pta in segment.PointAssociations)
            {
                if (pta.Topology == 1 && doc.Points.TryGetValue(pta.RecordId, out var startPt)) // TOPI=1 begin
                    segCoords.Add(new GeoPosition(startPt.Y / cmfy, startPt.X / cmfx));
            }

            // Intermediate points
            foreach (var (y, x) in segment.IntermediateCoordinates)
            {
                segCoords.Add(new GeoPosition(y / cmfy, x / cmfx));
            }

            // End point
            foreach (var pta in segment.PointAssociations)
            {
                if (pta.Topology == 2 && doc.Points.TryGetValue(pta.RecordId, out var endPt)) // TOPI=2 end
                    segCoords.Add(new GeoPosition(endPt.Y / cmfy, endPt.X / cmfx));
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

        foreach (var mp in doc.MultiPoints.Values)
        {
            foreach (var (y, x, _) in mp.Points)
            {
                UpdateBounds(y / cmfy, x / cmfx);
            }
        }

        // Most S-101 cells store their extent in curve geometry, not isolated
        // point records: a cell built entirely from curves/surfaces has no
        // Point/MultiPoint records, so the boundary vertices live in each
        // curve segment's intermediate coordinates. Fold those in too, or the
        // extent collapses to (0,0,0,0) and callers fall back to world bounds.
        foreach (var seg in doc.CurveSegments.Values)
        {
            foreach (var (y, x) in seg.IntermediateCoordinates)
            {
                UpdateBounds(y / cmfy, x / cmfx);
            }
        }

        if (!hasCoords)
        {
            return new BoundingBox(0, 0, 0, 0);
        }

        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }
}
