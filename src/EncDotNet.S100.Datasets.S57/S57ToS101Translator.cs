using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Translates a parsed S-57 <see cref="EncDotNet.S57.S57Document"/> (from the
/// <c>EncDotNet.S57</c> NuGet package) into the S-101 in-memory document model
/// so that the existing S-101 portrayal pipeline can drive rendering of S-57
/// ENC data.
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
///   <item>S-57 multi-point soundings (<c>SOUNDG</c>) are translated into a single
///   S-101 <c>Sounding</c> feature backed by a multi-point spatial record
///   (RCNM = 115); the depth values live on the points within that record and
///   are read by the <c>SOUNDG03</c> portrayal rule as <c>point.ScaledZ</c>.</item>
///   <item>Listed-value remap (S-57 enum codes to S-101 enum codes) is not
///   yet performed; string attribute values pass through unchanged.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class S57ToS101Translator
{
    private const byte S101RcnmPoint = 110;
    private const byte S101RcnmMultiPoint = 115;
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

    // ── S-57 textual-info attribute codes (S-57 Appendix A Chapter 2) ──
    // Per IHO S-57→S-101 Conversion Guidance §2.3, these four attributes
    // do NOT pass through as simple S-101 attributes; instead they are
    // grouped into one or more instances of the S-101 `information`
    // complex attribute on the feature itself, with sub-attributes
    // text / fileReference / language as appropriate.
    private const ushort S57AttrInform = 102;   // INFORM  — free text (Eng.)
    private const ushort S57AttrTxtdsc = 158;   // TXTDSC  — text-file ref (Eng.)
    private const ushort S57AttrNinfom = 300;   // NINFOM  — free text (national)
    private const ushort S57AttrNtxtds = 304;   // NTXTDS  — text-file ref (national)

    // ── S-57 object-name attribute codes (S-57 Appendix A Chapter 2) ──
    // Per IHO S-57→S-101 Conversion Guidance, OBJNAM/NOBJNM do NOT pass
    // through as a simple attribute; they become one or more instances of
    // the S-101 `featureName` complex attribute (sub-attributes name /
    // language [/ nameUsage]). The FC declares OBJNAM and NOBJNM as the two
    // aliases of the `name` sub-attribute, mirroring the INFORM/NINFOM split.
    private const ushort S57AttrObjnam = 116;   // OBJNAM  — object name (Eng.)
    private const ushort S57AttrNobjnm = 301;   // NOBJNM  — object name (national)

    // S-101 attribute codes for the `information` complex attribute and
    // its sub-attributes (verified against the bundled FC).
    private const string S101AttrInformation = "information";
    private const string S101AttrText = "text";
    private const string S101AttrFileReference = "fileReference";
    private const string S101AttrLanguage = "language";

    // S-101 attribute codes for the `featureName` complex attribute and its
    // sub-attributes (verified against the bundled FC; `name` is [1..1],
    // `language` is [1..1], `nameUsage` is [0..1] and has no S-57 source).
    private const string S101AttrFeatureName = "featureName";
    private const string S101AttrName = "name";

    // ── S-57 light-characteristic attribute codes (S-57 Appendix A) ──
    // On light features these do NOT pass through as simple attributes;
    // they become sub-attributes of the S-101 `rhythmOfLight` complex
    // attribute. LITCHR is the mandatory `lightCharacteristic` [1..1];
    // SIGGRP/SIGPER are the optional `signalGroup`/`signalPeriod`. (The
    // nested `signalSequence` sub-complex from SIGSEQ, and the sector
    // geometry from SECTR1/SECTR2/LITVIS, are deferred.)
    private const ushort S57AttrLitchr = 107;   // LITCHR  — light characteristic
    private const ushort S57AttrSiggrp = 141;   // SIGGRP  — signal group
    private const ushort S57AttrSigper = 142;   // SIGPER  — signal period

    // S-101 attribute codes for the `rhythmOfLight` complex attribute and
    // its (first-level) sub-attributes (verified against the bundled FC:
    // `lightCharacteristic` [1..1], `signalGroup` [0..*], `signalPeriod`
    // [0..1]).
    private const string S101AttrRhythmOfLight = "rhythmOfLight";
    private const string S101AttrLightCharacteristic = "lightCharacteristic";
    private const string S101AttrSignalGroup = "signalGroup";
    private const string S101AttrSignalPeriod = "signalPeriod";

    // S-101 feature classes that bind `rhythmOfLight` directly (per the
    // bundled S-101 FC). On any other feature class, LITCHR/SIGGRP/SIGPER
    // are handled by the normal per-attribute path (e.g. `signalGroup` /
    // `signalPeriod` are directly feature-bound simple attributes on
    // FogSignal / RadarTransponderBeacon).
    private static readonly FrozenSet<string> RhythmOfLightFeatureClasses =
        new[] { "LightAllAround", "LightFogDetector", "LightAirObstruction" }
            .ToFrozenSet(StringComparer.Ordinal);

    // S-57 SIGSEQ (Signal sequence, ATTL 143) maps to the S-101
    // `signalSequence` complex attribute. The S-57 value (S-57 Appendix B.1)
    // is a '+'-separated list of phase durations in seconds; a duration in
    // parentheses denotes an eclipse / silence phase. Each phase becomes one
    // `signalSequence` instance carrying `signalDuration` [1..1] (real,
    // seconds) and `signalStatus` [1..1] (1 = Lit/Sound for a bare duration,
    // 2 = Eclipsed/Silent for a parenthesised duration). On the light feature
    // classes that bind `rhythmOfLight` the sequence nests inside that complex
    // (it is the last sub-attribute in the FC's binding order); on FogSignal /
    // RadarTransponderBeacon `signalSequence` is bound at the top level.
    private const ushort S57AttrSigseq = 143;  // SIGSEQ — signal sequence
    private const string S101AttrSignalSequence = "signalSequence";
    private const string S101AttrSignalDuration = "signalDuration";
    private const string S101AttrSignalStatus = "signalStatus";
    private const string SignalStatusLit = "1";        // Lit / Sound
    private const string SignalStatusEclipsed = "2";   // Eclipsed / Silent

    // ── S-57 date-range attribute codes (S-57 Appendix A) ──
    // These pairs are not simple pass-through attributes; each pair becomes a
    // distinct S-101 date-range *complex* attribute whose `dateStart`/`dateEnd`
    // sub-attributes are of type S100_TruncatedDate. The destination complex is
    // determined by the S-57 pair, and is only emitted on a feature class that
    // actually binds it (see S101FeatureAttributeBindings), since the shared
    // sub-attributes would otherwise be non-conformant:
    //   DATSTA/DATEND → fixedDateRange     (dateStart [0..1], dateEnd [0..1])
    //   PERSTA/PEREND → periodicDateRange  (dateStart [1..1], dateEnd [1..1])
    //   SURSTA/SUREND → surveyDateRange    (dateStart [0..1], dateEnd [1..1])
    private const ushort S57AttrDatsta = 86;   // DATSTA — date start
    private const ushort S57AttrDatend = 85;   // DATEND — date end
    private const ushort S57AttrPersta = 119;  // PERSTA — periodic date start
    private const ushort S57AttrPerend = 118;  // PEREND — periodic date end
    private const ushort S57AttrSursta = 152;  // SURSTA — survey date start
    private const ushort S57AttrSurend = 151;  // SUREND — survey date end

    // S-101 date-range complex attribute codes and their shared sub-attribute
    // codes (verified against the bundled FC).
    private const string S101AttrFixedDateRange = "fixedDateRange";
    private const string S101AttrPeriodicDateRange = "periodicDateRange";
    private const string S101AttrSurveyDateRange = "surveyDateRange";
    private const string S101AttrDateStart = "dateStart";
    private const string S101AttrDateEnd = "dateEnd";

    // S-57 CATZOC (Category of zone of confidence in data, ATTL 72) maps to the
    // S-101 `zoneOfConfidence` complex attribute's `categoryOfZoneOfConfidence-
    // InData` sub-attribute (S-101 Conversion Guidance; verified against the
    // bundled FC). CATZOC is only bound to S-57 M_QUAL, which translates to the
    // S-101 QualityOfBathymetricData feature — the sole feature class binding
    // `zoneOfConfidence`. The complex's other sub-attributes (fixedDateRange,
    // horizontalPositionUncertainty, verticalUncertainty) have no CATZOC-side
    // source and are left unpopulated. The enumeration values are identical in
    // S-57 and S-101 (1=A1, 2=A2, 3=B, 4=C, 5=D, 6=U), so no remapping is
    // needed; an out-of-range code drops the instance (its only sub-attribute
    // would be missing).
    private const ushort S57AttrCatzoc = 72;   // CATZOC — category of ZOC in data
    private const string S101AttrZoneOfConfidence = "zoneOfConfidence";
    private const string S101AttrCategoryOfZocInData = "categoryOfZoneOfConfidenceInData";

    // S-57 NATSUR (Nature of surface, ATTL 113) and NATQUA (Nature of surface,
    // qualifying terms, ATTL 114) — both list-valued — map onto the S-101
    // `surfaceCharacteristics` complex attribute's `natureOfSurface` and
    // `natureOfSurfaceQualifyingTerms` sub-attributes (S-101 Conversion
    // Guidance; verified against the bundled FC). `surfaceCharacteristics` is
    // bound only to SeabedArea (SBDARE), which — unlike Coastline, LandRegion,
    // etc. — does NOT bind a top-level `natureOfSurface`, so on SeabedArea the
    // NATSUR value has no conformant home except inside the complex. The two
    // S-57 lists are paired positionally into one repeating complex instance
    // per position: `natureOfSurface` [0..1] and `natureOfSurfaceQualifyingTerms`
    // [0..3] are both optional, so positions with only one of the two still
    // form a valid instance.
    private const ushort S57AttrNatsur = 113;  // NATSUR — nature of surface
    private const ushort S57AttrNatqua = 114;  // NATQUA — nature of surface, qualifying terms
    private const string S101AttrSurfaceCharacteristics = "surfaceCharacteristics";
    private const string S101AttrNatureOfSurface = "natureOfSurface";
    private const string S101AttrNatureOfSurfaceQualifyingTerms = "natureOfSurfaceQualifyingTerms";

    // ISO 639-3 language code used for the English-language INFORM/TXTDSC
    // bucket. NINFOM/NTXTDS are emitted with an empty language string,
    // since S-57 carries no language tag and Data Producers are expected
    // to fill it in manually (Conversion Guidance §2.3).
    private const string LanguageEng = "eng";

    private readonly S57S101Mapping _mapping;
    private readonly S101AllowedEnumValues? _allowedEnumValues;
    private readonly S101FeatureAttributeBindings _featureBindings;

    /// <summary>Creates a translator using <see cref="S57S101Mapping.Default"/>.</summary>
    public S57ToS101Translator() : this(S57S101Mapping.Default, S101AllowedEnumValues.Default) { }

    /// <summary>Creates a translator using the supplied code mapping.</summary>
    public S57ToS101Translator(S57S101Mapping mapping)
        : this(mapping, S101AllowedEnumValues.Default) { }

    /// <summary>
    /// Creates a translator using the supplied code mapping and the given
    /// allowable-value lookup. Pass <c>null</c> for <paramref name="allowedEnumValues"/>
    /// to disable enumerate-value enforcement (useful in tests).
    /// </summary>
    public S57ToS101Translator(S57S101Mapping mapping, S101AllowedEnumValues? allowedEnumValues)
        : this(mapping, allowedEnumValues, S101FeatureAttributeBindings.Default) { }

    /// <summary>
    /// Creates a translator using the supplied code mapping, allowable-value
    /// lookup, and feature/attribute binding lookup. Pass <c>null</c> for
    /// <paramref name="allowedEnumValues"/> to disable enumerate-value
    /// enforcement (useful in tests). <paramref name="featureBindings"/> gates
    /// which feature classes may carry each assembled S-101 complex attribute.
    /// </summary>
    public S57ToS101Translator(
        S57S101Mapping mapping,
        S101AllowedEnumValues? allowedEnumValues,
        S101FeatureAttributeBindings featureBindings)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(featureBindings);
        _mapping = mapping;
        _allowedEnumValues = allowedEnumValues;
        _featureBindings = featureBindings;
    }

    /// <summary>
    /// Translates an <see cref="S57Dataset"/> into an <see cref="S101Document"/>.
    /// </summary>
    public S101Document Translate(S57Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Translate(dataset.Document, diagnostics: null);
    }

    /// <summary>
    /// Translates an <see cref="S57Dataset"/> into an <see cref="S101Document"/>,
    /// recording what was dropped into <paramref name="diagnostics"/>.
    /// </summary>
    public S101Document Translate(S57Dataset dataset, S57TranslationDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Translate(dataset.Document, diagnostics);
    }

    /// <summary>
    /// Translates an <see cref="EncDotNet.S57.S57Document"/> into an
    /// <see cref="S101Document"/>.
    /// </summary>
    public S101Document Translate(EncDotNet.S57.S57Document s57)
        => Translate(s57, diagnostics: null);

    /// <summary>
    /// Translates an <see cref="EncDotNet.S57.S57Document"/> into an
    /// <see cref="S101Document"/>, optionally recording per-drop diagnostics.
    /// </summary>
    /// <param name="s57">The parsed S-57 document to translate.</param>
    /// <param name="diagnostics">
    /// Optional collector that accumulates the object classes, attributes, and
    /// enumerate values dropped during translation. Pass <c>null</c> (the
    /// default) to disable collection with zero overhead.
    /// </param>
    public S101Document Translate(EncDotNet.S57.S57Document s57, S57TranslationDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(s57);

        var ctx = new TranslationContext(s57, _mapping, _allowedEnumValues, _featureBindings, diagnostics);
        ctx.IndexVectorRecords();
        ctx.TranslateNodes();
        ctx.TranslateEdges();
        ctx.TranslateFeatures();

        // S57Document.CoordinateMultiplicationFactor / SoundingMultiplicationFactor
        // already supply the documented defaults (10_000_000 and 10).
        var cmf = (uint)s57.CoordinateMultiplicationFactor;
        var somf = (uint)s57.SoundingMultiplicationFactor;
        var dsid = s57.DataSetIdentification;

        return new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                RecordName = 10,
                RecordId = 1,
                ProductSpecification = "S-101",
                ProductSpecificationEdition = "1.0.0",
                DatasetName = dsid?.DataSetName ?? "",
                DatasetTitle = dsid?.DataSetName ?? "",
                DatasetReferenceDate = dsid?.IssueDate ?? "",
                DatasetLanguage = "eng",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = cmf,
                CoordinateMultiplicationFactorY = cmf,
                CoordinateMultiplicationFactorZ = somf,
            },
            FeatureTypeCatalogue = ctx.FeatureTypeCatalogue,
            AttributeTypeCatalogue = ctx.AttributeTypeCatalogue,
            Points = ctx.Points,
            MultiPoints = ctx.MultiPoints,
            CurveSegments = ctx.CurveSegments,
            CompositeCurves = ctx.CompositeCurves,
            Surfaces = ctx.Surfaces,
            Features = ctx.Features,
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }

    // ── Translation context ─────────────────────────────────────────────

    private sealed class TranslationContext
    {
        private readonly EncDotNet.S57.S57Document _s57;
        private readonly S57S101Mapping _mapping;
        private readonly S101AllowedEnumValues? _allowedEnumValues;
        private readonly S101FeatureAttributeBindings _featureBindings;
        private readonly S57TranslationDiagnostics? _diagnostics;

        // Index of the document's flat VectorRecords list, keyed by
        // (RecordNameCode, RecordId) for fast lookup from spatial pointers.
        private readonly Dictionary<(int rcnm, int rcid), EncDotNet.S57.S57VectorRecord> _vectorIndex = new();

        // Mapping from S-57 (RCNM, RCID) to allocated S-101 IDs, per spatial kind.
        private readonly Dictionary<(int rcnm, int rcid), uint> _nodeIdMap = new();
        private readonly Dictionary<int, uint> _edgeIdMap = new();
        private uint _nextPointId = 1;
        private uint _nextMultiPointId = 1;
        private uint _nextCurveId = 1;
        private uint _nextCompositeId = 1;
        private uint _nextSurfaceId = 1;
        private uint _nextFeatureId = 1;
        private ushort _nextFeatureTypeCode = 1;
        private ushort _nextAttributeCode = 1;
        private readonly Dictionary<string, ushort> _featureTypeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _attributeByName = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<uint, S101PointRecord> Points { get; } = new();
        public Dictionary<uint, S101MultiPointRecord> MultiPoints { get; } = new();
        public Dictionary<uint, S101CurveSegmentRecord> CurveSegments { get; } = new();
        public Dictionary<uint, S101CompositeCurveRecord> CompositeCurves { get; } = new();
        public Dictionary<uint, S101SurfaceRecord> Surfaces { get; } = new();
        public List<S101FeatureRecord> Features { get; } = new();
        public Dictionary<ushort, string> FeatureTypeCatalogue { get; } = new();
        public Dictionary<ushort, string> AttributeTypeCatalogue { get; } = new();

        public TranslationContext(
            EncDotNet.S57.S57Document s57,
            S57S101Mapping mapping,
            S101AllowedEnumValues? allowedEnumValues,
            S101FeatureAttributeBindings featureBindings,
            S57TranslationDiagnostics? diagnostics)
        {
            _s57 = s57;
            _mapping = mapping;
            _allowedEnumValues = allowedEnumValues;
            _featureBindings = featureBindings;
            _diagnostics = diagnostics;
        }

        public void IndexVectorRecords()
        {
            foreach (var vr in _s57.VectorRecords)
            {
                var key = (vr.RecordName.RecordNameCode, vr.RecordName.RecordId);
                _vectorIndex[key] = vr;
            }
        }

        // ── Spatial translation ─────────────────────────────────────────

        public void TranslateNodes()
        {
            foreach (var vr in _s57.VectorRecords)
            {
                var rcnm = vr.RecordName.RecordNameCode;
                if (rcnm != S57RecordNameCodes.IsolatedNode
                    && rcnm != S57RecordNameCodes.ConnectedNode)
                    continue;

                // Skip multi-point sounding nodes here; they're exploded into
                // features in TranslateFeatures().
                if (vr.Soundings.Count > 0) continue;
                if (vr.Coordinates2D.Count == 0) continue;

                var coord = vr.Coordinates2D[0];
                var id = _nextPointId++;
                Points[id] = new S101PointRecord { RecordId = id, Y = coord.Y, X = coord.X };
                _nodeIdMap[(rcnm, vr.RecordName.RecordId)] = id;
            }
        }

        public void TranslateEdges()
        {
            foreach (var vr in _s57.VectorRecords)
            {
                if (vr.RecordName.RecordNameCode != S57RecordNameCodes.Edge) continue;

                S101PointAssociation? begin = null;
                S101PointAssociation? end = null;
                foreach (var p in vr.VectorPointers)
                {
                    var topo = (int)p.Topology;
                    if (topo == TopologyBegin)
                    {
                        if (TryGetPointId(p.Name, out var pid))
                            begin = new S101PointAssociation(S101RcnmPoint, pid, TopologyBegin);
                    }
                    else if (topo == TopologyEnd)
                    {
                        if (TryGetPointId(p.Name, out var pid))
                            end = new S101PointAssociation(S101RcnmPoint, pid, TopologyEnd);
                    }
                }

                var ptas = new List<S101PointAssociation>();
                if (begin is not null) ptas.Add(begin.Value);
                if (end is not null) ptas.Add(end.Value);

                // Project package coords to (Y,X) tuples expected by the S-101 record.
                var intermediates = new List<(int Y, int X)>(vr.Coordinates2D.Count);
                foreach (var c in vr.Coordinates2D)
                    intermediates.Add((c.Y, c.X));

                var id = _nextCurveId++;
                CurveSegments[id] = new S101CurveSegmentRecord
                {
                    RecordId = id,
                    PointAssociations = ptas,
                    IntermediateCoordinates = intermediates,
                };
                _edgeIdMap[vr.RecordName.RecordId] = id;
            }
        }

        private bool TryGetPointId(S57RecordName name, out uint id)
            => _nodeIdMap.TryGetValue((name.RecordNameCode, name.RecordId), out id);

        // ── Feature translation ─────────────────────────────────────────

        public void TranslateFeatures()
        {
            foreach (var feat in _s57.FeatureRecords)
            {
                var objl = (ushort)(int)feat.ObjectCode;
                if (objl == SoundingObjl)
                {
                    if (_diagnostics is not null) _diagnostics.SoundingFeaturesRead++;
                    EmitSoundingMultiPoint(feat);
                    continue;
                }

                if (_diagnostics is not null) _diagnostics.FeatureRecordsRead++;

                var acronymView = _mapping.BuildAcronymView(feat.Attributes);
                var resolved = _mapping.ResolveFeature(objl, acronymView);
                if (resolved is null)
                {
                    if (_diagnostics is not null)
                    {
                        if (_mapping.FeatureRules.ContainsKey(objl))
                            _diagnostics.RecordRuleDroppedObjectClass(objl);
                        else
                            _diagnostics.RecordUnmappedObjectClass(objl);
                    }
                    continue;
                }

                var typeCode = GetOrAssignFeatureTypeCode(resolved.S101Code);
                var attributes = TranslateAttributes(feat.Attributes, resolved, objl);
                var spatials = TranslateSpatialPointers(feat);
                if (spatials.Count == 0)
                {
                    _diagnostics?.RecordFeatureWithoutGeometry(resolved.S101Code);
                    continue;
                }

                if (_diagnostics is not null) _diagnostics.FeaturesEmitted++;
                Features.Add(new S101FeatureRecord
                {
                    RecordId = _nextFeatureId++,
                    FeatureTypeCode = typeCode,
                    ProducingAgency = (ushort)feat.RecordName.AgencyCode,
                    FeatureIdentificationNumber = (uint)feat.RecordName.FeatureId,
                    FeatureIdentificationSubdivision = (ushort)feat.RecordName.FeatureSubdivision,
                    Attributes = attributes,
                    SpatialAssociations = spatials,
                    FeatureAssociations = [],
                    InformationAssociations = [],
                });
            }
        }

        /// <summary>
        /// S-57 SOUNDG (OBJL=129) features carry many depth measurements via SG3D
        /// triples on one or more vector records. The S-101 portrayal pipeline
        /// expects <c>Sounding</c> features whose <c>PrimitiveType</c> is
        /// <c>MultiPoint</c>; emit a single S-101 Sounding feature backed by an
        /// S-101 multi-point record (RCNM=115) so the SOUNDG03 rule can iterate
        /// the points and draw symbolised depth values.
        /// </summary>
        private void EmitSoundingMultiPoint(EncDotNet.S57.S57FeatureRecord feat)
        {
            var triples = new List<(int Y, int X, int Z)>();
            foreach (var ptr in feat.SpatialPointers)
            {
                if (!_vectorIndex.TryGetValue(
                        (ptr.Name.RecordNameCode, ptr.Name.RecordId),
                        out var vr))
                    continue;
                foreach (var s in vr.Soundings)
                    triples.Add((s.Y, s.X, s.Depth));
            }

            if (triples.Count == 0)
            {
                if (_diagnostics is not null) _diagnostics.SoundingFeaturesWithoutPoints++;
                return;
            }

            var mpid = _nextMultiPointId++;
            MultiPoints[mpid] = new S101MultiPointRecord
            {
                RecordId = mpid,
                Points = triples,
            };

            if (_diagnostics is not null)
            {
                _diagnostics.SoundingFeaturesEmitted++;
                _diagnostics.SoundingPointsEmitted += triples.Count;
            }

            var typeCode = GetOrAssignFeatureTypeCode(SoundingS101Code);
            Features.Add(new S101FeatureRecord
            {
                RecordId = _nextFeatureId++,
                FeatureTypeCode = typeCode,
                ProducingAgency = (ushort)feat.RecordName.AgencyCode,
                FeatureIdentificationNumber = (uint)feat.RecordName.FeatureId,
                FeatureIdentificationSubdivision = (ushort)feat.RecordName.FeatureSubdivision,
                // The S-101 Sounding feature carries no attributes — depth values
                // live on the individual points within the MultiPoint geometry,
                // which the SOUNDG03 portrayal rule reads as point.ScaledZ.
                Attributes = [],
                SpatialAssociations = [new S101SpatialAssociation(S101RcnmMultiPoint, mpid, OrientationForward)],
                FeatureAssociations = [],
                InformationAssociations = [],
            });
        }

        private IReadOnlyList<S101Attribute> TranslateAttributes(
            IReadOnlyList<EncDotNet.S57.S57AttributeValue> attrs,
            ResolvedFeature feature,
            ushort ownerObjl)
        {
            if (attrs.Count == 0) return [];

            // Pre-pass: collect INFORM / NINFOM / TXTDSC / NTXTDS values so we
            // can emit them as one or more S-101 `information` complex-attribute
            // instances (Conversion Guidance §2.3), and OBJNAM / NOBJNM so we
            // can emit them as `featureName` complex-attribute instances.
            string? informText = null;
            string? ninfomText = null;
            string? txtdscFile = null;
            string? ntxtdsFile = null;
            string? objnamText = null;
            string? nobjnmText = null;
            // rhythmOfLight sources — only assembled on light features that
            // bind the complex (see RhythmOfLightFeatureClasses).
            bool bindsRhythm = RhythmOfLightFeatureClasses.Contains(feature.S101Code);
            string? litchrValue = null;
            string? siggrpValue = null;
            string? sigperValue = null;
            // signalSequence source — SIGSEQ. On the light feature classes it
            // nests inside `rhythmOfLight`; on FogSignal / RadarTransponderBeacon
            // it binds at the top level. Gated so it is only diverted from the
            // per-attribute pass-through where it has a conformant home.
            bool bindsSignalSequenceTop = _featureBindings.Binds(feature.S101Code, S101AttrSignalSequence);
            string? sigseqValue = null;
            // Date-range sources — each S-57 pair maps to a distinct S-101
            // date-range complex, gated on the resolved feature class actually
            // binding that complex (per the bundled FC).
            bool bindsFixedDate = _featureBindings.Binds(feature.S101Code, S101AttrFixedDateRange);
            bool bindsPeriodicDate = _featureBindings.Binds(feature.S101Code, S101AttrPeriodicDateRange);
            bool bindsSurveyDate = _featureBindings.Binds(feature.S101Code, S101AttrSurveyDateRange);
            string? datstaValue = null;
            string? datendValue = null;
            string? perstaValue = null;
            string? perendValue = null;
            string? surstaValue = null;
            string? surendValue = null;
            // zoneOfConfidence source — CATZOC, assembled on the (single) feature
            // class that binds the complex (QualityOfBathymetricData).
            bool bindsZoc = _featureBindings.Binds(feature.S101Code, S101AttrZoneOfConfidence);
            string? catzocValue = null;
            // surfaceCharacteristics source — NATSUR/NATQUA, assembled on the
            // (single) feature class that binds the complex (SeabedArea).
            bool bindsSurfaceChar = _featureBindings.Binds(feature.S101Code, S101AttrSurfaceCharacteristics);
            string? natsurList = null;
            string? natquaList = null;
            foreach (var a in attrs)
            {
                switch (a.AttributeCode)
                {
                    case S57AttrInform: informText = a.Value; break;
                    case S57AttrNinfom: ninfomText = a.Value; break;
                    case S57AttrTxtdsc: txtdscFile = a.Value; break;
                    case S57AttrNtxtds: ntxtdsFile = a.Value; break;
                    case S57AttrObjnam: if (!string.IsNullOrEmpty(a.Value)) objnamText = a.Value; break;
                    case S57AttrNobjnm: if (!string.IsNullOrEmpty(a.Value)) nobjnmText = a.Value; break;
                    case S57AttrLitchr: if (bindsRhythm && !string.IsNullOrEmpty(a.Value)) litchrValue = a.Value; break;
                    case S57AttrSiggrp: if (bindsRhythm && !string.IsNullOrEmpty(a.Value)) siggrpValue = a.Value; break;
                    case S57AttrSigper: if (bindsRhythm && !string.IsNullOrEmpty(a.Value)) sigperValue = a.Value; break;
                    case S57AttrSigseq: if ((bindsRhythm || bindsSignalSequenceTop) && !string.IsNullOrEmpty(a.Value)) sigseqValue = a.Value; break;
                    case S57AttrDatsta: if (bindsFixedDate && !string.IsNullOrEmpty(a.Value)) datstaValue = a.Value; break;
                    case S57AttrDatend: if (bindsFixedDate && !string.IsNullOrEmpty(a.Value)) datendValue = a.Value; break;
                    case S57AttrPersta: if (bindsPeriodicDate && !string.IsNullOrEmpty(a.Value)) perstaValue = a.Value; break;
                    case S57AttrPerend: if (bindsPeriodicDate && !string.IsNullOrEmpty(a.Value)) perendValue = a.Value; break;
                    case S57AttrSursta: if (bindsSurveyDate && !string.IsNullOrEmpty(a.Value)) surstaValue = a.Value; break;
                    case S57AttrSurend: if (bindsSurveyDate && !string.IsNullOrEmpty(a.Value)) surendValue = a.Value; break;
                    case S57AttrCatzoc: if (bindsZoc && !string.IsNullOrEmpty(a.Value)) catzocValue = a.Value; break;
                    case S57AttrNatsur: if (bindsSurfaceChar && !string.IsNullOrEmpty(a.Value)) natsurList = a.Value; break;
                    case S57AttrNatqua: if (bindsSurfaceChar && !string.IsNullOrEmpty(a.Value)) natquaList = a.Value; break;
                }
            }

            var builder = new List<S101Attribute>();
            foreach (var a in attrs)
            {
                // Textual-info and object-name attributes are handled as complex
                // attribute groups below — skip the per-attribute pass-through.
                if (a.AttributeCode is S57AttrInform or S57AttrNinfom or S57AttrTxtdsc or S57AttrNtxtds
                    or S57AttrObjnam or S57AttrNobjnm)
                    continue;

                // On rhythmOfLight-binding feature classes, LITCHR/SIGGRP/SIGPER
                // are assembled into the `rhythmOfLight` complex attribute below
                // rather than emitted as top-level simple attributes.
                if (bindsRhythm && a.AttributeCode is S57AttrLitchr or S57AttrSiggrp or S57AttrSigper)
                    continue;

                // SIGSEQ is assembled into the `signalSequence` complex —
                // nested inside `rhythmOfLight` on light features, or top-level
                // on FogSignal / RadarTransponderBeacon. On any other feature
                // it falls through and is recorded as unmapped (no conformant
                // home in S-101).
                if ((bindsRhythm || bindsSignalSequenceTop) && a.AttributeCode is S57AttrSigseq)
                    continue;

                // On feature classes that bind a date-range complex, the S-57
                // date pair is assembled into that complex below rather than
                // passed through. (When the feature does not bind the complex
                // the pair falls through and is recorded as unmapped, since the
                // date has no conformant home in S-101 on that feature.)
                if (bindsFixedDate && a.AttributeCode is S57AttrDatsta or S57AttrDatend)
                    continue;
                if (bindsPeriodicDate && a.AttributeCode is S57AttrPersta or S57AttrPerend)
                    continue;
                if (bindsSurveyDate && a.AttributeCode is S57AttrSursta or S57AttrSurend)
                    continue;

                // On QualityOfBathymetricData, CATZOC is assembled into the
                // `zoneOfConfidence` complex below rather than passed through.
                if (bindsZoc && a.AttributeCode is S57AttrCatzoc)
                    continue;

                // On SeabedArea, NATSUR/NATQUA are assembled into the
                // `surfaceCharacteristics` complex below. SeabedArea does not
                // bind a top-level `natureOfSurface`, so passing NATSUR through
                // would be non-conformant; NATQUA has no top-level home at all.
                if (bindsSurfaceChar && a.AttributeCode is S57AttrNatsur or S57AttrNatqua)
                    continue;

                var attl = (ushort)a.AttributeCode;
                if (!_mapping.AttributeRules.TryGetValue(attl, out var attrRule))
                {
                    _diagnostics?.RecordUnmappedAttribute(ownerObjl, attl);
                    continue;
                }

                var resolved = _mapping.ResolveAttribute(attrRule.S57Acronym, a.Value, feature);
                if (resolved is null)
                {
                    _diagnostics?.RecordRuleDroppedAttribute(attl);
                    continue;
                }

                // S-57 list-type attributes (e.g. COLOUR, NATSUR, CATLIT)
                // carry multiple enumerate codes as a comma-separated string
                // (e.g. "3,3"). The destination S-101 enumerate attribute
                // encodes each value as a separate occurrence, so split the
                // list and validate each code independently — otherwise a
                // single invalid (or simply multi-valued) code would cause the
                // whole attribute to be dropped. Enumerate codes are integer
                // tokens and never contain commas, so non-enumerate attributes
                // (text such as OBJNAM, which may legitimately contain commas)
                // are never split.
                if (_allowedEnumValues is not null
                    && _allowedEnumValues.IsEnumerated(resolved.S101Code)
                    && a.Value.Contains(','))
                {
                    ushort index = 1;
                    foreach (var rawCode in a.Value.Split(','))
                    {
                        var code = rawCode.Trim();
                        if (code.Length == 0)
                            continue;

                        var sub = _mapping.ResolveAttribute(attrRule.S57Acronym, code, feature);
                        if (sub is null)
                        {
                            _diagnostics?.RecordRuleDroppedAttribute(attl);
                            continue;
                        }

                        if (!_allowedEnumValues.IsAllowed(sub.S101Code, sub.Value))
                        {
                            _diagnostics?.RecordDroppedEnumValue(sub.S101Code, sub.Value);
                            continue;
                        }

                        builder.Add(new S101Attribute(GetOrAssignAttributeCode(sub.S101Code), index++, sub.Value));
                    }
                    continue;
                }

                // Drop S-57 enum values that aren't in the S-101 FC's allowable
                // listed values (per IHO S-57→S-101 Conversion Guidance, Jan 2021).
                if (_allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(resolved.S101Code, resolved.Value))
                {
                    _diagnostics?.RecordDroppedEnumValue(resolved.S101Code, resolved.Value);
                    continue;
                }

                var numeric = GetOrAssignAttributeCode(resolved.S101Code);
                builder.Add(new S101Attribute(numeric, 1, resolved.Value));
            }

            // Append `information` complex-attribute instances. Each instance
            // is (marker, text?, fileReference?, language) — language is
            // mandatory in the S-101 FC's binding for `information`.
            if (informText is not null || txtdscFile is not null)
                AppendInformationInstance(builder, text: informText, fileReference: txtdscFile, language: LanguageEng);
            if (ninfomText is not null || ntxtdsFile is not null)
                AppendInformationInstance(builder, text: ninfomText, fileReference: ntxtdsFile, language: string.Empty);

            // Append `featureName` complex-attribute instances. OBJNAM carries
            // the English name; NOBJNM the national-language name (emitted with
            // an empty language string, as S-57 carries no language tag —
            // mirrors the INFORM/NINFOM handling above).
            if (objnamText is not null)
                AppendFeatureNameInstance(builder, name: objnamText, language: LanguageEng);
            if (nobjnmText is not null)
                AppendFeatureNameInstance(builder, name: nobjnmText, language: string.Empty);

            // Append the `rhythmOfLight` complex-attribute instance. The FC
            // makes `lightCharacteristic` [1..1] mandatory, so an instance is
            // only emitted when a valid LITCHR value is present; SIGGRP/SIGPER
            // are included as the optional `signalGroup`/`signalPeriod`
            // sub-attributes and SIGSEQ as the nested `signalSequence`
            // sub-complex. An out-of-range LITCHR code is dropped (and
            // reported), which also drops the instance since the mandatory
            // sub-attribute would be missing; a SIGSEQ carried by a light with
            // no valid LITCHR therefore has nowhere to nest and is dropped.
            if (bindsRhythm && litchrValue is not null)
            {
                if (_allowedEnumValues is null
                    || _allowedEnumValues.IsAllowed(S101AttrLightCharacteristic, litchrValue))
                {
                    AppendRhythmOfLightInstance(builder, litchrValue, siggrpValue, sigperValue, sigseqValue);
                }
                else
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrLightCharacteristic, litchrValue);
                    if (sigseqValue is not null)
                        _diagnostics?.RecordRuleDroppedAttribute(S57AttrSigseq);
                }
            }
            else if (bindsRhythm && sigseqValue is not null)
            {
                // SIGSEQ present on a rhythmOfLight-binding light but no LITCHR
                // to anchor the parent complex — the sequence cannot be nested.
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrSigseq);
            }

            // Append the date-range complex-attribute instances. Each is a
            // marker followed by the S100_TruncatedDate `dateStart`/`dateEnd`
            // sub-attributes (S-57 dates pass through unchanged; both S-57 and
            // S-101 use CCYYMMDD). Mandatory sub-attributes (per the FC's
            // multiplicity) must be present or the instance is dropped and
            // recorded, rather than emitting a non-conformant partial complex.
            //
            //   fixedDateRange    — dateStart [0..1], dateEnd [0..1]: emit if
            //                       either endpoint is present.
            if (bindsFixedDate && (datstaValue is not null || datendValue is not null))
                AppendDateRangeInstance(builder, S101AttrFixedDateRange, datstaValue, datendValue);

            //   periodicDateRange — dateStart [1..1], dateEnd [1..1]: both
            //                       endpoints are mandatory.
            if (bindsPeriodicDate)
            {
                if (perstaValue is not null && perendValue is not null)
                    AppendDateRangeInstance(builder, S101AttrPeriodicDateRange, perstaValue, perendValue);
                else if (perstaValue is not null)
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrPersta);
                else if (perendValue is not null)
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrPerend);
            }

            //   surveyDateRange   — dateStart [0..1], dateEnd [1..1]: dateEnd is
            //                       mandatory, dateStart optional.
            if (bindsSurveyDate)
            {
                if (surendValue is not null)
                    AppendDateRangeInstance(builder, S101AttrSurveyDateRange, surstaValue, surendValue);
                else if (surstaValue is not null)
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrSursta);
            }

            // Append the `zoneOfConfidence` complex-attribute instance. The only
            // CATZOC-sourced sub-attribute, `categoryOfZoneOfConfidenceInData`,
            // is mandatory in practice (nothing else is populated), so an
            // out-of-range CATZOC code drops the instance and is reported.
            if (bindsZoc && catzocValue is not null)
            {
                if (_allowedEnumValues is null
                    || _allowedEnumValues.IsAllowed(S101AttrCategoryOfZocInData, catzocValue))
                {
                    AppendZoneOfConfidenceInstance(builder, catzocValue);
                }
                else
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrCategoryOfZocInData, catzocValue);
                }
            }

            // Append `surfaceCharacteristics` complex-attribute instances. The
            // S-57 NATSUR and NATQUA lists are paired positionally: position i
            // yields one instance carrying `natureOfSurface` = NATSUR[i] (when
            // present and permitted) and `natureOfSurfaceQualifyingTerms` =
            // NATQUA[i] (when present and permitted). Both sub-attributes are
            // optional, so a position with only one populated still forms a
            // valid instance; a position where both are dropped emits nothing.
            if (bindsSurfaceChar && (natsurList is not null || natquaList is not null))
                AppendSurfaceCharacteristicsInstances(builder, natsurList, natquaList);

            // Append top-level `signalSequence` complex-attribute instances on
            // feature classes that bind it directly (FogSignal,
            // RadarTransponderBeacon). On the light feature classes SIGSEQ is
            // instead nested inside `rhythmOfLight` (emitted above), so the
            // `!bindsRhythm` guard prevents any double emission.
            if (bindsSignalSequenceTop && !bindsRhythm && sigseqValue is not null)
                AppendSignalSequenceInstances(builder, sigseqValue);

            return builder;
        }

        private void AppendInformationInstance(
            List<S101Attribute> builder,
            string? text,
            string? fileReference,
            string language)
        {
            var infoCode = GetOrAssignAttributeCode(S101AttrInformation);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(infoCode, 1, string.Empty));
            if (text is not null)
            {
                var textCode = GetOrAssignAttributeCode(S101AttrText);
                builder.Add(new S101Attribute(textCode, 1, text));
            }
            if (fileReference is not null)
            {
                var fileRefCode = GetOrAssignAttributeCode(S101AttrFileReference);
                builder.Add(new S101Attribute(fileRefCode, 1, fileReference));
            }
            var langCode = GetOrAssignAttributeCode(S101AttrLanguage);
            builder.Add(new S101Attribute(langCode, 1, language));
        }

        // Emits a `featureName` complex-attribute instance using the same
        // marker + contiguous-sub-attribute convention as the information
        // complex (the S-101 data provider identifies an instance by the
        // complex marker row and collects the sub-rows that follow it).
        private void AppendFeatureNameInstance(
            List<S101Attribute> builder,
            string name,
            string language)
        {
            var featureNameCode = GetOrAssignAttributeCode(S101AttrFeatureName);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(featureNameCode, 1, string.Empty));
            var nameCode = GetOrAssignAttributeCode(S101AttrName);
            builder.Add(new S101Attribute(nameCode, 1, name));
            var langCode = GetOrAssignAttributeCode(S101AttrLanguage);
            builder.Add(new S101Attribute(langCode, 1, language));
        }

        // Emits a `rhythmOfLight` complex-attribute instance (marker +
        // lightCharacteristic + optional signalGroup/signalPeriod), using the
        // same flat marker + contiguous-sub-attribute convention as the other
        // complex attributes. Only the first level of the FC's structure is
        // populated; the nested `signalSequence` sub-complex (from SIGSEQ) is
        // not yet assembled.
        private void AppendRhythmOfLightInstance(
            List<S101Attribute> builder,
            string lightCharacteristic,
            string? signalGroup,
            string? signalPeriod,
            string? signalSequence)
        {
            var rhythmCode = GetOrAssignAttributeCode(S101AttrRhythmOfLight);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(rhythmCode, 1, string.Empty));
            var litCharCode = GetOrAssignAttributeCode(S101AttrLightCharacteristic);
            builder.Add(new S101Attribute(litCharCode, 1, lightCharacteristic));
            if (signalGroup is not null)
            {
                var sigGrpCode = GetOrAssignAttributeCode(S101AttrSignalGroup);
                builder.Add(new S101Attribute(sigGrpCode, 1, signalGroup));
            }
            if (signalPeriod is not null)
            {
                var sigPerCode = GetOrAssignAttributeCode(S101AttrSignalPeriod);
                builder.Add(new S101Attribute(sigPerCode, 1, signalPeriod));
            }
            // Nested `signalSequence` sub-complex instances. Per the FC binding
            // order `signalSequence` is the last sub-attribute of
            // `rhythmOfLight`, so the nested markers/sub-attributes are appended
            // after signalGroup/signalPeriod. The S-101 data provider's scope
            // resolver treats these as nested (rather than sibling) complexes
            // because the FC declares `signalSequence` a sub-attribute of
            // `rhythmOfLight`.
            if (signalSequence is not null)
                AppendSignalSequenceInstances(builder, signalSequence);
        }

        // Emits a date-range complex-attribute instance (marker + optional
        // dateStart + optional dateEnd), using the same flat marker +
        // contiguous-sub-attribute convention as the other complex attributes.
        // The three date-range complexes (fixedDateRange / periodicDateRange /
        // surveyDateRange) share the `dateStart` / `dateEnd` sub-attribute
        // codes; the S-101 data provider delimits one instance from the next
        // by the complex marker, so emitting each instance as a contiguous run
        // keeps them distinct.
        private void AppendDateRangeInstance(
            List<S101Attribute> builder,
            string complexCode,
            string? dateStart,
            string? dateEnd)
        {
            var rangeCode = GetOrAssignAttributeCode(complexCode);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(rangeCode, 1, string.Empty));
            if (dateStart is not null)
            {
                var startCode = GetOrAssignAttributeCode(S101AttrDateStart);
                builder.Add(new S101Attribute(startCode, 1, dateStart));
            }
            if (dateEnd is not null)
            {
                var endCode = GetOrAssignAttributeCode(S101AttrDateEnd);
                builder.Add(new S101Attribute(endCode, 1, dateEnd));
            }
        }

        // Emits a `zoneOfConfidence` complex-attribute instance (marker +
        // categoryOfZoneOfConfidenceInData) using the same marker /
        // contiguous-sub-attribute convention as the other complex attributes.
        private void AppendZoneOfConfidenceInstance(
            List<S101Attribute> builder,
            string categoryOfZoneOfConfidenceInData)
        {
            var zocCode = GetOrAssignAttributeCode(S101AttrZoneOfConfidence);
            // Marker entry — Index=1, value=empty — followed by the sub-attribute.
            builder.Add(new S101Attribute(zocCode, 1, string.Empty));
            var catCode = GetOrAssignAttributeCode(S101AttrCategoryOfZocInData);
            builder.Add(new S101Attribute(catCode, 1, categoryOfZoneOfConfidenceInData));
        }

        // Emits zero or more `surfaceCharacteristics` complex-attribute
        // instances by pairing the S-57 NATSUR and NATQUA lists positionally.
        // Each is a flat marker + contiguous-sub-attribute run (the same
        // convention as the other complex attributes); the S-101 data provider
        // delimits one repeating instance from the next by the complex marker.
        // Out-of-range enumerate codes are dropped (and reported) individually,
        // and a position whose sub-attributes are all dropped emits no instance.
        private void AppendSurfaceCharacteristicsInstances(
            List<S101Attribute> builder,
            string? natsurList,
            string? natquaList)
        {
            var surfaces = SplitEnumList(natsurList);
            var quals = SplitEnumList(natquaList);
            int count = Math.Max(surfaces.Count, quals.Count);
            for (int i = 0; i < count; i++)
            {
                string? surface = i < surfaces.Count ? surfaces[i] : null;
                string? qual = i < quals.Count ? quals[i] : null;

                if (surface is not null
                    && _allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrNatureOfSurface, surface))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrNatureOfSurface, surface);
                    surface = null;
                }
                if (qual is not null
                    && _allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrNatureOfSurfaceQualifyingTerms, qual))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrNatureOfSurfaceQualifyingTerms, qual);
                    qual = null;
                }

                if (surface is null && qual is null)
                    continue;

                var scCode = GetOrAssignAttributeCode(S101AttrSurfaceCharacteristics);
                // Marker entry — Index=1, value=empty — followed by sub-attributes.
                builder.Add(new S101Attribute(scCode, 1, string.Empty));
                if (surface is not null)
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrNatureOfSurface), 1, surface));
                if (qual is not null)
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrNatureOfSurfaceQualifyingTerms), 1, qual));
            }
        }

        // Splits an S-57 list-valued enumerate attribute (comma-separated
        // integer codes) into its individual non-empty tokens.
        private static List<string> SplitEnumList(string? list)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(list))
                return result;
            foreach (var token in list.Split(','))
            {
                var code = token.Trim();
                if (code.Length > 0)
                    result.Add(code);
            }
            return result;
        }

        // Emits zero or more `signalSequence` complex-attribute instances by
        // parsing the S-57 SIGSEQ string (S-57 Appendix B.1). Each parsed
        // phase becomes a flat marker + contiguous sub-attribute run (marker +
        // signalDuration + signalStatus), the same convention as the other
        // complex attributes; when appended after a `rhythmOfLight` instance's
        // simple sub-attributes these form nested sub-complexes of that
        // instance. Phases that do not parse as a real duration are dropped
        // (and reported).
        private void AppendSignalSequenceInstances(List<S101Attribute> builder, string sigseq)
        {
            foreach (var (duration, status) in ParseSignalSequence(sigseq))
            {
                var seqCode = GetOrAssignAttributeCode(S101AttrSignalSequence);
                // Marker entry — Index=1, value=empty — followed by sub-attributes.
                builder.Add(new S101Attribute(seqCode, 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSignalDuration), 1, duration));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSignalStatus), 1, status));
            }
        }

        // Parses an S-57 SIGSEQ value into (signalDuration, signalStatus)
        // pairs. The value is a '+'-separated list of phase durations in
        // seconds (e.g. "02.0+(02.0)+02.0+(24.0)"); a duration enclosed in
        // parentheses is an eclipse / silence phase (signalStatus = 2), an
        // unparenthesised one is a lit / sound phase (signalStatus = 1). The
        // duration is normalised to an invariant-culture real. Tokens that do
        // not parse as a real are skipped and recorded as a rule-dropped
        // attribute.
        private IEnumerable<(string Duration, string Status)> ParseSignalSequence(string sigseq)
        {
            foreach (var rawToken in sigseq.Split('+'))
            {
                var token = rawToken.Trim();
                if (token.Length == 0)
                    continue;

                var status = SignalStatusLit;
                if (token.StartsWith('(') && token.EndsWith(')'))
                {
                    status = SignalStatusEclipsed;
                    token = token[1..^1].Trim();
                }

                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrSigseq);
                    continue;
                }

                yield return (seconds.ToString(CultureInfo.InvariantCulture), status);
            }
        }

        private IReadOnlyList<S101SpatialAssociation> TranslateSpatialPointers(EncDotNet.S57.S57FeatureRecord feat)
        {
            // S57GeometricPrimitive struct overlays an int (1=Point, 2=Line,
            // 3=Area, 255=None) so casting to int recovers the wire value.
            var prim = (int)feat.Primitive;
            return prim switch
            {
                1 => TranslatePointSpatial(feat),
                2 => TranslateLineSpatial(feat),
                3 => TranslateAreaSpatial(feat),
                _ => [],
            };
        }

        private IReadOnlyList<S101SpatialAssociation> TranslatePointSpatial(EncDotNet.S57.S57FeatureRecord feat)
        {
            // Point features reference a single isolated/connected node.
            foreach (var ptr in feat.SpatialPointers)
            {
                if (TryGetPointId(ptr.Name, out var pid))
                {
                    return [new S101SpatialAssociation(S101RcnmPoint, pid, OrientationForward)];
                }
            }
            return [];
        }

        private IReadOnlyList<S101SpatialAssociation> TranslateLineSpatial(EncDotNet.S57.S57FeatureRecord feat)
        {
            // Line features reference one or more edges in traversal order.
            var builder = new List<S101SpatialAssociation>();
            foreach (var ptr in feat.SpatialPointers)
            {
                if (ptr.Name.RecordNameCode != S57RecordNameCodes.Edge) continue;
                if (!_edgeIdMap.TryGetValue(ptr.Name.RecordId, out var cid)) continue;
                var ornt = (int)ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                builder.Add(new S101SpatialAssociation(S101RcnmCurveSegment, cid, ornt));
            }
            return builder;
        }

        private IReadOnlyList<S101SpatialAssociation> TranslateAreaSpatial(EncDotNet.S57.S57FeatureRecord feat)
        {
            // Area features reference a ring of edges via FSPT. Group by
            // USAG (1 = exterior, 2 = interior) and wrap each group into a
            // composite curve referenced from a synthesised surface record.
            var exterior = new List<S101CurveUsage>();
            var interiors = new List<List<S101CurveUsage>>();
            List<S101CurveUsage>? currentInterior = null;

            foreach (var ptr in feat.SpatialPointers)
            {
                if (ptr.Name.RecordNameCode != S57RecordNameCodes.Edge) continue;
                if (!_edgeIdMap.TryGetValue(ptr.Name.RecordId, out var cid)) continue;
                var ornt = (int)ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                var usage = new S101CurveUsage(S101RcnmCurveSegment, cid, ornt);

                switch ((int)ptr.Usage)
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
            if (exterior.Count == 0) return [];

            var rings = new List<S101RingAssociation>();

            // Exterior ring as one composite curve.
            var extId = _nextCompositeId++;
            CompositeCurves[extId] = new S101CompositeCurveRecord
            {
                RecordId = extId,
                CurveComponents = exterior,
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
                    CurveComponents = interior.ToArray(),
                };
                rings.Add(new S101RingAssociation(
                    S101RcnmCompositeCurve, intId, OrientationForward, UsageInterior));
            }

            var sid = _nextSurfaceId++;
            Surfaces[sid] = new S101SurfaceRecord
            {
                RecordId = sid,
                RingAssociations = rings,
            };

            return [new S101SpatialAssociation(S101RcnmSurface, sid, OrientationForward)];
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
