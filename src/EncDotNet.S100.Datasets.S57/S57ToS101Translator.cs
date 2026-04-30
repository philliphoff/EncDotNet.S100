using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Translates a parsed <see cref="S57Document"/> into the S-101 in-memory
/// document model so that the existing S-101 portrayal pipeline can drive
/// rendering of S-57 ENC data.
/// </summary>
/// <remarks>
/// <para>
/// The translation is intentionally lossy and breadth-first:
/// <list type="bullet">
///   <item>S-57 object/attribute numeric codes are remapped to S-101 Feature
///   Catalogue acronyms via <see cref="S57S101Mapping"/>. Unmapped feature
///   classes are skipped.</item>
///   <item>S-57 isolated / connected nodes become S-101 Point records.</item>
///   <item>S-57 edges become S-101 Curve Segment records with begin / end
///   point associations and intermediate coordinates.</item>
///   <item>S-57 area features have their FSPT edge ring wrapped into a
///   single composite curve and referenced from a synthesised S-101 Surface
///   record.</item>
///   <item>S-57 multi-point soundings (<c>SOUNDG</c>) are exploded so each
///   <c>(Y, X, Z)</c> triple becomes a separate S-101 Sounding feature with
///   a single point geometry and a <c>valueOfSounding</c> attribute.</item>
///   <item>Listed-value remap (S-57 enum codes to S-101 enum codes) is not
///   yet performed; string attribute values pass through unchanged.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class S57ToS101Translator
{
    private const byte S101RcnmPoint = 110;
    private const byte S101RcnmCurveSegment = 120;
    private const byte S101RcnmCompositeCurve = 125;
    private const byte S101RcnmSurface = 130;

    private const byte OrientationForward = 1;
    private const byte OrientationReverse = 2;
    private const byte UsageExterior = 1;
    private const byte UsageInterior = 2;
    private const byte TopologyBegin = 1;
    private const byte TopologyEnd = 2;

    private const ushort SoundingObjl = 129;
    private const string SoundingS101Code = "Sounding";
    private const string SoundingValueAttribute = "valueOfSounding";

    private readonly S57S101Mapping _mapping;

    /// <summary>Creates a translator using <see cref="S57S101Mapping.Default"/>.</summary>
    public S57ToS101Translator() : this(S57S101Mapping.Default) { }

    /// <summary>Creates a translator using the supplied code mapping.</summary>
    public S57ToS101Translator(S57S101Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        _mapping = mapping;
    }

    /// <summary>
    /// Translates an <see cref="S57Document"/> into an <see cref="S101Document"/>.
    /// </summary>
    public S101Document Translate(S57Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Translate(dataset.Document);
    }

    /// <summary>
    /// Translates an <see cref="S57Document"/> into an <see cref="S101Document"/>.
    /// </summary>
    public S101Document Translate(S57Document s57)
    {
        ArgumentNullException.ThrowIfNull(s57);

        var ctx = new TranslationContext(s57, _mapping);
        ctx.TranslateNodes();
        ctx.TranslateEdges();
        ctx.TranslateFeatures();

        var cmf = s57.Parameters.CoordinateMultiplicationFactor;
        if (cmf == 0) cmf = 10_000_000;

        return new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                RecordName = 10,
                RecordId = 1,
                ProductSpecification = "S-101",
                ProductSpecificationEdition = "1.0.0",
                DatasetName = s57.Identification.DatasetName,
                DatasetTitle = s57.Identification.DatasetName,
                DatasetReferenceDate = s57.Identification.IssueDate,
                DatasetLanguage = "eng",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = cmf,
                CoordinateMultiplicationFactorY = cmf,
                CoordinateMultiplicationFactorZ = s57.Parameters.SoundingMultiplicationFactor,
            },
            FeatureTypeCatalogue = ctx.FeatureTypeCatalogue.ToImmutable(),
            AttributeTypeCatalogue = ctx.AttributeTypeCatalogue.ToImmutable(),
            Points = ctx.Points.ToImmutable(),
            CurveSegments = ctx.CurveSegments.ToImmutable(),
            CompositeCurves = ctx.CompositeCurves.ToImmutable(),
            Surfaces = ctx.Surfaces.ToImmutable(),
            Features = ctx.Features.ToImmutable(),
            InformationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };
    }

    // ── Translation context ─────────────────────────────────────────────

    private sealed class TranslationContext
    {
        private readonly S57Document _s57;
        private readonly S57S101Mapping _mapping;

        // Mapping from S-57 (RCNM, RCID) to allocated S-101 IDs, per spatial kind.
        private readonly Dictionary<S57Name, uint> _nodeIdMap = new();
        private readonly Dictionary<uint, uint> _edgeIdMap = new();
        private uint _nextPointId = 1;
        private uint _nextCurveId = 1;
        private uint _nextCompositeId = 1;
        private uint _nextSurfaceId = 1;
        private uint _nextFeatureId = 1;
        private ushort _nextFeatureTypeCode = 1;
        private ushort _nextAttributeCode = 1;
        private readonly Dictionary<string, ushort> _featureTypeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _attributeByName = new(StringComparer.OrdinalIgnoreCase);

        public ImmutableDictionary<uint, S101PointRecord>.Builder Points { get; }
            = ImmutableDictionary.CreateBuilder<uint, S101PointRecord>();
        public ImmutableDictionary<uint, S101CurveSegmentRecord>.Builder CurveSegments { get; }
            = ImmutableDictionary.CreateBuilder<uint, S101CurveSegmentRecord>();
        public ImmutableDictionary<uint, S101CompositeCurveRecord>.Builder CompositeCurves { get; }
            = ImmutableDictionary.CreateBuilder<uint, S101CompositeCurveRecord>();
        public ImmutableDictionary<uint, S101SurfaceRecord>.Builder Surfaces { get; }
            = ImmutableDictionary.CreateBuilder<uint, S101SurfaceRecord>();
        public ImmutableArray<S101FeatureRecord>.Builder Features { get; }
            = ImmutableArray.CreateBuilder<S101FeatureRecord>();
        public ImmutableDictionary<ushort, string>.Builder FeatureTypeCatalogue { get; }
            = ImmutableDictionary.CreateBuilder<ushort, string>();
        public ImmutableDictionary<ushort, string>.Builder AttributeTypeCatalogue { get; }
            = ImmutableDictionary.CreateBuilder<ushort, string>();

        public TranslationContext(S57Document s57, S57S101Mapping mapping)
        {
            _s57 = s57;
            _mapping = mapping;
        }

        // ── Spatial translation ─────────────────────────────────────────

        public void TranslateNodes()
        {
            foreach (var (key, vr) in _s57.VectorRecords)
            {
                if (vr.RecordName != S57DocumentReader.RcnmIsolatedNode
                    && vr.RecordName != S57DocumentReader.RcnmConnectedNode)
                    continue;

                // Skip multi-point sounding nodes here; they're exploded into
                // features in TranslateFeatures().
                if (vr.Coordinates3D.Length > 0) continue;
                if (vr.Coordinates2D.Length == 0) continue;

                var (y, x) = vr.Coordinates2D[0];
                var id = _nextPointId++;
                Points[id] = new S101PointRecord { RecordId = id, Y = y, X = x };
                _nodeIdMap[key] = id;
            }
        }

        public void TranslateEdges()
        {
            foreach (var (key, vr) in _s57.VectorRecords)
            {
                if (vr.RecordName != S57DocumentReader.RcnmEdge) continue;

                S101PointAssociation? begin = null;
                S101PointAssociation? end = null;
                foreach (var p in vr.Pointers)
                {
                    if (p.Topology == TopologyBegin)
                    {
                        if (TryGetPointId(p.RecordName, p.RecordId, out var pid))
                            begin = new S101PointAssociation(S101RcnmPoint, pid, TopologyBegin);
                    }
                    else if (p.Topology == TopologyEnd)
                    {
                        if (TryGetPointId(p.RecordName, p.RecordId, out var pid))
                            end = new S101PointAssociation(S101RcnmPoint, pid, TopologyEnd);
                    }
                }

                var ptas = ImmutableArray.CreateBuilder<S101PointAssociation>();
                if (begin is not null) ptas.Add(begin.Value);
                if (end is not null) ptas.Add(end.Value);

                var id = _nextCurveId++;
                CurveSegments[id] = new S101CurveSegmentRecord
                {
                    RecordId = id,
                    PointAssociations = ptas.ToImmutable(),
                    IntermediateCoordinates = vr.Coordinates2D,
                };
                _edgeIdMap[vr.RecordId] = id;
            }
        }

        private bool TryGetPointId(byte rcnm, uint rcid, out uint id)
            => _nodeIdMap.TryGetValue(new S57Name(rcnm, rcid), out id);

        // ── Feature translation ─────────────────────────────────────────

        public void TranslateFeatures()
        {
            foreach (var feat in _s57.Features)
            {
                if (feat.ObjectClass == SoundingObjl)
                {
                    // S-101 Sounding portrayal expects a PointSet (multi-point)
                    // geometry; exploding S-57 SG3D triples into individual
                    // single-point Sounding features fails the Lua primitive-type
                    // check and produces thousands of default-symbology fallbacks
                    // that dominate render time. Skip soundings until proper
                    // PointSet support is added.
                    continue;
                }

                var s101Code = _mapping.ResolveFeatureCode(feat.ObjectClass);
                if (s101Code is null) continue;

                var typeCode = GetOrAssignFeatureTypeCode(s101Code);
                var attributes = TranslateAttributes(feat.Attributes);
                var spatials = TranslateSpatialPointers(feat);
                if (spatials.Length == 0) continue;

                Features.Add(new S101FeatureRecord
                {
                    RecordId = _nextFeatureId++,
                    FeatureTypeCode = typeCode,
                    ProducingAgency = feat.ProducingAgency,
                    FeatureIdentificationNumber = feat.FeatureIdentificationNumber,
                    FeatureIdentificationSubdivision = feat.FeatureIdentificationSubdivision,
                    Attributes = attributes,
                    SpatialAssociations = spatials,
                    FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
                    InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
                });
            }
        }

        private void ExplodeSounding(S57FeatureRecord feat)
        {
            var typeCode = GetOrAssignFeatureTypeCode(SoundingS101Code);
            var attrCode = GetOrAssignAttributeCode(SoundingValueAttribute);
            var somf = _s57.Parameters.SoundingMultiplicationFactor;
            if (somf == 0) somf = 10;

            // Each sounding feature in S-57 references one or more vector
            // records carrying SG3D triples. Explode every triple into its
            // own S-101 Sounding feature.
            foreach (var ptr in feat.SpatialPointers)
            {
                if (!_s57.VectorRecords.TryGetValue(new S57Name(ptr.RecordName, ptr.RecordId), out var vr))
                    continue;

                foreach (var (y, x, z) in vr.Coordinates3D)
                {
                    var pid = _nextPointId++;
                    Points[pid] = new S101PointRecord { RecordId = pid, Y = y, X = x };

                    var depth = (z / (double)somf).ToString("0.###", CultureInfo.InvariantCulture);
                    Features.Add(new S101FeatureRecord
                    {
                        RecordId = _nextFeatureId++,
                        FeatureTypeCode = typeCode,
                        ProducingAgency = feat.ProducingAgency,
                        FeatureIdentificationNumber = feat.FeatureIdentificationNumber,
                        FeatureIdentificationSubdivision = feat.FeatureIdentificationSubdivision,
                        Attributes = ImmutableArray.Create(new S101Attribute(attrCode, 1, depth)),
                        SpatialAssociations = ImmutableArray.Create(
                            new S101SpatialAssociation(S101RcnmPoint, pid, OrientationForward)),
                        FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
                        InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
                    });
                }
            }
        }

        private ImmutableArray<S101Attribute> TranslateAttributes(ImmutableArray<S57Attribute> attrs)
        {
            if (attrs.Length == 0) return ImmutableArray<S101Attribute>.Empty;

            var builder = ImmutableArray.CreateBuilder<S101Attribute>();
            foreach (var a in attrs)
            {
                var s101Code = _mapping.ResolveAttributeCode(a.Code);
                if (s101Code is null) continue;
                var numeric = GetOrAssignAttributeCode(s101Code);
                builder.Add(new S101Attribute(numeric, 1, a.Value));
            }
            return builder.ToImmutable();
        }

        private ImmutableArray<S101SpatialAssociation> TranslateSpatialPointers(S57FeatureRecord feat)
        {
            switch (feat.Primitive)
            {
                case 1: // Point
                    return TranslatePointSpatial(feat);
                case 2: // Line
                    return TranslateLineSpatial(feat);
                case 3: // Area
                    return TranslateAreaSpatial(feat);
                default:
                    return ImmutableArray<S101SpatialAssociation>.Empty;
            }
        }

        private ImmutableArray<S101SpatialAssociation> TranslatePointSpatial(S57FeatureRecord feat)
        {
            // Point features reference a single isolated/connected node.
            foreach (var ptr in feat.SpatialPointers)
            {
                if (TryGetPointId(ptr.RecordName, ptr.RecordId, out var pid))
                {
                    return ImmutableArray.Create(
                        new S101SpatialAssociation(S101RcnmPoint, pid, OrientationForward));
                }
            }
            return ImmutableArray<S101SpatialAssociation>.Empty;
        }

        private ImmutableArray<S101SpatialAssociation> TranslateLineSpatial(S57FeatureRecord feat)
        {
            // Line features reference one or more edges in traversal order.
            var builder = ImmutableArray.CreateBuilder<S101SpatialAssociation>();
            foreach (var ptr in feat.SpatialPointers)
            {
                if (ptr.RecordName != S57DocumentReader.RcnmEdge) continue;
                if (!_edgeIdMap.TryGetValue(ptr.RecordId, out var cid)) continue;
                var ornt = ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                builder.Add(new S101SpatialAssociation(S101RcnmCurveSegment, cid, ornt));
            }
            return builder.ToImmutable();
        }

        private ImmutableArray<S101SpatialAssociation> TranslateAreaSpatial(S57FeatureRecord feat)
        {
            // Area features reference a ring of edges via FSPT. Group by
            // USAG (1 = exterior, 2 = interior) and wrap each group into a
            // composite curve referenced from a synthesised surface record.
            var exterior = ImmutableArray.CreateBuilder<S101CurveUsage>();
            var interiors = new List<List<S101CurveUsage>>();
            List<S101CurveUsage>? currentInterior = null;

            foreach (var ptr in feat.SpatialPointers)
            {
                if (ptr.RecordName != S57DocumentReader.RcnmEdge) continue;
                if (!_edgeIdMap.TryGetValue(ptr.RecordId, out var cid)) continue;
                var ornt = ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                var usage = new S101CurveUsage(S101RcnmCurveSegment, cid, ornt);

                switch (ptr.Usage)
                {
                    case UsageInterior:
                        currentInterior ??= new List<S101CurveUsage>();
                        currentInterior.Add(usage);
                        break;
                    case UsageExterior:
                    case 3: // exterior truncated
                    default:
                        if (currentInterior is not null)
                        {
                            interiors.Add(currentInterior);
                            currentInterior = null;
                        }
                        exterior.Add(usage);
                        break;
                }
            }
            if (currentInterior is not null) interiors.Add(currentInterior);
            if (exterior.Count == 0) return ImmutableArray<S101SpatialAssociation>.Empty;

            var rings = ImmutableArray.CreateBuilder<S101RingAssociation>();

            // Exterior ring as one composite curve.
            var extId = _nextCompositeId++;
            CompositeCurves[extId] = new S101CompositeCurveRecord
            {
                RecordId = extId,
                CurveComponents = exterior.ToImmutable(),
            };
            rings.Add(new S101RingAssociation(
                S101RcnmCompositeCurve, extId, OrientationForward, UsageExterior));

            // Interior rings each as their own composite curve.
            foreach (var interior in interiors)
            {
                var intId = _nextCompositeId++;
                CompositeCurves[intId] = new S101CompositeCurveRecord
                {
                    RecordId = intId,
                    CurveComponents = interior.ToImmutableArray(),
                };
                rings.Add(new S101RingAssociation(
                    S101RcnmCompositeCurve, intId, OrientationForward, UsageInterior));
            }

            var sid = _nextSurfaceId++;
            Surfaces[sid] = new S101SurfaceRecord
            {
                RecordId = sid,
                RingAssociations = rings.ToImmutable(),
            };

            return ImmutableArray.Create(
                new S101SpatialAssociation(S101RcnmSurface, sid, OrientationForward));
        }

        // ── Catalogue interning ─────────────────────────────────────────

        private ushort GetOrAssignFeatureTypeCode(string s101Code)
        {
            if (_featureTypeByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextFeatureTypeCode++;
            _featureTypeByName[s101Code] = code;
            FeatureTypeCatalogue[code] = s101Code;
            return code;
        }

        private ushort GetOrAssignAttributeCode(string s101Code)
        {
            if (_attributeByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextAttributeCode++;
            _attributeByName[s101Code] = code;
            AttributeTypeCatalogue[code] = s101Code;
            return code;
        }
    }
}
