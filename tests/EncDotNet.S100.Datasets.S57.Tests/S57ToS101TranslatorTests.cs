using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Datasets.S57.Tests;

public class S57ToS101TranslatorTests
{
    // S-57 record-name codes; mirrored from EncDotNet.S57.S57RecordNameCodes
    // so existing tests can use the short names without prefixing.
    private const byte RcnmIsolatedNode = 110;
    private const byte RcnmConnectedNode = 120;
    private const byte RcnmEdge = 130;

    // ── Builders that produce package S-57 types from primitive args ───

    private static EncDotNet.S57.S57Document BuildDocument(
        IEnumerable<EncDotNet.S57.S57VectorRecord>? vectorRecords = null,
        IEnumerable<EncDotNet.S57.S57FeatureRecord>? features = null,
        uint comf = 10_000_000,
        uint somf = 10)
        => new()
        {
            DataSetIdentification = new EncDotNet.S57.S57DataSetIdentification
            {
                DataSetName = "TEST.000",
                EditionNumber = "1",
                UpdateNumber = "0",
                IssueDate = "20240101",
            },
            DataSetParameters = new EncDotNet.S57.S57DataSetParameters
            {
                CompilationScale = 50_000,
                CoordinateMultiplicationFactor = (int)comf,
                SoundingMultiplicationFactor = (int)somf,
            },
            VectorRecords = (vectorRecords ?? Array.Empty<EncDotNet.S57.S57VectorRecord>()).ToArray(),
            FeatureRecords = (features ?? Array.Empty<EncDotNet.S57.S57FeatureRecord>()).ToArray(),
        };

    private static EncDotNet.S57.S57RecordName Name(byte rcnm, uint id)
        => new() { RecordNameCode = rcnm, RecordId = (int)id };

    private static EncDotNet.S57.S57VectorRecord Node(uint id, int y, int x, byte rcnm = RcnmConnectedNode)
        => new()
        {
            RecordName = Name(rcnm, id),
            VectorPointers = [],
            Coordinates2D = [
                new EncDotNet.S57.S57Coordinate2D { X = x, Y = y }],
            Soundings = [],
            Attributes = [],
        };

    private static EncDotNet.S57.S57VectorRecord Edge(
        uint id, uint beginNodeId, uint endNodeId,
        params (int Y, int X)[] intermediates)
        => new()
        {
            RecordName = Name(RcnmEdge, id),
            VectorPointers = [
                Vp(RcnmConnectedNode, beginNodeId, ornt: 1, usage: 0, topo: 1, mask: 255),
                Vp(RcnmConnectedNode, endNodeId,   ornt: 1, usage: 0, topo: 2, mask: 255)],
            Coordinates2D = intermediates
                .Select(c => new EncDotNet.S57.S57Coordinate2D { X = c.X, Y = c.Y })
                .ToArray(),
            Soundings = [],
            Attributes = [],
        };

    private static EncDotNet.S57.S57VectorRecord SoundingNode(
        uint id, params (int Y, int X, int Z)[] soundings)
        => new()
        {
            RecordName = Name(RcnmIsolatedNode, id),
            VectorPointers = [],
            Coordinates2D = [],
            Soundings = soundings
                .Select(s => new EncDotNet.S57.S57Sounding { X = s.X, Y = s.Y, Depth = s.Z })
                .ToArray(),
            Attributes = [],
        };

    private static EncDotNet.S57.S57VectorPointer Vp(
        byte rcnm, uint id, byte ornt, byte usage, byte topo, byte mask)
        => new()
        {
            Name = Name(rcnm, id),
            Orientation = (EncDotNet.S57.S57Orientation)(int)ornt,
            Usage = (EncDotNet.S57.S57UsageIndicator)(int)usage,
            Topology = (EncDotNet.S57.S57TopologyIndicator)(int)topo,
            Mask = (EncDotNet.S57.S57MaskingIndicator)(int)mask,
        };

    private static EncDotNet.S57.S57SpatialPointer Sp(
        byte rcnm, uint id, byte ornt, byte usage, byte mask)
        => new()
        {
            Name = Name(rcnm, id),
            Orientation = (EncDotNet.S57.S57Orientation)(int)ornt,
            Usage = (EncDotNet.S57.S57UsageIndicator)(int)usage,
            Mask = (EncDotNet.S57.S57MaskingIndicator)(int)mask,
        };

    private static EncDotNet.S57.S57AttributeValue Attr(int code, string value)
        => new() { AttributeCode = code, Value = value };

    private static EncDotNet.S57.S57FeatureRecord Feat(
        uint recordId,
        byte primitive,
        ushort objectClass,
        ushort producingAgency = 540,
        uint featureIdentificationNumber = 1,
        ushort featureIdentificationSubdivision = 0,
        IEnumerable<EncDotNet.S57.S57AttributeValue>? attributes = null,
        IEnumerable<EncDotNet.S57.S57SpatialPointer>? spatialPointers = null,
        IEnumerable<EncDotNet.S57.S57FeaturePointer>? featurePointers = null)
        => new()
        {
            RecordName = new EncDotNet.S57.S57RecordName
            {
                RecordNameCode = 100, // Feature
                RecordId = (int)recordId,
                AgencyCode = (int)producingAgency,
                FeatureId = (int)featureIdentificationNumber,
                FeatureSubdivision = (int)featureIdentificationSubdivision,
            },
            Primitive = (EncDotNet.S57.S57GeometricPrimitive)(int)primitive,
            ObjectCode = (EncDotNet.S57.S57ObjectCode)(int)objectClass,
            Attributes = (attributes ?? Array.Empty<EncDotNet.S57.S57AttributeValue>()).ToArray(),
            NationalAttributes = [],
            SpatialPointers = (spatialPointers ?? Array.Empty<EncDotNet.S57.S57SpatialPointer>()).ToArray(),
            FeaturePointers = (featurePointers ?? Array.Empty<EncDotNet.S57.S57FeaturePointer>()).ToArray(),
        };

    // Builds an FFPT feature-to-feature pointer referencing a target feature by
    // its S-57 long name (LNAM: agency, feature id, subdivision). Mirrors real
    // S-57 where FFPT names carry rcnm=0/rcid=0 and identify the target by LNAM.
    private static EncDotNet.S57.S57FeaturePointer Ffpt(
        ushort producingAgency, uint featureIdentificationNumber,
        ushort featureIdentificationSubdivision = 0,
        EncDotNet.S57.S57RelationshipIndicator relationship
            = EncDotNet.S57.S57RelationshipIndicator.Peer)
        => new()
        {
            Name = new EncDotNet.S57.S57RecordName
            {
                RecordNameCode = 0,
                RecordId = 0,
                AgencyCode = (int)producingAgency,
                FeatureId = (int)featureIdentificationNumber,
                FeatureSubdivision = (int)featureIdentificationSubdivision,
            },
            Relationship = relationship,
        };

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Translate_NodeBecomesPointRecord()
    {
        var n1 = Node(1, 100, 200);
        var doc = BuildDocument(vectorRecords: new[] { n1 });

        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Single(s101.Points);
        var pt = s101.Points.Values.Single();
        Assert.Equal(100, pt.Y);
        Assert.Equal(200, pt.X);
    }

    [Fact]
    public void Translate_EdgeBecomesCurveSegmentWithBeginEndAssociations()
    {
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 100, 100);
        var e1 = Edge(10, 1, 2, (50, 50));

        var doc = BuildDocument(vectorRecords: new[] { n1, n2, e1 });
        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Equal(2, s101.Points.Count);
        var cs = Assert.Single(s101.CurveSegments.Values);
        Assert.Equal(2, cs.PointAssociations.Count);
        Assert.Equal(1, cs.PointAssociations[0].Topology);
        Assert.Equal(2, cs.PointAssociations[1].Topology);
        Assert.Equal((50, 50), cs.IntermediateCoordinates[0]);
    }

    [Fact]
    public void Translate_PointFeature_ReferencesPoint()
    {
        var n1 = Node(1, 100, 200);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 5, // BCNCAR → CardinalBeacon
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });

        var doc = BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("CardinalBeacon", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(110, sa.RecordName);
    }

    [Fact]
    public void Translate_LineFeature_ReferencesCurveSegments()
    {
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 100, 100);
        var e1 = Edge(10, 1, 2);
        var feature = Feat(
            recordId: 1, primitive: 2, objectClass: 30, // COALNE → Coastline
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 0, 0) });

        var doc = BuildDocument(vectorRecords: new[] { n1, n2, e1 }, features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Coastline", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(120, sa.RecordName);
    }

    [Fact]
    public void Translate_MCovr_CoverageAvailable_BecomesDataCoverage()
    {
        // M_COVR (OBJL 302) with CATCOV = 1 (coverage available) converts to a
        // DataCoverage feature (S-57 → S-101 Conversion Guidance).
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 0, 100);
        var n3 = Node(3, 100, 50);
        var e1 = Edge(10, 1, 2);
        var e2 = Edge(11, 2, 3);
        var e3 = Edge(12, 3, 1);

        var feature = Feat(
            recordId: 1, primitive: 3, objectClass: 302, // M_COVR
            attributes: new[] { Attr(18, "1") },          // CATCOV = coverage available
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });

        var doc = BuildDocument(
            vectorRecords: new[] { n1, n2, n3, e1, e2, e3 },
            features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("DataCoverage", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
    }

    [Fact]
    public void Translate_MCovr_NoCoverageAvailable_IsDropped()
    {
        // M_COVR (OBJL 302) with CATCOV = 2 (no coverage available) has no
        // S-101 equivalent — S-101 represents the absence of data by the
        // absence of a DataCoverage feature. Emitting it as DataCoverage would
        // falsely assert coverage over the cell's no-data region and drive
        // cross-cell overlap suppression to blank the coarser overlapping cell
        // there (issue #438). It must be dropped.
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 0, 100);
        var n3 = Node(3, 100, 50);
        var e1 = Edge(10, 1, 2);
        var e2 = Edge(11, 2, 3);
        var e3 = Edge(12, 3, 1);

        var feature = Feat(
            recordId: 1, primitive: 3, objectClass: 302, // M_COVR
            attributes: new[] { Attr(18, "2") },          // CATCOV = no coverage available
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });

        var doc = BuildDocument(
            vectorRecords: new[] { n1, n2, n3, e1, e2, e3 },
            features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Empty(s101.Features);
    }

    [Fact]
    public void Translate_MCovr_MixedCoverage_KeepsOnlyCoverageAvailable()
    {
        // A cell commonly carries both a coverage-available M_COVR (CATCOV = 1)
        // and a no-coverage M_COVR (CATCOV = 2). Only the former survives.
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 0, 100);
        var n3 = Node(3, 100, 50);
        var e1 = Edge(10, 1, 2);
        var e2 = Edge(11, 2, 3);
        var e3 = Edge(12, 3, 1);

        var available = Feat(
            recordId: 1, primitive: 3, objectClass: 302,
            featureIdentificationNumber: 1,
            attributes: new[] { Attr(18, "1") },
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });
        var noCoverage = Feat(
            recordId: 2, primitive: 3, objectClass: 302,
            featureIdentificationNumber: 2,
            attributes: new[] { Attr(18, "2") },
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });

        var doc = BuildDocument(
            vectorRecords: new[] { n1, n2, n3, e1, e2, e3 },
            features: new[] { available, noCoverage });
        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("DataCoverage", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
    }

    [Fact]
    public void Translate_AreaFeature_BuildsSurfaceWithCompositeCurveExterior()
    {
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 0, 100);
        var n3 = Node(3, 100, 50);
        var e1 = Edge(10, 1, 2);
        var e2 = Edge(11, 2, 3);
        var e3 = Edge(12, 3, 1);

        var feature = Feat(
            recordId: 1, primitive: 3, objectClass: 42, // DEPARE → DepthArea
            attributes: new[] { Attr(87, "10"), Attr(88, "20") },
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });

        var doc = BuildDocument(
            vectorRecords: new[] { n1, n2, n3, e1, e2, e3 },
            features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("DepthArea", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Equal(2, feat.Attributes.Count);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(130, sa.RecordName);

        var surface = s101.Surfaces[sa.RecordId];
        var ring = Assert.Single(surface.RingAssociations);
        Assert.Equal(1, ring.Usage);
        Assert.Equal(125, ring.RecordName);

        var composite = s101.CompositeCurves[ring.RecordId];
        Assert.Equal(3, composite.CurveComponents.Count);
    }

    [Fact]
    public void Translate_SoundingFeature_BecomesMultiPointSounding()
    {
        // S-57 SOUNDG (OBJL=129) features are translated into a single S-101
        // Sounding feature backed by a multi-point spatial record (RCNM=115).
        var sn = SoundingNode(1, (10, 20, 50), (30, 40, 75), (50, 60, 100));
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 129, // SOUNDG
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 1, 1, 0, 0) });

        var doc = BuildDocument(vectorRecords: new[] { sn }, features: new[] { feature }, somf: 10);
        var s101 = new S57ToS101Translator().Translate(doc);

        var s101Feature = Assert.Single(s101.Features);
        var soundingTypeCode = s101.FeatureTypeCatalogue.First(kv => kv.Value == "Sounding").Key;
        Assert.Equal(soundingTypeCode, s101Feature.FeatureTypeCode);
        Assert.Empty(s101Feature.Attributes);

        var spa = Assert.Single(s101Feature.SpatialAssociations);
        Assert.Equal((byte)115, spa.RecordName);

        var mp = Assert.Single(s101.MultiPoints.Values);
        Assert.Equal(spa.RecordId, mp.RecordId);
        Assert.Equal(3, mp.Points.Count);
        Assert.Equal((10, 20, 50), mp.Points[0]);
        Assert.Equal((30, 40, 75), mp.Points[1]);
        Assert.Equal((50, 60, 100), mp.Points[2]);

        // Soundings must not pollute the Point record table — only the
        // MultiPoint record is emitted for them.
        Assert.Empty(s101.Points);

        // CMFZ defaults to SOMF (10) so consumers can recover real depth.
        Assert.Equal(10u, s101.StructureInfo.CoordinateMultiplicationFactorZ);
    }

    [Fact]
    public void Translate_SoundingFeature_AcrossMultipleNodes_AggregatesAllPoints()
    {
        var sn1 = SoundingNode(1, (1, 2, 3), (4, 5, 6));
        var sn2 = SoundingNode(2, (7, 8, 9));
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 129,
            spatialPointers: new[]
            {
                Sp(RcnmIsolatedNode, 1, 1, 0, 0),
                Sp(RcnmIsolatedNode, 2, 1, 0, 0),
            });

        var doc = BuildDocument(vectorRecords: new[] { sn1, sn2 }, features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        var mp = Assert.Single(s101.MultiPoints.Values);
        Assert.Equal(3, mp.Points.Count);
    }

    [Fact]
    public void Translate_UnmappedFeatureClass_IsSkipped()
    {
        var feature = Feat(recordId: 1, primitive: 1, objectClass: 65535);
        var doc = BuildDocument(features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Empty(s101.Features);
    }

    [Fact]
    public void Translate_DocumentMetadataIsSet()
    {
        var doc = BuildDocument(comf: 5_000_000, somf: 100);
        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Equal("S-101", s101.Identification.ProductSpecification);
        Assert.Equal("TEST.000", s101.Identification.DatasetName);
        Assert.Equal(5_000_000u, s101.StructureInfo.CoordinateMultiplicationFactorX);
        Assert.Equal(5_000_000u, s101.StructureInfo.CoordinateMultiplicationFactorY);
        Assert.Equal(100u, s101.StructureInfo.CoordinateMultiplicationFactorZ);
    }

    // ── v3.5: S-101 FC allowable enum-value enforcement ──────────────

    private static EncDotNet.S57.S57Document LandRegionDocWithCatlnd(string catlndValue)
    {
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 73, // LNDRGN → LandRegion
            attributes: new[] { Attr(34, catlndValue) }, // CATLND → categoryOfLandRegion (enum)
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        return BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });
    }

    [Fact]
    public void Translate_EnumAttribute_AllowedValue_IsEmitted()
    {
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("1"));

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("1", attr.Value);
        var attrName = s101.AttributeTypeCatalogue[attr.NumericCode];
        Assert.Equal("categoryOfLandRegion", attrName);
    }

    [Fact]
    public void Translate_EnumAttribute_DisallowedValue_IsDropped()
    {
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("99"));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_EnumAttribute_DisallowedValue_PassesThroughWhenEnforcementDisabled()
    {
        var translator = new S57ToS101Translator(S57S101Mapping.Default, allowedEnumValues: null);
        var s101 = translator.Translate(LandRegionDocWithCatlnd("99"));

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("99", attr.Value);
    }

    [Fact]
    public void Translate_NonEnumAttribute_PassesThroughRegardlessOfValue()
    {
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 0, 100);
        var n3 = Node(3, 100, 50);
        var e1 = Edge(10, 1, 2);
        var e2 = Edge(11, 2, 3);
        var e3 = Edge(12, 3, 1);

        var feature = Feat(
            recordId: 1, primitive: 3, objectClass: 42, // DEPARE → DepthArea
            attributes: new[] { Attr(87, "999.9"), Attr(88, "1234.5") },
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });

        var doc = BuildDocument(
            vectorRecords: new[] { n1, n2, n3, e1, e2, e3 },
            features: new[] { feature });
        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        var values = feat.Attributes.Select(a => a.Value).ToArray();
        Assert.Contains("999.9", values);
        Assert.Contains("1234.5", values);
    }

    // ── Translation target (issue #608) ──────────────────────────────

    // S-401 is used here only because its bundled Feature Catalogue lists
    // restriction value 28, which S-101's does not; the S-57 → S-101 mapping
    // table is reused unchanged, so this exercises only the catalogue swap.
    private static readonly S57TranslationTarget S401Target = new() { Spec = "S-401", Edition = "1.3.0" };

    private static EncDotNet.S57.S57Document RestrictedAreaDocWithRestrn(string restrnValue)
    {
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 112, // RESARE → RestrictedArea
            attributes: new[] { Attr(131, restrnValue) }, // RESTRN → restriction (enum)
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        return BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });
    }

    [Fact]
    public void Translate_DefaultTarget_DeclaresS101()
    {
        var translator = new S57ToS101Translator();

        var s101 = translator.Translate(LandRegionDocWithCatlnd("1"));

        Assert.Same(S57TranslationTarget.S101, translator.Target);
        Assert.Equal("S-101", s101.Identification.ProductSpecification);
        Assert.Equal("1.0.0", s101.Identification.ProductSpecificationEdition);
    }

    [Fact]
    public void Translate_ForTarget_DeclaresTargetProduct()
    {
        var translator = S57ToS101Translator.ForTarget(S401Target, S57S101Mapping.Default);

        var translated = translator.Translate(LandRegionDocWithCatlnd("1"));

        Assert.Same(S401Target, translator.Target);
        Assert.Equal("S-401", translated.Identification.ProductSpecification);
        Assert.Equal("1.3.0", translated.Identification.ProductSpecificationEdition);
    }

    [Fact]
    public void Translate_S101Target_DropsValueOnlyTheS401CatalogueAllows()
    {
        var s101 = new S57ToS101Translator().Translate(RestrictedAreaDocWithRestrn("28"));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_ForTarget_ChecksValuesAgainstTargetCatalogue()
    {
        var translated = S57ToS101Translator.ForTarget(S401Target, S57S101Mapping.Default)
            .Translate(RestrictedAreaDocWithRestrn("28"));

        var feat = Assert.Single(translated.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("restriction", translated.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("28", attr.Value);
    }

    [Fact]
    public void S101AllowedEnumValues_ForSpec_IsSharedPerCatalogue()
    {
        Assert.Same(S101AllowedEnumValues.Default, S101AllowedEnumValues.ForSpec("S-101"));
        Assert.Same(S101AllowedEnumValues.ForSpec("S-401"), S101AllowedEnumValues.ForSpec("s-401"));
        Assert.NotSame(S101AllowedEnumValues.Default, S101AllowedEnumValues.ForSpec("S-401"));
    }

    [Fact]
    public void S101AllowedEnumValues_ForSpec_ReadsThatCatalogue()
    {
        Assert.False(S101AllowedEnumValues.Default.IsAllowed("restriction", "28"));
        Assert.True(S101AllowedEnumValues.ForSpec("S-401").IsAllowed("restriction", "28"));
    }

    [Fact]
    public void S101AllowedEnumValues_ForSpec_UnbundledCatalogue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => S101AllowedEnumValues.ForSpec("S-999"));
        Assert.Throws<ArgumentException>(() => S101AllowedEnumValues.ForSpec(" "));
    }

    [Fact]
    public void S101FeatureAttributeBindings_ForSpec_ReadsThatCatalogue()
    {
        // NoticeMark is an inland feature class that only S-401 defines.
        Assert.Same(S101FeatureAttributeBindings.Default, S101FeatureAttributeBindings.ForSpec("S-101"));
        Assert.False(S101FeatureAttributeBindings.Default.Binds("NoticeMark", "featureName"));
        Assert.True(S101FeatureAttributeBindings.ForSpec("S-401").Binds("NoticeMark", "featureName"));
    }

    [Fact]
    public void Translate_ForS401Target_UsesS401MappingAndDeclaresS401()
    {
        // RAPIDS (107) maps to S-101 Rapids, a class S-401 does not define.
        var n1 = Node(1, 1000, 2000);
        var rapids = Feat(recordId: 1, primitive: 1, objectClass: 107,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1 }, features: new[] { rapids });
        var diag = new S57TranslationDiagnostics();

        var maritime = new S57ToS101Translator().Translate(doc);
        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc, diag);

        Assert.Equal("Rapids", ClassOf(maritime, Assert.Single(maritime.Features)));
        Assert.Empty(inland.Features);
        Assert.Equal(1, diag.RuleDroppedObjectClasses[107]);
        Assert.Equal("S-401", inland.Identification.ProductSpecification);
        Assert.Equal("1.3.0", inland.Identification.ProductSpecificationEdition);
    }

    [Fact]
    public void Translate_InlandNoticeMark_S401TargetEmitsNoticeMark_S101ReportsUnmapped()
    {
        // notmrk (17050) with catnmk (17052) and wtwdis (17064), IENC FC 2.4.
        var n1 = Node(1, 1000, 2000);
        var notmrk = Feat(recordId: 1, primitive: 1, objectClass: 17050,
            attributes: new[] { Attr(17052, "8"), Attr(17064, "13.5") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1 }, features: new[] { notmrk });
        var maritimeDiag = new S57TranslationDiagnostics();

        var maritime = new S57ToS101Translator().Translate(doc, maritimeDiag);
        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        Assert.Empty(maritime.Features);
        Assert.Equal(1, maritimeDiag.UnmappedObjectClasses[17050]);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("NoticeMark", ClassOf(inland, feature));
        var attributes = feature.Attributes.ToDictionary(
            a => inland.AttributeTypeCatalogue[a.NumericCode], a => a.Value);
        Assert.Equal("8", attributes["categoryOfNoticeMark"]);

        // S-401 NoticeMark does not bind waterwayDistance (nor does the IEHG
        // conversion guidance list wtwdis for notmrk, clause 3.90).
        Assert.DoesNotContain("waterwayDistance", attributes.Keys);
    }

    [Fact]
    public void Translate_InlandTwin_S401Target_TranslatesLikeItsStandardClass()
    {
        // slcons (17032) with catslc (17012) and watlev (17104) translates as
        // SLCONS / CATSLC / WATLEV would.
        var n1 = Node(1, 1000, 2000);
        var slcons = Feat(recordId: 1, primitive: 1, objectClass: 17032,
            attributes: new[] { Attr(17012, "2"), Attr(17104, "3") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1 }, features: new[] { slcons });

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("ShorelineConstruction", ClassOf(inland, feature));
        var attributes = feature.Attributes.ToDictionary(
            a => inland.AttributeTypeCatalogue[a.NumericCode], a => a.Value);
        Assert.Equal("2", attributes["categoryOfShorelineConstruction"]);
        Assert.Equal("3", attributes["waterLevelEffect"]);
    }

    [Fact]
    public void Translate_InlandBridge_S401Target_CarriesBridgeCategories()
    {
        // USACE inland bridges (17011) carry the standard CATBRG (9); an
        // inland bridge reuses the BRIDGE rule, so its categories convert too.
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 100, 100);
        var e1 = Edge(10, 1, 2);
        var bridge = Feat(recordId: 1, primitive: 2, objectClass: 17011,
            attributes: new[] { Attr(9, "3") }, // swing bridge
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1, n2, e1 }, features: new[] { bridge });

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("Bridge", ClassOf(inland, feature));
        var attributes = feature.Attributes.ToDictionary(
            a => inland.AttributeTypeCatalogue[a.NumericCode], a => a.Value);
        Assert.Equal("3", attributes["categoryOfOpeningBridge"]);
        Assert.Equal("true", attributes["openingBridge"]);
    }

    [Fact]
    public void Translate_InlandBridgeArch_S401Target_IsAnArchWithAFixedSpan()
    {
        // IEHG "S-57 ENC to S-401 Conversion Guidance" Ed 1.3.0 draft 2,
        // clauses 3.7 and 3.144: CATBRG 13 (bridge arch) becomes
        // bridgeConstruction 1 (arch) and the span is a SpanFixed.
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 100, 100);
        var e1 = Edge(10, 1, 2);
        var bridge = Feat(recordId: 1, primitive: 2, objectClass: 17011,
            attributes: new[] { Attr(9, "13"), Attr(AttlVerclr, "7.1") },
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1, n2, e1 }, features: new[] { bridge });
        var diag = new S57TranslationDiagnostics();

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc, diag);

        var feature = SingleOfClass(inland, "Bridge");
        var attributes = feature.Attributes.ToDictionary(
            a => inland.AttributeTypeCatalogue[a.NumericCode], a => a.Value);
        Assert.Equal("1", attributes["bridgeConstruction"]);
        Assert.Equal("false", attributes["openingBridge"]);
        Assert.DoesNotContain("categoryOfOpeningBridge", attributes.Keys);

        var span = SingleOfClass(inland, "SpanFixed");
        AssertBridgeComponents(inland, feature, span);
        Assert.DoesNotContain(inland.Features, f => ClassOf(inland, f) == "SpanOpening");
        Assert.Empty(diag.DroppedEnumValues);
    }

    [Fact]
    public void Translate_S401Target_ShipDimensionsCarryCategoryOfShipIncluding()
    {
        // lg_sdm (18001) → MaximumPermittedShipDimensions. Conversion guidance
        // clause 3.82 pairs lc_csi (18012) with categoryOfShipIncluding and
        // lc_cse (18013) with categoryOfShipExcluding. Both are S-57 list
        // attributes and both bind [0..*], so every listed value survives.
        // lg_sdm is rare in USACE data, hence the synthetic fixture.
        var doc = AreaFeatureWithS57Attributes(18001,
            Attr(18012, "7,9"),     // lc_csi: inland waterway vessel, motor vessel
            Attr(18013, "8"),       // lc_cse: sea going ship
            Attr(18004, "135.5"));  // lg_lgs: maximal permitted length
        var diag = new S57TranslationDiagnostics();

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc, diag);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("MaximumPermittedShipDimensions", ClassOf(inland, feature));

        var attributes = feature.Attributes
            .Select(a => (Code: inland.AttributeTypeCatalogue[a.NumericCode], a.Index, a.Value))
            .ToList();
        Assert.Equal(
            [("categoryOfShipIncluding", 1, "7"), ("categoryOfShipIncluding", 2, "9")],
            attributes.Where(a => a.Code == "categoryOfShipIncluding"));
        Assert.Contains(("categoryOfShipExcluding", (ushort)1, "8"), attributes);
        Assert.Contains(("maximalPermittedLength", (ushort)1, "135.5"), attributes);

        Assert.False(diag.RuleDroppedAttributes.ContainsKey(18012));
        Assert.Empty(diag.DroppedEnumValues);
    }

    [Fact]
    public void Translate_S401Target_DropsInlandAttributeWithoutS401Equivalent()
    {
        // CLSNAM (18028) names a NEWOBJ class; S-401 has no equivalent.
        var doc = PointFeatureWithS57Attributes(17004, Attr(18028, "x"), Attr(17064, "11"));
        var diag = new S57TranslationDiagnostics();

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc, diag);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("DistanceMark", ClassOf(inland, feature));
        Assert.Equal("waterwayDistance", inland.AttributeTypeCatalogue[Assert.Single(feature.Attributes).NumericCode]);
        Assert.Equal(1, diag.RuleDroppedAttributes[18028]);
    }

    [Theory]
    // hunits → distanceUnitOfMeasurement, conversion guidance §2.1.4 (table 2.3).
    [InlineData("1", "1")] // metres
    [InlineData("3", "3")] // kilometres
    [InlineData("4", "7")] // hectometres
    [InlineData("5", "4")] // statute miles
    [InlineData("6", "5")] // nautical miles
    public void Translate_S401Target_HunitsBecomesDistanceUnitOfMeasurement(string hunits, string expected)
    {
        var doc = PointFeatureWithS57Attributes(17004, Attr(17103, hunits), Attr(17064, "11"));

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("DistanceMark", ClassOf(inland, feature));
        var attributes = feature.Attributes.ToDictionary(
            a => inland.AttributeTypeCatalogue[a.NumericCode], a => a.Value);
        Assert.Equal(expected, attributes["distanceUnitOfMeasurement"]);
        Assert.Equal("11", attributes["waterwayDistance"]);
    }

    [Fact]
    public void Translate_S401Target_HunitsFeet_IsDropped()
    {
        // S-401 has no feet; its code 2 means yards, so the value must not pass through.
        var doc = LineFeatureWithS57Attributes(17012, Attr(17101, "3"), Attr(17103, "2"), Attr(17064, "11"));
        var diag = new S57TranslationDiagnostics();

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc, diag);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("CableOverhead", ClassOf(inland, feature));
        Assert.DoesNotContain(feature.Attributes,
            a => inland.AttributeTypeCatalogue[a.NumericCode] == "distanceUnitOfMeasurement");
        Assert.Equal(1, diag.RuleDroppedAttributes[17103]);
    }

    [Fact]
    public void Translate_S401Target_HunitsOnClassNotBindingTheUnit_IsDropped()
    {
        // S-401 NoticeMark does not bind distanceUnitOfMeasurement, so hunits has no conformant home there.
        Assert.False(S101FeatureAttributeBindings.ForSpec("S-401").Binds("NoticeMark", "distanceUnitOfMeasurement"));
        var doc = PointFeatureWithS57Attributes(17050, Attr(17052, "8"), Attr(17103, "5"));
        var diag = new S57TranslationDiagnostics();

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc, diag);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("NoticeMark", ClassOf(inland, feature));
        Assert.Equal("categoryOfNoticeMark", inland.AttributeTypeCatalogue[Assert.Single(feature.Attributes).NumericCode]);
        Assert.Equal(1, diag.RuleDroppedAttributes[17103]);
    }

    // ── S-401 anchorage rules (IEHG conversion guidance 3.3, 3.4, 3.85) ──

    // Every value of a simple attribute, in emission order, keyed by S-101 name.
    private static Dictionary<string, List<string>> AttributeValues(S101Document doc, S101FeatureRecord feat)
        => feat.Attributes
            .GroupBy(a => doc.AttributeTypeCatalogue[a.NumericCode])
            .ToDictionary(g => g.Key, g => g.Select(a => a.Value).ToList());

    // ── M_NSYS with ORIENT → LocalDirectionOfBuoyage (S-65 Annex B § 12.2) ──

    [Fact]
    public void Translate_NavigationalSystemOfMarksWithOrient_BecomesLocalDirectionOfBuoyage()
    {
        var doc = AreaFeatureWithS57Attributes(306, Attr(109, "1"), Attr(117, "135"));

        var s101 = new S57ToS101Translator().Translate(doc);

        var feature = Assert.Single(s101.Features);
        Assert.Equal("LocalDirectionOfBuoyage", ClassOf(s101, feature));
        var values = AttributeValues(s101, feature);
        Assert.Equal(["1"], values["marksNavigationalSystemOf"]);
        Assert.Equal(["135"], values["orientationValue"]);
    }

    [Fact]
    public void Translate_NavigationalSystemOfMarksWithEmptyOrient_StaysAndDropsOrient()
    {
        // NOAA US6OH09M encodes an empty (unknown) ORIENT on its M_NSYS.
        var doc = AreaFeatureWithS57Attributes(306, Attr(109, "2"), Attr(117, ""));
        var diagnostics = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(doc, diagnostics);

        var feature = Assert.Single(s101.Features);
        Assert.Equal("NavigationalSystemOfMarks", ClassOf(s101, feature));
        Assert.Equal(["marksNavigationalSystemOf"], AttributeNames(s101, feature));
        Assert.Equal(1, diagnostics.RuleDroppedAttributes[117]);
    }

    [Theory]
    [InlineData(17018)] // inland m_nsys
    [InlineData(306)]   // maritime-coded M_NSYS in an inland cell
    public void Translate_NavigationalSystemOfMarksWithOrient_S401Target_BecomesLocalDirectionOfBuoyage(int objl)
    {
        var doc = AreaFeatureWithS57Attributes((ushort)objl, Attr(17009, "11"), Attr(117, "90"));

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("LocalDirectionOfBuoyage", ClassOf(inland, feature));
        var values = AttributeValues(inland, feature);
        Assert.Equal(["11"], values["marksNavigationalSystemOf"]);
        Assert.Equal(["90"], values["orientationValue"]);
    }

    [Theory]
    [InlineData(17001, 17000)] // inland achare / catach
    [InlineData(4, 8)]         // maritime-coded ACHARE / CATACH in an inland cell
    public void Translate_AnchorageAreaSmallCraftMooring_S401Target_BecomesMooringArea(int objl, int attl)
    {
        var doc = AreaFeatureWithS57Attributes((ushort)objl, Attr(attl, "8"), Attr(116, "Visitor moorings"));

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("MooringArea", ClassOf(inland, feature));
        var values = AttributeValues(inland, feature);
        Assert.Equal(["1"], values["categoryOfMooringArea"]);
        Assert.False(values.ContainsKey("categoryOfAnchorage"));
    }

    [Fact]
    public void Translate_AnchorageAreaSmallCraftMooring_S101Target_StaysAnchorageArea()
    {
        var doc = AreaFeatureWithS57Attributes(4, Attr(8, "8"));

        var maritime = new S57ToS101Translator().Translate(doc);

        var feature = Assert.Single(maritime.Features);
        Assert.Equal("AnchorageArea", ClassOf(maritime, feature));
        // S-101 categoryOfAnchorage has no value 8, so the value itself is dropped.
        Assert.DoesNotContain("categoryOfMooringArea", AttributeNames(maritime, feature));
    }

    [Theory]
    [InlineData(17001, 17000)]
    [InlineData(4, 8)]
    public void Translate_AnchorageAreaPushingNavigation_S401Target_Remaps10To16(int objl, int attl)
    {
        var doc = AreaFeatureWithS57Attributes((ushort)objl, Attr(attl, "10"));

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("AnchorageArea", ClassOf(inland, feature));
        Assert.Equal(["16"], AttributeValues(inland, feature)["categoryOfAnchorage"]);
    }

    [Theory]
    // Unrestricted, deep-water and tanker anchorages stay anchorages, despite
    // the "catach=1, 2, 3" in the title of guidance clause 3.85.
    [InlineData("1", new[] { "1" })]
    [InlineData("2", new[] { "2" })]
    [InlineData("3", new[] { "3" })]
    // A list redirects only when it is a lone 8; otherwise every code is kept
    // on the anchorage, since S-401 categoryOfAnchorage binds 8 too.
    [InlineData("7,8", new[] { "7", "8" })]
    [InlineData("8,10", new[] { "8", "16" })]
    public void Translate_InlandAnchorageArea_S401Target_StaysAnchorageArea(string catach, string[] expected)
    {
        var doc = AreaFeatureWithS57Attributes(17001, Attr(17000, catach));

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("AnchorageArea", ClassOf(inland, feature));
        Assert.Equal(expected, AttributeValues(inland, feature)["categoryOfAnchorage"]);
    }

    [Fact]
    public void Translate_InlandAnchorBerthPushingNavigation_S401Target_Remaps10To16()
    {
        // Clause 3.4 applies the same catach 10 → 16 rule to anchor berths.
        var doc = PointFeatureWithS57Attributes(17000, Attr(17000, "10"));

        var inland = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(doc);

        var feature = Assert.Single(inland.Features);
        Assert.Equal("AnchorBerth", ClassOf(inland, feature));
        Assert.Equal(["16"], AttributeValues(inland, feature)["categoryOfAnchorage"]);
    }

    [Theory]
    [InlineData("S-101", "LightAllAround", true)]
    [InlineData("S-401", "LightAllAround", true)]
    [InlineData("S-101", "LightFogDetector", true)]
    [InlineData("S-401", "LightFogDetector", false)] // S-401 defines no LightFogDetector
    [InlineData("S-101", "NoticeMark", false)]       // S-101 defines no NoticeMark
    [InlineData("S-401", "NoticeMark", true)]
    public void S101FeatureAttributeBindings_BindsFeatureAssociation_FollowsCatalogue(
        string spec, string equipment, bool expected)
    {
        var bindings = S101FeatureAttributeBindings.ForSpec(spec);

        Assert.Equal(expected, bindings.BindsFeatureAssociation("Bridge", "StructureEquipment", "theEquipment", equipment));
        Assert.False(bindings.BindsFeatureAssociation("Bridge", "StructureEquipment", "theStructure", equipment));
        Assert.False(bindings.BindsFeatureAssociation(null, "StructureEquipment", "theEquipment", equipment));
    }

    [Fact]
    public void S101FeatureAttributeBindings_DefinesFeatureType_FollowsCatalogue()
    {
        var s101 = S101FeatureAttributeBindings.Default;
        var s401 = S101FeatureAttributeBindings.ForSpec("S-401");

        Assert.True(s101.DefinesFeatureType("RangeSystem"));
        Assert.False(s401.DefinesFeatureType("RangeSystem"));
        Assert.False(s101.DefinesFeatureType("NoticeMark"));
        Assert.True(s401.DefinesFeatureType("NoticeMark"));
        Assert.True(s401.DefinesFeatureType("LandArea"));
        Assert.False(s101.DefinesFeatureType("landarea"));
        Assert.False(s101.DefinesFeatureType(null));
        Assert.False(s101.DefinesFeatureType(""));
    }

    [Fact]
    public void ForTarget_NullTarget_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => S57ToS101Translator.ForTarget(null!, S57S101Mapping.Default));
    }

    [Fact]
    public void S101AllowedEnumValues_Default_KnowsCommonEnumeratedAttributes()
    {
        var allowed = S101AllowedEnumValues.Default;

        Assert.True(allowed.IsEnumerated("categoryOfLandRegion"));
        Assert.True(allowed.IsAllowed("categoryOfLandRegion", "1"));
        Assert.False(allowed.IsAllowed("categoryOfLandRegion", "99"));

        Assert.False(allowed.IsEnumerated("depthRangeMinimumValue"));
        Assert.True(allowed.IsAllowed("depthRangeMinimumValue", "anything"));

        Assert.True(allowed.IsAllowed("totallyMadeUpAttribute", "x"));
    }

    // ── List-valued enum attributes: comma-separated S-57 codes are split
    //    into one S-101 occurrence per value (not dropped wholesale). ──────

    [Fact]
    public void Translate_ListEnumAttribute_SplitsCommaSeparatedValues()
    {
        // CATLND (list type) → categoryOfLandRegion; "1,3" is two valid codes.
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("1,3"));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        Assert.All(feat.Attributes, a =>
            Assert.Equal("categoryOfLandRegion", s101.AttributeTypeCatalogue[a.NumericCode]));
        var values = feat.Attributes.Select(a => a.Value).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "1", "3" }, values);
        // Each occurrence carries a distinct ATIX (1-based).
        Assert.Equal(new ushort[] { 1, 2 }, feat.Attributes.Select(a => a.Index).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Translate_ListEnumAttribute_DropsOnlyInvalidCodes_KeepsValidOnes()
    {
        // "3,99,5": 99 is not an allowable categoryOfLandRegion code.
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("3,99,5"));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        var values = feat.Attributes.Select(a => a.Value).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "3", "5" }, values);
    }

    [Fact]
    public void Translate_ListEnumAttribute_PreservesDuplicateCodes()
    {
        // "3,3" is a real corpus pattern; both occurrences are preserved.
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("3,3"));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        Assert.All(feat.Attributes, a => Assert.Equal("3", a.Value));
    }

    [Fact]
    public void Translate_ListEnumAttribute_AllInvalidCodes_EmitsNothing()
    {
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("98,99"));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_ListEnumAttribute_IgnoresEmptyElements()
    {
        // Trailing/duplicate commas should not produce empty-valued rows.
        var s101 = new S57ToS101Translator().Translate(LandRegionDocWithCatlnd("1,,3,"));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        Assert.DoesNotContain(feat.Attributes, a => a.Value.Length == 0);
    }

    [Fact]
    public void Translate_ListEnumAttribute_SplitsEvenWhenEnforcementDisabled()
    {
        // Splitting a comma-separated list enum is a structural S-57→S-101
        // requirement, independent of FC validation. With enforcement disabled
        // (allowedEnumValues: null) each code must still become its own
        // occurrence rather than passing through as a single raw "1,3" value.
        var translator = new S57ToS101Translator(S57S101Mapping.Default, allowedEnumValues: null);
        var s101 = translator.Translate(LandRegionDocWithCatlnd("1,3"));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        Assert.All(feat.Attributes, a =>
            Assert.Equal("categoryOfLandRegion", s101.AttributeTypeCatalogue[a.NumericCode]));
        var values = feat.Attributes.Select(a => a.Value).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "1", "3" }, values);
        Assert.Equal(new ushort[] { 1, 2 }, feat.Attributes.Select(a => a.Index).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Translate_ListEnumAttribute_EnforcementDisabled_KeepsOutOfRangeCodes()
    {
        // With enforcement disabled, the split still happens but no IsAllowed
        // filtering is applied, so even out-of-FC codes survive as occurrences.
        var translator = new S57ToS101Translator(S57S101Mapping.Default, allowedEnumValues: null);
        var s101 = translator.Translate(LandRegionDocWithCatlnd("3,99"));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, feat.Attributes.Count);
        var values = feat.Attributes.Select(a => a.Value).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "3", "99" }, values);
    }

    [Fact]
    public void Translate_NonEnumTextAttribute_WithComma_EnforcementDisabled_IsNotSplit()
    {
        // Even with enforcement disabled, free text (OBJNAM) that legitimately
        // contains a comma must not be split — the integer-list fallback
        // discriminator recognises non-integer tokens as text.
        var doc = LandRegionWithS57Attributes(Attr(116, "Smith, Jones and Co."));

        var translator = new S57ToS101Translator(S57S101Mapping.Default, allowedEnumValues: null);
        var s101 = translator.Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();
        Assert.Equal("Smith, Jones and Co.", GetSubAttribute(s101, instance, "name"));
    }

    [Fact]
    public void Translate_NonEnumTextAttribute_WithComma_IsNotSplit()
    {
        // OBJNAM (text) is handled as featureName; a comma in the name must
        // survive intact rather than being split as if it were a list.
        var doc = LandRegionWithS57Attributes(Attr(116, "Smith, Jones and Co."));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();
        Assert.Equal("Smith, Jones and Co.", GetSubAttribute(s101, instance, "name"));
    }

    // ── v3.4 → #452 #4: INFORM/NINFOM/TXTDSC/NTXTDS → NauticalInformation ──
    //
    // The textual attributes now travel via the "fuller path" (Conversion
    // Guidance §2.3): a standalone NauticalInformation information-type record
    // carries the `information` complex, bound to the feature by an
    // AdditionalInformation / theInformation association. These helpers resolve
    // the NauticalInformation record a feature points at and iterate its
    // `information` complex instances.

    private static S101InformationRecord? NauticalInformationOf(
        S101Document doc, S101FeatureRecord feature)
    {
        // Resolve the AdditionalInformation association code by name.
        ushort? assocCode = null;
        foreach (var (c, n) in doc.InformationAssociationCatalogue)
            if (string.Equals(n, "AdditionalInformation", StringComparison.OrdinalIgnoreCase)) { assocCode = c; break; }
        if (assocCode is null) return null;

        foreach (var ia in feature.InformationAssociations)
        {
            if (ia.NumericCode != assocCode) continue;
            if (doc.InformationTypes.TryGetValue(ia.RecordId, out var info)) return info;
        }
        return null;
    }

    private static IEnumerable<S101Attribute> InformationInstance(
        S101Document doc,
        IReadOnlyList<S101Attribute> attrs,
        int instanceIndex)
    {
        ushort? infoCode = null;
        foreach (var (code, name) in doc.AttributeTypeCatalogue)
        {
            if (string.Equals(name, "information", StringComparison.OrdinalIgnoreCase))
            {
                infoCode = code;
                break;
            }
        }
        if (infoCode is null) yield break;

        int found = 0;
        bool collecting = false;
        foreach (var a in attrs)
        {
            if (a.NumericCode == infoCode && a.Index == 1)
            {
                if (collecting) break; // hit next instance
                found++;
                if (found == instanceIndex)
                {
                    collecting = true;
                    yield return a;
                    continue;
                }
            }
            else if (collecting)
            {
                yield return a;
            }
        }
    }

    private static string? GetSubAttribute(
        S101Document doc,
        IEnumerable<S101Attribute> instance,
        string subAttrCode)
    {
        ushort? code = null;
        foreach (var (c, n) in doc.AttributeTypeCatalogue)
        {
            if (string.Equals(n, subAttrCode, StringComparison.OrdinalIgnoreCase))
            {
                code = c;
                break;
            }
        }
        if (code is null) return null;
        foreach (var a in instance)
            if (a.NumericCode == code && a.Index == 1)
                return a.Value;
        return null;
    }

    private static EncDotNet.S57.S57Document LandRegionWithS57Attributes(
        params EncDotNet.S57.S57AttributeValue[] attrs)
    {
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 73, // LNDRGN → LandRegion
            attributes: attrs,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        return BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });
    }

    [Fact]
    public void Translate_InformAttribute_BecomesNauticalInformation_WithEnglish()
    {
        var doc = LandRegionWithS57Attributes(Attr(102, "Visible all around. Higher intensity on rangeline"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        // The feature no longer carries an inline `information` complex.
        Assert.Empty(InformationInstance(s101, feat.Attributes, 1).ToList());

        var info = NauticalInformationOf(s101, feat);
        Assert.NotNull(info);
        Assert.Equal("NauticalInformation", s101.InformationTypeCatalogue[info!.InformationTypeCode]);
        var instance = InformationInstance(s101, info.Attributes, 1).ToList();

        Assert.NotEmpty(instance);
        Assert.Equal("Visible all around. Higher intensity on rangeline",
            GetSubAttribute(s101, instance, "text"));
        Assert.Equal("eng", GetSubAttribute(s101, instance, "language"));
        Assert.Null(GetSubAttribute(s101, instance, "fileReference"));
    }

    [Fact]
    public void Translate_TxtdscAttribute_BecomesFileReferenceWithEnglish()
    {
        var doc = LandRegionWithS57Attributes(Attr(158, "US5WA23A.TXT"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var info = NauticalInformationOf(s101, feat);
        Assert.NotNull(info);
        var instance = InformationInstance(s101, info!.Attributes, 1).ToList();

        Assert.Equal("US5WA23A.TXT", GetSubAttribute(s101, instance, "fileReference"));
        Assert.Equal("eng", GetSubAttribute(s101, instance, "language"));
        Assert.Null(GetSubAttribute(s101, instance, "text"));
    }

    [Fact]
    public void Translate_NinfomAttribute_BecomesNauticalInformation_WithBlankLanguage()
    {
        var doc = LandRegionWithS57Attributes(Attr(300, "Información en español"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var info = NauticalInformationOf(s101, feat);
        Assert.NotNull(info);
        var instance = InformationInstance(s101, info!.Attributes, 1).ToList();

        Assert.Equal("Información en español", GetSubAttribute(s101, instance, "text"));
        Assert.Equal("", GetSubAttribute(s101, instance, "language"));
    }

    [Fact]
    public void Translate_InformAndNinfom_EmitTwoInformationInstances_OnOneNauticalInformation()
    {
        var doc = LandRegionWithS57Attributes(
            Attr(102, "English text"),
            Attr(300, "National text"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var info = NauticalInformationOf(s101, feat);
        Assert.NotNull(info);

        var first = InformationInstance(s101, info!.Attributes, 1).ToList();
        var second = InformationInstance(s101, info.Attributes, 2).ToList();

        Assert.Equal("English text", GetSubAttribute(s101, first, "text"));
        Assert.Equal("eng", GetSubAttribute(s101, first, "language"));
        Assert.Equal("National text", GetSubAttribute(s101, second, "text"));
        Assert.Equal("", GetSubAttribute(s101, second, "language"));

        // Both texts live on a single NauticalInformation record (one association).
        Assert.Single(feat.InformationAssociations);
        Assert.Single(s101.InformationTypes);
    }

    [Fact]
    public void Translate_InformAndTxtdscTogether_EmitOneInstanceWithBothSubAttrs()
    {
        var doc = LandRegionWithS57Attributes(
            Attr(102, "Inline note"),
            Attr(158, "EXTRA.TXT"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var info = NauticalInformationOf(s101, feat);
        Assert.NotNull(info);

        var first = InformationInstance(s101, info!.Attributes, 1).ToList();
        Assert.Equal("Inline note", GetSubAttribute(s101, first, "text"));
        Assert.Equal("EXTRA.TXT", GetSubAttribute(s101, first, "fileReference"));
        Assert.Equal("eng", GetSubAttribute(s101, first, "language"));

        Assert.Empty(InformationInstance(s101, info.Attributes, 2).ToList());
    }

    [Fact]
    public void Translate_NoTextualAttributes_EmitsNoNauticalInformation()
    {
        var doc = LandRegionWithS57Attributes(Attr(34, "1"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        Assert.Empty(feat.InformationAssociations);
        Assert.Empty(s101.InformationTypes);
        Assert.Empty(InformationInstance(s101, feat.Attributes, 1).ToList());
    }

    [Fact]
    public void Translate_Inform_WiresAdditionalInformationAssociation_AndCatalogues()
    {
        var diag = new S57TranslationDiagnostics();
        var doc = LandRegionWithS57Attributes(Attr(102, "See caution note"));

        var s101 = new S57ToS101Translator().Translate(doc, diag);
        var feat = Assert.Single(s101.Features);

        // The feature binds the info type through AdditionalInformation/theInformation.
        var ia = Assert.Single(feat.InformationAssociations);
        Assert.Equal("AdditionalInformation", s101.InformationAssociationCatalogue[ia.NumericCode]);
        Assert.Equal("theInformation", s101.RoleCatalogue[ia.RoleCode]);

        // The association points at the emitted NauticalInformation record.
        Assert.True(s101.InformationTypes.ContainsKey(ia.RecordId));
        Assert.Equal("NauticalInformation",
            s101.InformationTypeCatalogue[s101.InformationTypes[ia.RecordId].InformationTypeCode]);

        Assert.Equal(1, diag.NauticalInformationTypesEmitted);
    }

    [Fact]
    public void Translate_SeparateFeaturesWithText_EachGetOwnNauticalInformation()
    {
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 3000, 4000);
        var f1 = Feat(recordId: 1, primitive: 1, objectClass: 73,
            attributes: new[] { Attr(102, "First") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var f2 = Feat(recordId: 2, primitive: 1, objectClass: 73,
            featureIdentificationNumber: 2,
            attributes: new[] { Attr(102, "Second") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2 }, features: new[] { f1, f2 }), diag);

        Assert.Equal(2, s101.Features.Count);
        Assert.Equal(2, s101.InformationTypes.Count);
        Assert.Equal(2, diag.NauticalInformationTypesEmitted);
        foreach (var feat in s101.Features)
        {
            var info = NauticalInformationOf(s101, feat);
            Assert.NotNull(info);
        }
    }

    // ── TOPMAR slave → parent `topmark` complex attribute ───────────────

    // Builds a buoy/beacon master feature (with geometry) that references a
    // TOPMAR slave via an FFPT with the S-57 master/slave relationship, plus
    // the TOPMAR feature carrying the topmark shape/colour attributes.
    private static EncDotNet.S57.S57Document TopmarkScenario(
        ushort masterObjl,
        IEnumerable<EncDotNet.S57.S57AttributeValue> topmarkAttrs,
        EncDotNet.S57.S57RelationshipIndicator relationship
            = EncDotNet.S57.S57RelationshipIndicator.Slave,
        IEnumerable<EncDotNet.S57.S57AttributeValue>? masterAttrs = null)
    {
        var n1 = Node(1, 1000, 2000);
        var master = Feat(
            recordId: 1, primitive: 1, objectClass: masterObjl,
            featureIdentificationNumber: 10,
            attributes: masterAttrs,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) },
            featurePointers: new[] { Ffpt(540, 20, relationship: relationship) });
        var topmar = Feat(
            recordId: 2, primitive: 1, objectClass: 144, // TOPMAR
            featureIdentificationNumber: 20,
            attributes: topmarkAttrs);
        return BuildDocument(vectorRecords: new[] { n1 }, features: new[] { master, topmar });
    }

    [Fact]
    public void Translate_TopmarSlave_FoldsIntoParentTopmarkComplex()
    {
        // BOYSAW (OBJL 18) → SafeWaterBuoy, which binds the `topmark` complex.
        // The TOPMAR slave carries TOPSHP + COLOUR + COLPAT, which fold into the
        // parent's topmark instance (topmarkDaymarkShape / colour / colourPattern).
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(TopmarkScenario(
            masterObjl: 18,
            topmarkAttrs: new[]
            {
                Attr(171, "1"),  // TOPSHP → topmarkDaymarkShape (mandatory)
                Attr(75, "3"),   // COLOUR → colour (Red)
                Attr(76, "1"),   // COLPAT → colourPattern
            }), diag);

        Assert.Equal(1, diag.TopmarksAbsorbed);

        // Only the master buoy survives; the TOPMAR is absorbed, not standalone.
        var feat = Assert.Single(s101.Features);
        Assert.Equal("SafeWaterBuoy", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.DoesNotContain((ushort)144, diag.UnmappedObjectClasses.Keys);

        var topmark = ComplexInstance(s101, feat.Attributes, "topmark", 1).ToList();
        Assert.NotEmpty(topmark);
        Assert.Equal("1", GetSubAttribute(s101, topmark, "topmarkDaymarkShape"));
        Assert.Equal("3", GetSubAttribute(s101, topmark, "colour"));
        Assert.Equal("1", GetSubAttribute(s101, topmark, "colourPattern"));
    }

    [Fact]
    public void Translate_TopmarSlave_MultiValuedColour_SplitsIntoOccurrences()
    {
        // A comma-separated COLOUR list becomes multiple colour occurrences
        // within the single topmark instance.
        var s101 = new S57ToS101Translator().Translate(TopmarkScenario(
            masterObjl: 18,
            topmarkAttrs: new[]
            {
                Attr(171, "1"),   // TOPSHP → topmarkDaymarkShape
                Attr(75, "1,3"),  // COLOUR → colour (White, Red)
            }));

        var feat = Assert.Single(s101.Features);
        var topmark = ComplexInstance(s101, feat.Attributes, "topmark", 1).ToList();

        ushort? colourCode = null;
        foreach (var (c, n) in s101.AttributeTypeCatalogue)
            if (string.Equals(n, "colour", StringComparison.OrdinalIgnoreCase)) colourCode = c;
        var colours = topmark.Where(a => a.NumericCode == colourCode).Select(a => a.Value).ToList();
        Assert.Equal(new[] { "1", "3" }, colours);
    }

    [Fact]
    public void Translate_TopmarSlave_MissingShape_DropsTopmarkInstance()
    {
        // topmarkDaymarkShape is mandatory; with no valid TOPSHP the whole
        // topmark instance is rolled back (recorded for corpus audits).
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(TopmarkScenario(
            masterObjl: 18,
            topmarkAttrs: new[]
            {
                Attr(171, "99"), // invalid TOPSHP
                Attr(75, "3"),   // COLOUR
            }), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "topmark", 1).ToList());
        var attributeNames = feat.Attributes
            .Select(a => s101.AttributeTypeCatalogue[a.NumericCode])
            .ToList();
        Assert.DoesNotContain("topmark", attributeNames);
    }

    [Fact]
    public void Translate_TopmarPeerRelationship_StaysUnmapped()
    {
        // A Peer (not Slave) relationship is not a topmark master/slave binding,
        // so the TOPMAR is left unmapped and no topmark is folded in.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(TopmarkScenario(
            masterObjl: 18,
            topmarkAttrs: new[] { Attr(171, "1"), Attr(75, "3") },
            relationship: EncDotNet.S57.S57RelationshipIndicator.Peer), diag);

        Assert.Equal(0, diag.TopmarksAbsorbed);
        var buoy = FeatureOfClass(s101, "SafeWaterBuoy");
        Assert.NotNull(buoy);
        Assert.Empty(ComplexInstance(s101, buoy!.Attributes, "topmark", 1).ToList());
        Assert.Contains((ushort)144, diag.UnmappedObjectClasses.Keys);
    }

    [Fact]
    public void Translate_TopmarSlave_OnNonTopmarkBindingMaster_StaysUnmapped()
    {
        // DEPARE (OBJL 42) → DepthArea does not bind `topmark`; a Slave FFPT to a
        // TOPMAR must not fold in, and the TOPMAR is left unmapped.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(TopmarkScenario(
            masterObjl: 42,
            topmarkAttrs: new[] { Attr(171, "1"), Attr(75, "3") }), diag);

        Assert.Equal(0, diag.TopmarksAbsorbed);
        Assert.Contains((ushort)144, diag.UnmappedObjectClasses.Keys);
    }

    [Fact]
    public void Translate_SharedTopmarSlave_FoldsIntoOnlyOneMaster()
    {
        // Two masters whose FFPTs both reference the same TOPMAR slave must not
        // both fold it in (which would duplicate the topmark attributes); the
        // TOPMAR is consumed by a single master only.
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 1000, 2100);
        var masterA = Feat(
            recordId: 1, primitive: 1, objectClass: 18, // BOYSAW → SafeWaterBuoy
            featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) },
            featurePointers: new[] { Ffpt(540, 30, relationship: EncDotNet.S57.S57RelationshipIndicator.Slave) });
        var masterB = Feat(
            recordId: 2, primitive: 1, objectClass: 18,
            featureIdentificationNumber: 20,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) },
            featurePointers: new[] { Ffpt(540, 30, relationship: EncDotNet.S57.S57RelationshipIndicator.Slave) });
        var topmar = Feat(
            recordId: 3, primitive: 1, objectClass: 144,
            featureIdentificationNumber: 30,
            attributes: new[] { Attr(171, "1"), Attr(75, "3") });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2 },
                features: new[] { masterA, masterB, topmar }), diag);

        Assert.Equal(1, diag.TopmarksAbsorbed);
        var withTopmark = s101.Features
            .Count(f => ComplexInstance(s101, f.Attributes, "topmark", 1).Any());
        Assert.Equal(1, withTopmark);
    }

    // ── C_AGGR → RangeSystemAggregation (synthesised RangeSystem) ────────

    // Resolves the S-101 class name of a feature via the FeatureTypeCatalogue.
    private static string ClassOf(S101Document doc, S101FeatureRecord feat)
        => doc.FeatureTypeCatalogue[feat.FeatureTypeCode];

    private static S101FeatureRecord? FeatureOfClass(S101Document doc, string s101Class)
        => doc.Features.FirstOrDefault(f => ClassOf(doc, f) == s101Class);

    [Fact]
    public void Translate_RangeSystemCAggr_SynthesisesGeometrylessRangeSystem_WithComponentAssociations()
    {
        // NavigationLine (track) + RecommendedTrack (track) + SpecialPurposeGeneralBeacon (navaid).
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 1000, 2100);
        var n3 = Node(3, 1000, 2200);
        var navlne = Feat(1, 1, 85, featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var rectrc = Feat(2, 1, 109, featureIdentificationNumber: 11,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var beacon = Feat(3, 1, 9, featureIdentificationNumber: 12,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 3, 1, 0, 0) });
        var aggr = Feat(4, 1, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 12) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2, n3 },
                features: new[] { navlne, rectrc, beacon, aggr }), diag);

        Assert.Equal(1, diag.RangeSystemsEmitted);
        var rangeSystem = FeatureOfClass(s101, "RangeSystem");
        Assert.NotNull(rangeSystem);

        // The collection feature is geometry-less and self-identifies from the C_AGGR.
        Assert.Empty(rangeSystem!.SpatialAssociations);
        Assert.Equal(99u, rangeSystem.FeatureIdentificationNumber);

        // One theComponent association per member, each pointing at the member's record.
        Assert.Equal(3, rangeSystem.FeatureAssociations.Count);
        var memberIds = new[] { navlne, rectrc, beacon }
            .Select(m => FeatureByLnam(s101, m).RecordId).ToHashSet();
        foreach (var fa in rangeSystem.FeatureAssociations)
        {
            Assert.Equal("RangeSystemAggregation", s101.FeatureAssociationCatalogue[fa.NumericCode]);
            Assert.Equal("theComponent", s101.RoleCatalogue[fa.RoleCode]);
            Assert.Contains(fa.RecordId, memberIds);
        }
        Assert.Equal(3, rangeSystem.FeatureAssociations.Select(a => a.RecordId).Distinct().Count());

        // Members carry no back-association (the collection is the navigable end).
        foreach (var m in new[] { navlne, rectrc, beacon })
            Assert.Empty(FeatureByLnam(s101, m).FeatureAssociations);
    }

    [Fact]
    public void Translate_S401Target_RangeSystemCAggr_IsNotSynthesised()
    {
        // The same qualifying range system as above; S-401 has no RangeSystem class.
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 1000, 2100);
        var n3 = Node(3, 1000, 2200);
        var navlne = Feat(1, 1, 85, featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var rectrc = Feat(2, 1, 109, featureIdentificationNumber: 11,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var beacon = Feat(3, 1, 9, featureIdentificationNumber: 12,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 3, 1, 0, 0) });
        var aggr = Feat(4, 1, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 12) });
        var diag = new S57TranslationDiagnostics();

        var translated = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(vectorRecords: new[] { n1, n2, n3 },
                features: new[] { navlne, rectrc, beacon, aggr }), diag);

        Assert.Equal(0, diag.RangeSystemsEmitted);
        Assert.Null(FeatureOfClass(translated, "RangeSystem"));
        Assert.Empty(translated.FeatureAssociationCatalogue);
        Assert.Equal(3, translated.Features.Count);
        Assert.Equal(1, diag.UnmappedObjectClasses[400]);
    }

    [Fact]
    public void Translate_CAggr_WithoutTrackMember_NotMappedToRangeSystem()
    {
        // Two beacons, no navigational track → not a range system.
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 1000, 2100);
        var b1 = Feat(1, 1, 9, featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var b2 = Feat(2, 1, 9, featureIdentificationNumber: 11,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var aggr = Feat(3, 1, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2 },
                features: new[] { b1, b2, aggr }), diag);

        Assert.Equal(0, diag.RangeSystemsEmitted);
        Assert.Null(FeatureOfClass(s101, "RangeSystem"));
        Assert.Equal(2, s101.Features.Count);
        Assert.Contains(diag.UnmappedObjectClasses, kv => kv.Key == 400);
    }

    [Fact]
    public void Translate_CAggr_WithNonPermittedMember_NotMappedToRangeSystem()
    {
        // NavigationLine (track) + LandArea (not a permitted range-system component).
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 1000, 2100);
        var navlne = Feat(1, 1, 85, featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var land = Feat(2, 1, 71, featureIdentificationNumber: 11,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var aggr = Feat(3, 1, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2 },
                features: new[] { navlne, land, aggr }), diag);

        Assert.Equal(0, diag.RangeSystemsEmitted);
        Assert.Null(FeatureOfClass(s101, "RangeSystem"));
        Assert.Contains(diag.UnmappedObjectClasses, kv => kv.Key == 400);
    }

    [Fact]
    public void Translate_CAggr_WithDroppedMember_NotMappedToRangeSystem()
    {
        // NavigationLine (track) + permitted beacon that resolves but is dropped
        // because it has no geometry. The C_AGGR must not emit a partial collection.
        var n1 = Node(1, 1000, 2000);
        var navlne = Feat(1, 1, 85, featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var droppedBeacon = Feat(2, 1, 9, featureIdentificationNumber: 11);
        var aggr = Feat(3, 1, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1 },
                features: new[] { navlne, droppedBeacon, aggr }), diag);

        Assert.Equal(0, diag.RangeSystemsEmitted);
        Assert.Null(FeatureOfClass(s101, "RangeSystem"));
        Assert.Contains(diag.UnmappedObjectClasses, kv => kv.Key == 400);
    }

    [Fact]
    public void Translate_FeatureRecordsRead_CountsAllIteratedNonSoundingFeatures()
    {
        var sharedLightNode = Node(1, 1000, 2000);
        var soundingNode = SoundingNode(2, (10, 20, 50));
        var primaryLight = Feat(1, 1, 75, featureIdentificationNumber: 1,
            attributes: new[] { Attr(107, "2"), Attr(75, "3"), Attr(136, "10"), Attr(137, "90") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var absorbedLight = Feat(2, 1, 75, featureIdentificationNumber: 2,
            attributes: new[] { Attr(107, "2"), Attr(75, "1"), Attr(136, "90"), Attr(137, "180") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var droppedBeacon = Feat(3, 1, 9, featureIdentificationNumber: 3);
        var aggr = Feat(4, 1, 400, featureIdentificationNumber: 4);
        var sounding = Feat(5, 1, 129, featureIdentificationNumber: 5,
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 2, 1, 0, 0) });
        var diag = new S57TranslationDiagnostics();

        new S57ToS101Translator().Translate(
            BuildDocument(
                vectorRecords: new EncDotNet.S57.S57VectorRecord[] { sharedLightNode, soundingNode },
                features: new[] { primaryLight, absorbedLight, droppedBeacon, aggr, sounding }),
            diag);

        Assert.Equal(4, diag.FeatureRecordsRead);
        Assert.Equal(1, diag.SoundingFeaturesRead);
        Assert.Equal(1, diag.SectorLightsMerged);
    }

    [Fact]
    public void Translate_NestedRangeSystemCAggr_LinksToNestedCollection()
    {
        // Inner range system: NavigationLine + beacon. Outer range system:
        // RecommendedTrack + the inner C_AGGR (RangeSystem is a permitted component).
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 1000, 2100);
        var n3 = Node(3, 1000, 2200);
        var innerTrack = Feat(1, 1, 85, featureIdentificationNumber: 10,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var innerBeacon = Feat(2, 1, 9, featureIdentificationNumber: 11,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var innerAggr = Feat(3, 1, 400, featureIdentificationNumber: 20,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var outerTrack = Feat(4, 1, 109, featureIdentificationNumber: 12,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 3, 1, 0, 0) });
        var outerAggr = Feat(5, 1, 400, featureIdentificationNumber: 21,
            featurePointers: new[] { Ffpt(540, 12), Ffpt(540, 20) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2, n3 },
                features: new[] { innerTrack, innerBeacon, innerAggr, outerTrack, outerAggr }), diag);

        Assert.Equal(2, diag.RangeSystemsEmitted);
        var rangeSystems = s101.Features.Where(f => ClassOf(s101, f) == "RangeSystem").ToList();
        Assert.Equal(2, rangeSystems.Count);

        var inner = rangeSystems.Single(r => r.FeatureIdentificationNumber == 20);
        var outer = rangeSystems.Single(r => r.FeatureIdentificationNumber == 21);

        // The outer range system links to the inner range system as a component.
        Assert.Contains(outer.FeatureAssociations, a => a.RecordId == inner.RecordId);
        Assert.Equal(2, outer.FeatureAssociations.Count);
    }

    private static S101FeatureRecord FeatureByLnam(
        S101Document doc, EncDotNet.S57.S57FeatureRecord s57)
        => doc.Features.Single(f =>
            f.ProducingAgency == (ushort)s57.RecordName.AgencyCode
            && f.FeatureIdentificationNumber == (uint)s57.RecordName.FeatureId
            && f.FeatureIdentificationSubdivision == (ushort)s57.RecordName.FeatureSubdivision
            && ClassOf(doc, f) != "RangeSystem");

    // ── OBJNAM/NOBJNM → featureName complex attribute ───────────────────

    private static IEnumerable<S101Attribute> ComplexInstance(
        S101Document doc,
        IReadOnlyList<S101Attribute> attrs,
        string complexCode,
        int instanceIndex)
    {
        ushort? code = null;
        foreach (var (c, n) in doc.AttributeTypeCatalogue)
        {
            if (string.Equals(n, complexCode, StringComparison.OrdinalIgnoreCase))
            {
                code = c;
                break;
            }
        }
        if (code is null) yield break;

        int found = 0;
        bool collecting = false;
        foreach (var a in attrs)
        {
            if (a.NumericCode == code && a.Index == 1)
            {
                if (collecting) break;
                found++;
                if (found == instanceIndex)
                {
                    collecting = true;
                    yield return a;
                    continue;
                }
            }
            else if (collecting)
            {
                yield return a;
            }
        }
    }

    [Fact]
    public void Translate_ObjnamAttribute_BecomesFeatureNameComplex_WithEnglish()
    {
        var doc = LandRegionWithS57Attributes(Attr(116, "Puget Sound"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();

        Assert.NotEmpty(instance);
        Assert.Equal("Puget Sound", GetSubAttribute(s101, instance, "name"));
        Assert.Equal("eng", GetSubAttribute(s101, instance, "language"));
    }

    [Fact]
    public void Translate_ObjnamAttribute_OnFeatureBindingFeatureName_BecomesFeatureNameComplex()
    {
        var doc = PointFeatureWithS57Attributes(71, Attr(116, "Bainbridge Island"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();

        Assert.NotEmpty(instance);
        Assert.Equal("LandArea", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Equal("Bainbridge Island", GetSubAttribute(s101, instance, "name"));
        Assert.Equal("eng", GetSubAttribute(s101, instance, "language"));
    }

    [Fact]
    public void Translate_ObjnamAttribute_OnFeatureNotBindingFeatureName_IsRecordedUnmapped()
    {
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(116, "Survey Area")), diag);

        var feat = Assert.Single(s101.Features);

        Assert.Equal("QualityOfBathymetricData", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList());
        Assert.True(
            diag.UnmappedAttributes.TryGetValue(new S57AttributeDrop(308, 116), out var count),
            "Expected OBJNAM (ATTL 116) on M_QUAL (OBJL 308) to be recorded as unmapped.");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Translate_NobjnmAttribute_OnFeatureNotBindingFeatureName_IsRecordedUnmapped()
    {
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(301, "Área de levantamiento")), diag);

        var feat = Assert.Single(s101.Features);

        Assert.Equal("QualityOfBathymetricData", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList());
        Assert.True(
            diag.UnmappedAttributes.TryGetValue(new S57AttributeDrop(308, 301), out var count),
            "Expected NOBJNM (ATTL 301) on M_QUAL (OBJL 308) to be recorded as unmapped.");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Translate_NobjnmAttribute_BecomesFeatureNameComplex_WithBlankLanguage()
    {
        var doc = LandRegionWithS57Attributes(Attr(301, "Bahía de Todos"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();

        Assert.Equal("Bahía de Todos", GetSubAttribute(s101, instance, "name"));
        Assert.Equal("", GetSubAttribute(s101, instance, "language"));
    }

    [Fact]
    public void Translate_ObjnamAndNobjnm_EmitTwoFeatureNameInstances()
    {
        var doc = LandRegionWithS57Attributes(
            Attr(116, "English name"),
            Attr(301, "National name"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        var first = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();
        var second = ComplexInstance(s101, feat.Attributes, "featureName", 2).ToList();

        Assert.Equal("English name", GetSubAttribute(s101, first, "name"));
        Assert.Equal("eng", GetSubAttribute(s101, first, "language"));
        Assert.Equal("National name", GetSubAttribute(s101, second, "name"));
        Assert.Equal("", GetSubAttribute(s101, second, "language"));
    }

    [Fact]
    public void Translate_EmptyObjnam_EmitsNoFeatureNameInstance()
    {
        var doc = LandRegionWithS57Attributes(Attr(116, ""));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        Assert.Empty(ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList());
    }

    [Fact]
    public void Translate_Objnam_IsNotEmittedAsSimpleNameAttribute()
    {
        var doc = LandRegionWithS57Attributes(Attr(116, "Some Place"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        // `name` must only appear inside a featureName instance (its marker
        // precedes it), never as a bare top-level simple attribute.
        ushort? nameCode = null;
        ushort? featureNameCode = null;
        foreach (var (c, n) in s101.AttributeTypeCatalogue)
        {
            if (string.Equals(n, "name", StringComparison.OrdinalIgnoreCase)) nameCode = c;
            if (string.Equals(n, "featureName", StringComparison.OrdinalIgnoreCase)) featureNameCode = c;
        }
        Assert.NotNull(nameCode);
        Assert.NotNull(featureNameCode);
        Assert.Contains(feat.Attributes, a => a.NumericCode == featureNameCode);
        // Every `name` row is preceded (somewhere) by a featureName marker.
        var instance = ComplexInstance(s101, feat.Attributes, "featureName", 1).ToList();
        Assert.Equal("Some Place", GetSubAttribute(s101, instance, "name"));
    }

    // ── LITCHR/SIGGRP/SIGPER → rhythmOfLight complex attribute ──────────

    private static EncDotNet.S57.S57Document LightWithS57Attributes(
        params EncDotNet.S57.S57AttributeValue[] attrs)
    {
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 75, // LIGHTS → LightAllAround
            attributes: attrs,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        return BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });
    }

    [Fact]
    public void Translate_Litchr_BecomesRhythmOfLightComplex()
    {
        // LITCHR = 107, value 2 ("Flashing") is an allowable lightCharacteristic.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(Attr(107, "2")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightAllAround", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var instance = ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("2", GetSubAttribute(s101, instance, "lightCharacteristic"));
    }

    [Fact]
    public void Translate_LitchrWithSignalGroupAndPeriod_AllBecomeRhythmSubAttributes()
    {
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "8"),   // LITCHR → lightCharacteristic (Occulting)
            Attr(141, "(2)"), // SIGGRP → signalGroup
            Attr(142, "6.0"))); // SIGPER → signalPeriod

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList();
        Assert.Equal("8", GetSubAttribute(s101, instance, "lightCharacteristic"));
        Assert.Equal("(2)", GetSubAttribute(s101, instance, "signalGroup"));
        Assert.Equal("6.0", GetSubAttribute(s101, instance, "signalPeriod"));

        // signalGroup/signalPeriod must NOT also appear as top-level simple
        // attributes on a light (they bind only via rhythmOfLight here).
        ushort? sigGrpCode = null;
        foreach (var (c, n) in s101.AttributeTypeCatalogue)
            if (string.Equals(n, "signalGroup", StringComparison.OrdinalIgnoreCase)) sigGrpCode = c;
        var topLevelSigGrp = feat.Attributes
            .TakeWhile(a => s101.AttributeTypeCatalogue[a.NumericCode] != "rhythmOfLight");
        Assert.DoesNotContain(topLevelSigGrp, a => a.NumericCode == sigGrpCode);
    }

    [Fact]
    public void Translate_InvalidLitchr_EmitsNoRhythmOfLight()
    {
        // 99 is not an allowable lightCharacteristic code; the mandatory
        // sub-attribute is missing so no rhythmOfLight instance is emitted.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(Attr(107, "99")));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList());
    }

    [Fact]
    public void Translate_SignalGroupOnFogSignal_StaysTopLevelSimpleAttribute()
    {
        // FOGSIG (OBJL 58) → FogSignal, which binds signalGroup directly (not
        // via rhythmOfLight). SIGGRP must remain a top-level simple attribute.
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 58, // FOGSIG → FogSignal
            attributes: new[] { Attr(141, "(3)") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList());
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("signalGroup", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("(3)", attr.Value);
    }

    // ── SIGSEQ → signalSequence complex attribute ───────────────────────

    [Fact]
    public void Translate_SigseqOnLight_BecomesNestedSignalSequenceInRhythmOfLight()
    {
        // LIGHTS (OBJL 75) → LightAllAround, which binds rhythmOfLight; SIGSEQ
        // nests inside it. "02.0+(02.0)" → two phases: 2.0s lit, 2.0s eclipsed.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),            // LITCHR → lightCharacteristic (Flashing)
            Attr(143, "02.0+(02.0)"))); // SIGSEQ → nested signalSequence

        var feat = Assert.Single(s101.Features);
        var rhythm = ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList();
        Assert.Equal("2", GetSubAttribute(s101, rhythm, "lightCharacteristic"));

        // Two nested signalSequence phases, read directly from the flat list.
        var phase1 = ComplexInstance(s101, feat.Attributes, "signalSequence", 1).ToList();
        Assert.Equal("2", GetSubAttribute(s101, phase1, "signalDuration"));
        Assert.Equal("1", GetSubAttribute(s101, phase1, "signalStatus"));

        var phase2 = ComplexInstance(s101, feat.Attributes, "signalSequence", 2).ToList();
        Assert.Equal("2", GetSubAttribute(s101, phase2, "signalDuration"));
        Assert.Equal("2", GetSubAttribute(s101, phase2, "signalStatus"));
    }

    [Fact]
    public void Translate_SigseqLeadingZerosAndMultiplePhases_NormalisedAndOrdered()
    {
        // "00.6+(05.4)+03.0+(03.0)" → 4 phases, leading zeros normalised.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),
            Attr(143, "00.6+(05.4)+03.0+(03.0)")));

        var feat = Assert.Single(s101.Features);
        var expected = new[] { ("0.6", "1"), ("5.4", "2"), ("3", "1"), ("3", "2") };
        for (int i = 0; i < expected.Length; i++)
        {
            var phase = ComplexInstance(s101, feat.Attributes, "signalSequence", i + 1).ToList();
            Assert.Equal(expected[i].Item1, GetSubAttribute(s101, phase, "signalDuration"));
            Assert.Equal(expected[i].Item2, GetSubAttribute(s101, phase, "signalStatus"));
        }
    }

    [Fact]
    public void Translate_SigseqOnFogSignal_BecomesTopLevelSignalSequence()
    {
        // FOGSIG (OBJL 58) → FogSignal, which binds signalSequence at the top
        // level (not via rhythmOfLight). "05.0+(10.0)" → 5.0s sound, 10.0s silent.
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 58,
            attributes: new[] { Attr(143, "05.0+(10.0)") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var doc = BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList());

        var phase1 = ComplexInstance(s101, feat.Attributes, "signalSequence", 1).ToList();
        Assert.Equal("5", GetSubAttribute(s101, phase1, "signalDuration"));
        Assert.Equal("1", GetSubAttribute(s101, phase1, "signalStatus"));

        var phase2 = ComplexInstance(s101, feat.Attributes, "signalSequence", 2).ToList();
        Assert.Equal("10", GetSubAttribute(s101, phase2, "signalDuration"));
        Assert.Equal("2", GetSubAttribute(s101, phase2, "signalStatus"));
    }

    [Fact]
    public void Translate_SigseqOnLightWithoutLitchr_EmitsNoSignalSequence()
    {
        // No LITCHR means no rhythmOfLight instance to anchor the nested
        // signalSequence, so the sequence has nowhere to nest and is dropped.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(143, "02.0+(02.0)")));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList());
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "signalSequence", 1).ToList());
    }

    // ── SECTR1/SECTR2/COLOUR/VALNMR/LITVIS → sectorCharacteristics (LightSectored) ──

    [Fact]
    public void Translate_LightWithSector_RedirectsToLightSectored_AndAssemblesComplex()
    {
        // LIGHTS (OBJL 75) carrying a sector arc (SECTR1/SECTR2) redirects to
        // LightSectored, whose sectorCharacteristics complex is assembled from
        // LITCHR/COLOUR/VALNMR and the two sector bearings.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),      // LITCHR → lightCharacteristic (Flashing)
            Attr(75, "3"),       // COLOUR → colour (Red)
            Attr(178, "10.5"),   // VALNMR → valueOfNominalRange
            Attr(136, "340.3"),  // SECTR1 → sectorLimitOne.sectorBearing
            Attr(137, "8.3")));  // SECTR2 → sectorLimitTwo.sectorBearing

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightSectored", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var sc = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList();
        Assert.NotEmpty(sc);
        Assert.Equal("2", GetSubAttribute(s101, sc, "lightCharacteristic"));
        Assert.Equal("3", GetSubAttribute(s101, sc, "colour"));
        Assert.Equal("10.5", GetSubAttribute(s101, sc, "valueOfNominalRange"));

        // The two bearings live three levels deep, distinguished by their
        // sectorLimitOne / sectorLimitTwo parent (each appears once, so the
        // first sectorBearing following each marker is that limit's bearing).
        var one = ComplexInstance(s101, feat.Attributes, "sectorLimitOne", 1).ToList();
        Assert.Equal("340.3", GetSubAttribute(s101, one, "sectorBearing"));
        var two = ComplexInstance(s101, feat.Attributes, "sectorLimitTwo", 1).ToList();
        Assert.Equal("8.3", GetSubAttribute(s101, two, "sectorBearing"));
    }

    [Fact]
    public void Translate_SectoredLight_WithOnlyOneBearing_OmitsSectorLimitSubtree()
    {
        // sectorLimitOne and sectorLimitTwo are both mandatory under
        // sectorLimit, so a lone SECTR1 bearing must not emit a partial subtree.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),      // LITCHR → lightCharacteristic (Flashing)
            Attr(75, "3"),       // COLOUR → colour (Red)
            Attr(136, "340.3")), // SECTR1 only
            diag);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightSectored", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var sc = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList();
        Assert.NotEmpty(sc);
        Assert.Equal("2", GetSubAttribute(s101, sc, "lightCharacteristic"));
        Assert.NotEmpty(ComplexInstance(s101, feat.Attributes, "lightSector", 1).ToList());

        var attributeNames = feat.Attributes
            .Select(a => s101.AttributeTypeCatalogue[a.NumericCode])
            .ToList();
        Assert.DoesNotContain("sectorLimit", attributeNames);
        Assert.DoesNotContain("sectorLimitOne", attributeNames);
        Assert.DoesNotContain("sectorLimitTwo", attributeNames);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "sectorLimit", 1).ToList());
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "sectorLimitOne", 1).ToList());
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "sectorLimitTwo", 1).ToList());

        // The lone SECTR1 bearing is dropped, so it is recorded for corpus audits.
        Assert.True(diag.RuleDroppedAttributes.TryGetValue(136, out var dropped) && dropped >= 1);
    }

    [Fact]
    public void Translate_SectoredLight_WithoutLitchr_RecordsDivertedSectorAttributes()
    {
        // A sector arc (SECTR1/SECTR2) redirects LIGHTS to LightSectored, but
        // without a valid LITCHR to anchor the mandatory lightCharacteristic no
        // sectorCharacteristics instance is emitted. The sector-input attributes
        // (COLOUR/SECTR1/SECTR2) are diverted from the per-attribute pass-through,
        // so they must be recorded as dropped for corpus audits rather than
        // vanishing silently.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(75, "3"),       // COLOUR → colour (Red)
            Attr(136, "340.3"),  // SECTR1
            Attr(137, "8.3")),   // SECTR2 (no LITCHR)
            diag);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightSectored", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList());

        Assert.True(diag.RuleDroppedAttributes.TryGetValue(75, out var colourDropped) && colourDropped >= 1);
        Assert.True(diag.RuleDroppedAttributes.TryGetValue(136, out var s1Dropped) && s1Dropped >= 1);
        Assert.True(diag.RuleDroppedAttributes.TryGetValue(137, out var s2Dropped) && s2Dropped >= 1);
    }

    [Fact]
    public void Translate_SectoredLight_WithoutColour_OmitsSectorCharacteristicsSubtree()
    {
        // lightSector requires at least one colour; with no COLOUR on the S-57
        // feature the entire sectorCharacteristics instance must be rolled back.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),      // LITCHR → lightCharacteristic (Flashing)
            Attr(136, "340.3"),  // SECTR1
            Attr(137, "8.3")));  // SECTR2

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightSectored", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var attributeNames = feat.Attributes
            .Select(a => s101.AttributeTypeCatalogue[a.NumericCode])
            .ToList();
        Assert.DoesNotContain("sectorCharacteristics", attributeNames);
        Assert.DoesNotContain("lightSector", attributeNames);
        Assert.DoesNotContain("colour", attributeNames);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList());
    }

    [Fact]
    public void Translate_SectoredLight_ColourAndVisibilityLists_SplitIntoMultipleSubAttributes()
    {
        // COLOUR and LITVIS are S-57 list-valued enumerations; each code
        // becomes a separate colour / lightVisibility sub-attribute of the
        // lightSector.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),     // LITCHR
            Attr(75, "3,1"),    // COLOUR → Red + White
            Attr(108, "3,7"),   // LITVIS → Faint + Obscured
            Attr(136, "10"),    // SECTR1
            Attr(137, "20")));  // SECTR2

        var feat = Assert.Single(s101.Features);
        var sc = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList();

        ushort NameCode(string n) => s101.AttributeTypeCatalogue.First(kv => kv.Value == n).Key;
        var colours = sc.Where(a => a.NumericCode == NameCode("colour")).Select(a => a.Value).ToList();
        Assert.Equal(new[] { "3", "1" }, colours);
        var vis = sc.Where(a => a.NumericCode == NameCode("lightVisibility")).Select(a => a.Value).ToList();
        Assert.Equal(new[] { "3", "7" }, vis);
    }

    [Fact]
    public void Translate_LightWithoutSector_StaysLightAllAround_NoSectorComplex()
    {
        // A LIGHTS object with no SECTR1 is a non-sectored light: it must still
        // map to LightAllAround (rhythmOfLight), not LightSectored.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),   // LITCHR
            Attr(75, "3")));  // COLOUR

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightAllAround", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList());
        Assert.NotEmpty(ComplexInstance(s101, feat.Attributes, "rhythmOfLight", 1).ToList());
    }

    [Fact]
    public void Translate_SectoredLight_SectorAttributesNotEmittedTopLevel()
    {
        // On LightSectored none of the sector attributes bind at the top level,
        // so COLOUR/VALNMR must not appear as top-level simple attributes —
        // only inside the sectorCharacteristics complex.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(107, "2"),
            Attr(75, "3"),
            Attr(178, "10.5"),
            Attr(136, "340.3"),
            Attr(137, "8.3")));

        var feat = Assert.Single(s101.Features);
        // Nothing precedes the sectorCharacteristics marker (all sector inputs
        // are diverted into the complex; LITCHR has no top-level home either).
        var topLevel = feat.Attributes
            .TakeWhile(a => s101.AttributeTypeCatalogue[a.NumericCode] != "sectorCharacteristics")
            .Select(a => s101.AttributeTypeCatalogue[a.NumericCode])
            .ToList();
        Assert.DoesNotContain("colour", topLevel);
        Assert.DoesNotContain("valueOfNominalRange", topLevel);
    }

    // ── Co-located sector-light merge (#452 item #2) ─────────────────────

    // Builds N point LIGHTS features, each carrying one sector arc, all sharing
    // the same connected node (so they resolve to one S-101 point). Each entry
    // is that light's attribute set.
    private static EncDotNet.S57.S57Document CoLocatedSectorLights(
        uint sharedNodeId, params EncDotNet.S57.S57AttributeValue[][] lights)
    {
        var n = Node(sharedNodeId, 1000, 2000);
        var features = new EncDotNet.S57.S57FeatureRecord[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            features[i] = Feat(
                recordId: (uint)(i + 1), primitive: 1, objectClass: 75,
                featureIdentificationNumber: (uint)(i + 1),
                attributes: lights[i],
                spatialPointers: new[] { Sp(RcnmConnectedNode, sharedNodeId, 1, 0, 0) });
        }
        return BuildDocument(vectorRecords: new[] { n }, features: features);
    }

    private static int CountComplexInstances(
        S101Document doc, IReadOnlyList<S101Attribute> attrs, string complexCode)
    {
        ushort? code = null;
        foreach (var (c, name) in doc.AttributeTypeCatalogue)
        {
            if (string.Equals(name, complexCode, StringComparison.OrdinalIgnoreCase)) { code = c; break; }
        }
        if (code is null) return 0;
        return attrs.Count(a => a.NumericCode == code && a.Index == 1);
    }

    [Fact]
    public void Translate_CoLocatedSectorLights_MergeIntoSingleLightSectored()
    {
        // Two point LIGHTS sharing a node, each with one sector arc, model one
        // physical two-sector light. They fold into a single LightSectored
        // feature carrying two sectorCharacteristics instances.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(CoLocatedSectorLights(1,
            new[] { Attr(107, "2"), Attr(75, "3"), Attr(136, "10"), Attr(137, "90") },
            new[] { Attr(107, "2"), Attr(75, "1"), Attr(136, "90"), Attr(137, "180") }),
            diag);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightSectored", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Equal(2, CountComplexInstances(s101, feat.Attributes, "sectorCharacteristics"));
        Assert.Equal(1, diag.SectorLightsMerged);

        // The primary contributes the first arc (Red / 10-90); the absorbed
        // member contributes the second (White / 90-180).
        var first = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList();
        Assert.Equal("3", GetSubAttribute(s101, first, "colour"));
        var second = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 2).ToList();
        Assert.Equal("1", GetSubAttribute(s101, second, "colour"));
    }

    [Fact]
    public void Translate_CoLocatedSectorLights_DifferingLitchr_EachGetsOwnCharacteristic()
    {
        // Members whose LITCHR differs still merge; each simply yields its own
        // sectorCharacteristics instance (all conformant, FC allows [1..*]).
        var s101 = new S57ToS101Translator().Translate(CoLocatedSectorLights(1,
            new[] { Attr(107, "2"), Attr(75, "3"), Attr(136, "10"), Attr(137, "90") },
            new[] { Attr(107, "4"), Attr(75, "1"), Attr(136, "90"), Attr(137, "180") }));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(2, CountComplexInstances(s101, feat.Attributes, "sectorCharacteristics"));
        var first = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList();
        Assert.Equal("2", GetSubAttribute(s101, first, "lightCharacteristic"));
        var second = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 2).ToList();
        Assert.Equal("4", GetSubAttribute(s101, second, "lightCharacteristic"));
    }

    [Fact]
    public void Translate_NonCoLocatedSectorLights_StaySeparate()
    {
        // Two sector lights on distinct nodes are distinct physical lights and
        // must not merge.
        var n1 = Node(1, 1000, 2000);
        var n2 = Node(2, 5000, 6000);
        var f1 = Feat(recordId: 1, primitive: 1, objectClass: 75,
            attributes: new[] { Attr(107, "2"), Attr(75, "3"), Attr(136, "10"), Attr(137, "90") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        var f2 = Feat(recordId: 2, primitive: 1, objectClass: 75,
            featureIdentificationNumber: 2,
            attributes: new[] { Attr(107, "2"), Attr(75, "1"), Attr(136, "90"), Attr(137, "180") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 2, 1, 0, 0) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectorRecords: new[] { n1, n2 }, features: new[] { f1, f2 }), diag);

        Assert.Equal(2, s101.Features.Count);
        foreach (var feat in s101.Features)
            Assert.Equal(1, CountComplexInstances(s101, feat.Attributes, "sectorCharacteristics"));
        Assert.Equal(0, diag.SectorLightsMerged);
    }

    [Fact]
    public void Translate_CoLocatedSectorLights_AbsorbedMemberWithoutLitchr_ContributesNoInstance()
    {
        // An absorbed member lacking a valid LITCHR cannot anchor a
        // sectorCharacteristics instance, so it is dropped — but the group is
        // still merged (the member emits no feature of its own).
        var s101 = new S57ToS101Translator().Translate(CoLocatedSectorLights(1,
            new[] { Attr(107, "2"), Attr(75, "3"), Attr(136, "10"), Attr(137, "90") },
            new[] { Attr(75, "1"), Attr(136, "90"), Attr(137, "180") })); // no LITCHR

        var feat = Assert.Single(s101.Features);
        Assert.Equal(1, CountComplexInstances(s101, feat.Attributes, "sectorCharacteristics"));
    }

    // ── HORCLR → horizontalClearanceOpen / horizontalClearanceFixed ──────

    [Fact]
    public void Translate_GateWithHorclr_AssemblesHorizontalClearanceOpen()
    {
        // GATCON (OBJL 61) → Gate, which binds horizontalClearanceOpen. HORCLR
        // (ATTL 98) feeds the mandatory horizontalClearanceValue sub-attribute.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(61, Attr(98, "12.5")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Gate", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var open = ComplexInstance(s101, feat.Attributes, "horizontalClearanceOpen", 1).ToList();
        Assert.NotEmpty(open);
        Assert.Equal("12.5", GetSubAttribute(s101, open, "horizontalClearanceValue"));

        // Gate binds the open complex, not the fixed one.
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "horizontalClearanceFixed", 1).ToList());
    }

    [Fact]
    public void Translate_ShorelineConstructionWithHorclr_AssemblesHorizontalClearanceFixed()
    {
        // SLCONS (OBJL 122) → ShorelineConstruction, which binds
        // horizontalClearanceFixed (not open).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(122, Attr(98, "8.0")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("ShorelineConstruction", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var fixedClr = ComplexInstance(s101, feat.Attributes, "horizontalClearanceFixed", 1).ToList();
        Assert.NotEmpty(fixedClr);
        Assert.Equal("8.0", GetSubAttribute(s101, fixedClr, "horizontalClearanceValue"));
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "horizontalClearanceOpen", 1).ToList());
    }

    [Fact]
    public void Translate_TunnelWithHorclr_AssemblesHorizontalClearanceFixed()
    {
        // TUNNEL (OBJL 151) → Tunnel, which binds horizontalClearanceFixed.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(151, Attr(98, "6.25")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Tunnel", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var fixedClr = ComplexInstance(s101, feat.Attributes, "horizontalClearanceFixed", 1).ToList();
        Assert.Equal("6.25", GetSubAttribute(s101, fixedClr, "horizontalClearanceValue"));
    }

    // ── IENC tisdge → S-401 TimeScheduleInGeneral (#608) ──

    private const ushort ObjlBerth = 17010;
    private const ushort ObjlTimeSchedule = 17068;
    private const ushort ObjlCAsso = 401;
    private const int AttlCattab = 17092;
    private const int AttlSchref = 17093;
    private const int AttlUseshp = 17094;
    private const int AttlAptref = 17099;
    private const int AttlDirimp = 17056;
    private const int AttlShptyp = 33066;
    private const int AttlSordat = 147;

    private static EncDotNet.S57.S57FeatureRecord PointAt(
        uint recordId, ushort objectClass, uint featureId,
        IEnumerable<EncDotNet.S57.S57AttributeValue>? attributes = null,
        IEnumerable<EncDotNet.S57.S57FeaturePointer>? featurePointers = null)
        => Feat(recordId, 1, objectClass, featureIdentificationNumber: featureId,
            attributes: attributes, featurePointers: featurePointers,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });

    private static EncDotNet.S57.S57FeatureRecord Schedule(
        uint recordId, uint featureId, string shipType,
        IEnumerable<EncDotNet.S57.S57FeaturePointer>? featurePointers = null,
        params EncDotNet.S57.S57AttributeValue[] extra)
        => Feat(recordId, 255, ObjlTimeSchedule, featureIdentificationNumber: featureId,
            attributes: new[] { Attr(AttlCattab, "1"), Attr(AttlSchref, $"schedule-{shipType}.xml"), Attr(AttlShptyp, shipType), Attr(AttlUseshp, "2") }.Concat(extra),
            featurePointers: featurePointers);

    private static List<(string Code, string Value)> InfoAttributes(S101Document doc, S101InformationRecord record)
        => record.Attributes.Select(a => (doc.AttributeTypeCatalogue[a.NumericCode], a.Value)).ToList();

    private static List<S101InformationRecord> LinkedInformation(S101Document doc, S101FeatureRecord feature)
    {
        Assert.All(feature.InformationAssociations, ia =>
        {
            Assert.Equal("AdditionalInformation", doc.InformationAssociationCatalogue[ia.NumericCode]);
            Assert.Equal("theInformation", doc.RoleCatalogue[ia.RoleCode]);
        });
        return feature.InformationAssociations.Select(ia => doc.InformationTypes[ia.RecordId]).ToList();
    }

    [Fact]
    public void Translate_S401Target_TimeScheduleInCAsso_BecomesTimeScheduleInGeneral()
    {
        var berth = PointAt(1, ObjlBerth, 10, attributes: new[] { Attr(AttlObjnam, "Quay") });
        var schedule = Schedule(2, 20, "1", extra: new[]
        {
            Attr(AttlAptref, "passing.xml"), Attr(AttlDirimp, "1,2"), Attr(AttlSordat, "20240101"),
        });
        var association = Feat(3, 255, ObjlCAsso, featureIdentificationNumber: 30,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 20) });
        var diag = new S57TranslationDiagnostics();

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(new[] { Node(1, 0, 0) }, new[] { berth, schedule, association }), diag);

        var feature = Assert.Single(s401.Features);
        Assert.Equal("Berth", ClassOf(s401, feature));
        var record = Assert.Single(LinkedInformation(s401, feature));
        Assert.Equal("TimeScheduleInGeneral", s401.InformationTypeCatalogue[record.InformationTypeCode]);
        Assert.Equal(
            [
                ("categoryOfTimeAndBehaviour", "1"), ("timeScheduleReference", "schedule-1.xml"),
                ("typeOfShip", "1"), ("useOfShip", "2"), ("averagePassingTimeReference", "passing.xml"),
                ("directionOfImpact", "1"), ("directionOfImpact", "2"), ("reportedDate", "20240101"),
            ],
            InfoAttributes(s401, record));
        Assert.Single(s401.InformationTypes);
        Assert.Equal(1, diag.TimeSchedulesEmitted);
        Assert.Empty(diag.UnmappedObjectClasses);
        Assert.Empty(diag.RuleDroppedObjectClasses);
    }

    [Fact]
    public void Translate_S401Target_EveryLinkedScheduleIsAssociated()
    {
        // Two schedules (one per ship type) linked by pointers in both
        // directions, plus INFORM: three AdditionalInformation associations,
        // beyond S-401's [0..1], so that no schedule is lost.
        var berth = PointAt(1, ObjlBerth, 10,
            attributes: new[] { Attr(102, "Call ahead") },
            featurePointers: new[] { Ffpt(540, 20) });
        var cargo = Schedule(2, 20, "1");
        var leisure = Schedule(3, 21, "4", featurePointers: new[] { Ffpt(540, 10) });
        var diag = new S57TranslationDiagnostics();

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(new[] { Node(1, 0, 0) }, new[] { berth, cargo, leisure }), diag);

        var linked = LinkedInformation(s401, Assert.Single(s401.Features));
        Assert.Equal(
            ["NauticalInformation", "TimeScheduleInGeneral", "TimeScheduleInGeneral"],
            linked.Select(r => s401.InformationTypeCatalogue[r.InformationTypeCode]));
        Assert.Equal(["1", "4"], linked.Skip(1).Select(r => InfoAttributes(s401, r).Single(a => a.Code == "typeOfShip").Value));
        Assert.Equal(2, diag.TimeSchedulesEmitted);
    }

    [Fact]
    public void Translate_S401Target_ScheduleSharedByTwoFeatures_IsEmittedOnce()
    {
        var first = PointAt(1, ObjlBerth, 10);
        var second = PointAt(2, ObjlBerth, 11);
        var schedule = Schedule(3, 20, "1", featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(new[] { Node(1, 0, 0) }, new[] { first, second, schedule }));

        var record = Assert.Single(s401.InformationTypes).Key;
        Assert.Equal(2, s401.Features.Count);
        Assert.All(s401.Features, f => Assert.Equal(record, Assert.Single(f.InformationAssociations).RecordId));
    }

    [Fact]
    public void Translate_S401Target_ScheduleOnClassWithoutTimeSchedules_IsDropped()
    {
        // S-401 Bridge takes ServiceHours, not TimeScheduleInGeneral.
        Assert.False(S101FeatureAttributeBindings.ForSpec("S-401")
            .BindsInformationType("Bridge", "AdditionalInformation", "TimeScheduleInGeneral"));
        var vectors = new EncDotNet.S57.S57VectorRecord[] { Node(1, 0, 0), Node(2, 100, 100), Edge(10, 1, 2) };
        var bridge = Feat(1, 2, 17011, featureIdentificationNumber: 10,
            attributes: new[] { Attr(AttlCatbrg, "3") },
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 0, 0) });
        var schedule = Schedule(2, 20, "1", featurePointers: new[] { Ffpt(540, 10) });
        var diag = new S57TranslationDiagnostics();

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(vectors, new[] { bridge, schedule }), diag);

        Assert.Empty(Assert.Single(s401.Features).InformationAssociations);
        Assert.Empty(s401.InformationTypes);
        Assert.Equal(0, diag.TimeSchedulesEmitted);
        Assert.Equal(1, diag.RuleDroppedObjectClasses[ObjlTimeSchedule]);
    }

    [Fact]
    public void Translate_S401Target_ScheduleAttributesOnAFeature_AreDropped()
    {
        var diag = new S57TranslationDiagnostics();
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(ObjlBerth, Attr(AttlObjnam, "Quay"), Attr(AttlCattab, "1"), Attr(AttlShptyp, "2")), diag);

        var feature = Assert.Single(s401.Features);
        Assert.DoesNotContain(feature.Attributes, a => s401.AttributeTypeCatalogue[a.NumericCode] is "categoryOfTimeAndBehaviour" or "typeOfShip");
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlCattab]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlShptyp]);
    }

    [Fact]
    public void Translate_S401Target_ScheduleText_IsDroppedWithoutNauticalInformation()
    {
        var berth = PointAt(1, ObjlBerth, 10);
        var schedule = Schedule(2, 20, "1", new[] { Ffpt(540, 10) }, Attr(102, "Closed on holidays"));
        var diag = new S57TranslationDiagnostics();

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(new[] { Node(1, 0, 0) }, new[] { berth, schedule }), diag);

        var record = Assert.Single(s401.InformationTypes).Value;
        Assert.Equal("TimeScheduleInGeneral", s401.InformationTypeCatalogue[record.InformationTypeCode]);
        Assert.Equal(1, diag.RuleDroppedAttributes[102]);
        Assert.Equal(0, diag.NauticalInformationTypesEmitted);
    }

    [Fact]
    public void Translate_S101Target_TimeSchedule_IsUnmapped()
    {
        var berth = PointAt(1, 10, 10);
        var schedule = Schedule(2, 20, "1");
        var association = Feat(3, 255, ObjlCAsso, featureIdentificationNumber: 30,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 20) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(new[] { Node(1, 0, 0) }, new[] { berth, schedule, association }), diag);

        Assert.Empty(s101.InformationTypes);
        Assert.Equal(1, diag.UnmappedObjectClasses[ObjlTimeSchedule]);
        Assert.Equal(1, diag.UnmappedObjectClasses[ObjlCAsso]);
    }

    // ── IENC horcll / horclw → S-401 lock and dock dimensions (#608) ──

    private const int AttlHorcll = 17074;
    private const int AttlHorclw = 17075;

    private static string? SimpleValue(S101Document doc, S101FeatureRecord feat, string code)
        => feat.Attributes.Where(a => doc.AttributeTypeCatalogue[a.NumericCode] == code)
            .Select(a => a.Value).SingleOrDefault();

    [Fact]
    public void Translate_S401Target_LockBasin_HorcllIsLength_HorclwIsHorizontalClearanceFixed()
    {
        // Conversion guidance clause 3.78: S-401 LockBasin binds
        // horizontalClearanceLength but carries the width in horizontalClearanceFixed.
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            AreaFeatureWithS57Attributes(17016, Attr(AttlHorcll, "182.88"), Attr(AttlHorclw, "33.53")));

        var feat = Assert.Single(s401.Features);
        Assert.Equal("LockBasin", ClassOf(s401, feat));
        Assert.Equal("182.88", SimpleValue(s401, feat, "horizontalClearanceLength"));
        Assert.Null(SimpleValue(s401, feat, "horizontalClearanceWidth"));
        var fixedClr = ComplexInstance(s401, feat.Attributes, "horizontalClearanceFixed", 1).ToList();
        Assert.Equal("33.53", GetSubAttribute(s401, fixedClr, "horizontalClearanceValue"));
    }

    [Fact]
    public void Translate_S401Target_LockBasin_HorclrTakesPrecedenceOverHorclw()
    {
        var diag = new S57TranslationDiagnostics();
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            AreaFeatureWithS57Attributes(79, Attr(AttlHorclw, "33.53"), Attr(AttlHorclr, "30")), diag);

        var feat = Assert.Single(s401.Features);
        Assert.Equal("LockBasin", ClassOf(s401, feat));
        var fixedClr = ComplexInstance(s401, feat.Attributes, "horizontalClearanceFixed", 1).ToList();
        Assert.Equal("30", GetSubAttribute(s401, fixedClr, "horizontalClearanceValue"));
        Assert.Empty(ComplexInstance(s401, feat.Attributes, "horizontalClearanceFixed", 2).ToList());
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlHorclw]);
    }

    [Fact]
    public void Translate_S401Target_LockBasinPart_CarriesLengthAndWidth()
    {
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            AreaFeatureWithS57Attributes(17058, Attr(AttlHorcll, "109.73"), Attr(AttlHorclw, "17.07")));

        var feat = Assert.Single(s401.Features);
        Assert.Equal("LockBasinPart", ClassOf(s401, feat));
        Assert.Equal("109.73", SimpleValue(s401, feat, "horizontalClearanceLength"));
        Assert.Equal("17.07", SimpleValue(s401, feat, "horizontalClearanceWidth"));
        Assert.Empty(ComplexInstance(s401, feat.Attributes, "horizontalClearanceFixed", 1).ToList());
    }

    [Fact]
    public void Translate_S401Target_HorcllOnClassNotBindingIt_IsDropped()
    {
        // S-401 NoticeMark binds neither dimension nor a clearance complex.
        var diag = new S57TranslationDiagnostics();
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17050, Attr(17052, "8"), Attr(AttlHorcll, "10"), Attr(AttlHorclw, "5")), diag);

        var feat = Assert.Single(s401.Features);
        Assert.Equal("NoticeMark", ClassOf(s401, feat));
        Assert.Equal("categoryOfNoticeMark", s401.AttributeTypeCatalogue[Assert.Single(feat.Attributes).NumericCode]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlHorcll]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlHorclw]);
    }

    // ── IENC shore power → S-401 powerCharacteristics (#608) ──

    private const int AttlBunves = 17065;
    private const int AttlCatbun = 17067;
    private const int AttlCatfrq = 18030;
    private const int AttlCatvol = 18031;
    private const int AttlAmoamp = 18032;
    private const int AttlAllcon = 18033;
    private const int AttlCatplg = 18034;
    private const int AttlShrnum = 18035;

    private static readonly string[] PowerSubAttributes =
    [
        "categoryOfVoltage", "categoryOfFrequency", "amountOfAmperage",
        "categoryOfPlug", "numberOfShoreConnectors", "allowedConsumption",
    ];

    [Fact]
    public void Translate_S401Target_BunkerStation_AssemblesPowerCharacteristics()
    {
        // Conversion guidance clause 3.10. catvol lists two voltages, so two
        // instances are emitted, each with the station-wide values.
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17054,
                Attr(AttlBunves, "2"), Attr(AttlCatbun, "4"),
                Attr(AttlCatvol, "1,2"), Attr(AttlCatfrq, "1"), Attr(AttlAmoamp, "300"),
                Attr(AttlAllcon, "1000"), Attr(AttlCatplg, "CEE"), Attr(AttlShrnum, "4")));

        var feat = Assert.Single(s401.Features);
        Assert.Equal("BunkerStation", ClassOf(s401, feat));
        Assert.DoesNotContain(feat.Attributes.TakeWhile(
                a => s401.AttributeTypeCatalogue[a.NumericCode] != "powerCharacteristics"),
            a => PowerSubAttributes.Contains(s401.AttributeTypeCatalogue[a.NumericCode]));

        var first = ComplexInstance(s401, feat.Attributes, "powerCharacteristics", 1).ToList();
        var second = ComplexInstance(s401, feat.Attributes, "powerCharacteristics", 2).ToList();
        Assert.Empty(ComplexInstance(s401, feat.Attributes, "powerCharacteristics", 3));
        Assert.Equal("1", GetSubAttribute(s401, first, "categoryOfVoltage"));
        Assert.Equal("2", GetSubAttribute(s401, second, "categoryOfVoltage"));
        foreach (var instance in new[] { first, second })
        {
            Assert.Equal("1", GetSubAttribute(s401, instance, "categoryOfFrequency"));
            Assert.Equal("300", GetSubAttribute(s401, instance, "amountOfAmperage"));
            Assert.Equal("CEE", GetSubAttribute(s401, instance, "categoryOfPlug"));
            Assert.Equal("4", GetSubAttribute(s401, instance, "numberOfShoreConnectors"));
            Assert.Equal("1000", GetSubAttribute(s401, instance, "allowedConsumption"));
        }
    }

    [Fact]
    public void Translate_S401Target_BunkerStation_DropsDisallowedVoltage()
    {
        var diag = new S57TranslationDiagnostics();
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17054,
                Attr(AttlBunves, "2"), Attr(AttlCatvol, "9"), Attr(AttlCatfrq, "1,2")), diag);

        var feat = Assert.Single(s401.Features);
        var first = ComplexInstance(s401, feat.Attributes, "powerCharacteristics", 1).ToList();
        var second = ComplexInstance(s401, feat.Attributes, "powerCharacteristics", 2).ToList();
        Assert.Null(GetSubAttribute(s401, first, "categoryOfVoltage"));
        Assert.Equal("1", GetSubAttribute(s401, first, "categoryOfFrequency"));
        Assert.Equal("2", GetSubAttribute(s401, second, "categoryOfFrequency"));
        Assert.Equal(1, diag.DroppedEnumValues[new S57EnumValueDrop("categoryOfVoltage", "9")]);
    }

    [Fact]
    public void Translate_S401Target_BunkerStationWithoutShorePower_HasNoPowerCharacteristics()
    {
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17054, Attr(AttlBunves, "1"), Attr(AttlCatbun, "1"), Attr(AttlCatvol, "")));

        var feat = Assert.Single(s401.Features);
        Assert.DoesNotContain(feat.Attributes,
            a => s401.AttributeTypeCatalogue[a.NumericCode] == "powerCharacteristics");
    }

    [Fact]
    public void Translate_S401Target_ShorePowerOnClassNotBindingIt_IsDropped()
    {
        var diag = new S57TranslationDiagnostics();
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17050, Attr(17052, "8"), Attr(AttlCatvol, "1"), Attr(AttlShrnum, "2")), diag);

        var feat = Assert.Single(s401.Features);
        Assert.Equal("NoticeMark", ClassOf(s401, feat));
        Assert.Equal("categoryOfNoticeMark", s401.AttributeTypeCatalogue[Assert.Single(feat.Attributes).NumericCode]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlCatvol]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlShrnum]);
    }

    // ── BRIDGE → Bridge + SpanFixed / SpanOpening (S-65 Annex B §4.8.10) ──

    // S-57 attribute codes used by the bridge tests.
    private const int AttlCatbrg = 9;
    private const int AttlConvis = 83;
    private const int AttlHoracc = 97;
    private const int AttlHorclr = 98;
    private const int AttlObjnam = 116;
    private const int AttlScamin = 133;
    private const int AttlVeracc = 180;
    private const int AttlVerclr = 181;
    private const int AttlVerccl = 182;
    private const int AttlVercop = 183;
    private const int AttlVerdat = 185;

    // Complex attributes a span may carry; used to delimit instances.
    private static readonly string[] SpanComplexes =
    [
        "fixedDateRange", "horizontalClearanceFixed", "verticalClearanceFixed",
        "verticalClearanceClosed", "verticalClearanceOpen",
    ];

    private static IReadOnlyList<S101Attribute> SpanComplex(
        S101Document doc, S101FeatureRecord feat, string complexCode)
        => ComplexInstanceStrict(doc, feat.Attributes, complexCode, 1, SpanComplexes).ToList();

    // Top-level value of a simple attribute (first occurrence), or null.
    private static string? TopLevelValue(S101Document doc, S101FeatureRecord feat, string code)
        => GetSubAttribute(doc, feat.Attributes, code);

    private static IEnumerable<string> AttributeNames(S101Document doc, S101FeatureRecord feat)
        => feat.Attributes.Select(a => doc.AttributeTypeCatalogue[a.NumericCode]);

    private static S101FeatureRecord SingleOfClass(S101Document doc, string s101Class)
        => Assert.Single(doc.Features, f => ClassOf(doc, f) == s101Class);

    private static void AssertBridgeComponents(
        S101Document doc, S101FeatureRecord bridge, params S101FeatureRecord[] components)
    {
        Assert.Equal(components.Length, bridge.FeatureAssociations.Count);
        foreach (var fa in bridge.FeatureAssociations)
        {
            Assert.Equal("BridgeAggregation", doc.FeatureAssociationCatalogue[fa.NumericCode]);
            Assert.Equal("theComponent", doc.RoleCatalogue[fa.RoleCode]);
        }
        Assert.Equal(
            components.Select(c => c.RecordId).OrderBy(i => i),
            bridge.FeatureAssociations.Select(a => a.RecordId).OrderBy(i => i));
    }

    [Fact]
    public void Translate_FixedBridgeWithClearance_EmitsBridgeAndSpanFixed()
    {
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11,
                Attr(AttlCatbrg, "1"),
                Attr(AttlVerclr, "12.4"),
                Attr(AttlVeracc, "0.5"),
                Attr(AttlHorclr, "30"),
                Attr(AttlHoracc, "2"),
                Attr(AttlVerdat, "24"),
                Attr(AttlScamin, "22000"),
                Attr(AttlObjnam, "Test Bridge")),
            diag);

        Assert.Equal(2, s101.Features.Count);
        var bridge = SingleOfClass(s101, "Bridge");
        var span = SingleOfClass(s101, "SpanFixed");

        // Both features share the S-57 geometry and identity; the Bridge is the
        // collection end of the BridgeAggregation.
        Assert.Equal(bridge.SpatialAssociations, span.SpatialAssociations);
        Assert.Equal(bridge.FeatureIdentificationNumber, span.FeatureIdentificationNumber);
        AssertBridgeComponents(s101, bridge, span);
        Assert.Empty(span.FeatureAssociations);

        // Clearances, accuracies and vertical datum live on the span only.
        var vertical = SpanComplex(s101, span, "verticalClearanceFixed");
        Assert.Equal("12.4", GetSubAttribute(s101, vertical, "verticalClearanceValue"));
        Assert.Equal("0.5", GetSubAttribute(s101, vertical, "uncertaintyFixed"));
        Assert.NotNull(GetSubAttribute(s101, vertical, "verticalUncertainty"));
        var horizontal = SpanComplex(s101, span, "horizontalClearanceFixed");
        Assert.Equal("30", GetSubAttribute(s101, horizontal, "horizontalClearanceValue"));
        Assert.Equal("2", GetSubAttribute(s101, horizontal, "horizontalDistanceUncertainty"));
        Assert.Equal("24", TopLevelValue(s101, span, "verticalDatum"));
        Assert.Equal("22000", TopLevelValue(s101, span, "scaleMinimum"));
        Assert.DoesNotContain("featureName", AttributeNames(s101, span));

        var bridgeAttrs = AttributeNames(s101, bridge).ToList();
        Assert.Contains("featureName", bridgeAttrs);
        Assert.Contains("scaleMinimum", bridgeAttrs);
        foreach (var spanOnly in new[]
        {
            "verticalClearanceValue", "verticalClearanceFixed", "verticalClearanceClosed",
            "verticalClearanceOpen", "horizontalClearanceFixed", "horizontalClearanceValue",
            "verticalDatum", "uncertaintyFixed", "horizontalDistanceUncertainty",
        })
        {
            Assert.DoesNotContain(spanOnly, bridgeAttrs);
        }

        Assert.Equal(1, diag.FeaturesEmitted);
        Assert.Equal(1, diag.BridgeSpansEmitted);
        Assert.Empty(diag.RuleDroppedAttributes);
        Assert.DoesNotContain(diag.UnmappedAttributes.Keys, k => k.AttributeCode is AttlHorclr or AttlHoracc or AttlVeracc);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("5")]
    [InlineData("7")]
    [InlineData("2,6")]
    [InlineData("10,4")]
    public void Translate_OpeningBridge_EmitsSpanOpening(string catbrg)
    {
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(AttlCatbrg, catbrg), Attr(AttlVerccl, "5.5")));

        var bridge = SingleOfClass(s101, "Bridge");
        var span = SingleOfClass(s101, "SpanOpening");
        AssertBridgeComponents(s101, bridge, span);

        var closed = SpanComplex(s101, span, "verticalClearanceClosed");
        Assert.Equal("5.5", GetSubAttribute(s101, closed, "verticalClearanceValue"));

        // No VERCOP: the open clearance is unlimited and carries no value.
        var open = SpanComplex(s101, span, "verticalClearanceOpen");
        Assert.Equal("true", GetSubAttribute(s101, open, "verticalClearanceUnlimited"));
        Assert.Null(GetSubAttribute(s101, open, "verticalClearanceValue"));
        Assert.Empty(SpanComplex(s101, span, "verticalClearanceFixed"));
    }

    [Theory]
    [InlineData("6")]
    [InlineData("1")]
    [InlineData("8,9")]
    // A bridge arch (13, an IENC extension) is a fixed span — IEHG
    // "S-57 ENC to S-401 Conversion Guidance" Ed 1.3.0 draft 2, clause 3.144.
    [InlineData("13")]
    [InlineData("13,1")]
    public void Translate_NonOpeningBridgeCategory_EmitsSpanFixed(string catbrg)
    {
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(AttlCatbrg, catbrg), Attr(AttlVerclr, "9")));

        var span = SingleOfClass(s101, "SpanFixed");
        Assert.Equal("9", GetSubAttribute(s101, SpanComplex(s101, span, "verticalClearanceFixed"), "verticalClearanceValue"));
        Assert.DoesNotContain(s101.Features, f => ClassOf(s101, f) == "SpanOpening");
    }

    [Fact]
    public void Translate_OpeningBridgeWithVercop_OpenClearanceIsLimited()
    {
        var s101 = new S57ToS101Translator().Translate(
            AreaFeatureWithS57Attributes(11,
                Attr(AttlCatbrg, "3"),
                Attr(AttlVerccl, "4"),
                Attr(AttlVercop, "40"),
                Attr(AttlVeracc, "0.1")));

        var bridge = SingleOfClass(s101, "Bridge");
        var span = SingleOfClass(s101, "SpanOpening");
        Assert.Equal((byte)130, bridge.SpatialAssociations.Single().RecordName);
        Assert.Equal(bridge.SpatialAssociations, span.SpatialAssociations);

        var open = SpanComplex(s101, span, "verticalClearanceOpen");
        Assert.Equal("false", GetSubAttribute(s101, open, "verticalClearanceUnlimited"));
        Assert.Equal("40", GetSubAttribute(s101, open, "verticalClearanceValue"));
        Assert.Equal("0.1", GetSubAttribute(s101, open, "uncertaintyFixed"));
        var closed = SpanComplex(s101, span, "verticalClearanceClosed");
        Assert.Equal("0.1", GetSubAttribute(s101, closed, "uncertaintyFixed"));
    }

    [Fact]
    public void Translate_OpeningBridgeWithEmptyVercop_OpenClearanceIsLimitedWithoutValue()
    {
        // VERCOP populated with an empty (null) value → unlimited = false.
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11,
                Attr(AttlCatbrg, "2"), Attr(AttlVerccl, "4"), Attr(AttlVercop, "")));

        var span = SingleOfClass(s101, "SpanOpening");
        var open = SpanComplex(s101, span, "verticalClearanceOpen");
        Assert.Equal("false", GetSubAttribute(s101, open, "verticalClearanceUnlimited"));
        Assert.Null(GetSubAttribute(s101, open, "verticalClearanceValue"));
    }

    [Fact]
    public void Translate_FixedBridgeWithEmptyVerclr_EmitsSpanWithUnknownClearance()
    {
        // An S-57 empty (unknown) VERCLR still asserts a clearance: the span is
        // emitted with its mandatory value populated as empty (null), and
        // VERACC — which has no known value to qualify — is dropped.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11,
                Attr(AttlCatbrg, "1"), Attr(AttlVerclr, ""), Attr(AttlVeracc, "0.5"), Attr(AttlHorclr, "")),
            diag);

        var bridge = SingleOfClass(s101, "Bridge");
        var span = SingleOfClass(s101, "SpanFixed");
        AssertBridgeComponents(s101, bridge, span);

        var vertical = SpanComplex(s101, span, "verticalClearanceFixed");
        Assert.Equal(string.Empty, GetSubAttribute(s101, vertical, "verticalClearanceValue"));
        Assert.Null(GetSubAttribute(s101, vertical, "verticalUncertainty"));
        Assert.Empty(SpanComplex(s101, span, "horizontalClearanceFixed"));

        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVeracc]);
        Assert.False(diag.RuleDroppedAttributes.ContainsKey(AttlVerclr));
        Assert.False(diag.RuleDroppedAttributes.ContainsKey(AttlHorclr));
    }

    [Fact]
    public void Translate_BridgeWithOnlyEmptyClearances_AndNoMandatoryOne_RecordsNothingDropped()
    {
        // An opening bridge with an empty VERCLR (but no VERCCL) has no span;
        // the empty value carries no data, so no drop is recorded.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(AttlCatbrg, "2"), Attr(AttlVerclr, "")), diag);

        Assert.Equal("Bridge", ClassOf(s101, Assert.Single(s101.Features)));
        Assert.Empty(diag.RuleDroppedAttributes);
    }

    [Fact]
    public void Translate_BridgeWithoutClearance_EmitsBridgeOnly()
    {
        // No clearance → treated as not crossing navigable water.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(AttlCatbrg, "1")), diag);

        var bridge = Assert.Single(s101.Features);
        Assert.Equal("Bridge", ClassOf(s101, bridge));
        Assert.Empty(bridge.FeatureAssociations);
        Assert.Empty(s101.FeatureAssociationCatalogue);
        Assert.Equal(0, diag.BridgeSpansEmitted);
    }

    [Fact]
    public void Translate_BridgeWithHorclrOnly_EmitsNoSpan_AndRecordsHorclrDropped()
    {
        // HORCLR alone cannot form a SpanFixed (verticalClearanceFixed is
        // mandatory) and Bridge binds no horizontal clearance, so the value is
        // recorded as rule-dropped rather than emitted anywhere.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(AttlHorclr, "12.5")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Bridge", ClassOf(s101, feat));
        Assert.Empty(feat.Attributes);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlHorclr]);
        Assert.DoesNotContain(diag.UnmappedAttributes.Keys, k => k.AttributeCode == AttlHorclr);
    }

    [Fact]
    public void Translate_OpeningBridgeWithOnlyVerclr_EmitsNoSpan()
    {
        // An opening span needs VERCCL; VERCLR has no home on SpanOpening or Bridge.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(AttlCatbrg, "4"), Attr(AttlVerclr, "7")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Bridge", ClassOf(s101, feat));
        Assert.DoesNotContain("verticalClearanceValue", AttributeNames(s101, feat));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVerclr]);
    }

    [Fact]
    public void Translate_PointBridge_BecomesLandmark()
    {
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(11,
                Attr(AttlCatbrg, "2"), Attr(AttlVerccl, "4"), Attr(AttlVerclr, "5"), Attr(AttlVerdat, "24")),
            diag);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Landmark", ClassOf(s101, feat));
        Assert.Empty(feat.FeatureAssociations);
        Assert.Equal("26", TopLevelValue(s101, feat, "categoryOfLandmark"));
        Assert.Equal("2", TopLevelValue(s101, feat, "visualProminence"));
        Assert.Equal(
            new[] { "categoryOfLandmark", "visualProminence" },
            AttributeNames(s101, feat));
        Assert.Equal(0, diag.BridgeSpansEmitted);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlCatbrg]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVerclr]);
    }

    [Fact]
    public void Translate_PointBridgeWithConvis_KeepsVisualProminence()
    {
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(11, Attr(AttlConvis, "1")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Landmark", ClassOf(s101, feat));
        Assert.Equal("26", TopLevelValue(s101, feat, "categoryOfLandmark"));
        Assert.Equal("1", Assert.Single(feat.Attributes, a =>
            s101.AttributeTypeCatalogue[a.NumericCode] == "visualProminence").Value);
    }

    // Three nodes on a line, two edges joining them (1→2, 2→3), and a node for
    // a pylon. Each BRIDGE member spans one edge.
    private static (EncDotNet.S57.S57VectorRecord[] Vectors, EncDotNet.S57.S57FeatureRecord FixedSpan,
        EncDotNet.S57.S57FeatureRecord OpeningSpan, EncDotNet.S57.S57FeatureRecord Pylon) TwoSpanBridgeParts(
        bool reverseSecondEdgeOrder = false)
    {
        var vectors = new[]
        {
            Node(1, 0, 0), Node(2, 0, 100), Node(3, 0, 200), Node(4, 5, 100, RcnmIsolatedNode),
            Edge(10, 1, 2), Edge(11, 2, 3),
        };
        var fixedSpan = Feat(1, 2, 11, featureIdentificationNumber: 10,
            attributes: new[] { Attr(AttlCatbrg, "1"), Attr(AttlVerclr, "20"), Attr(AttlScamin, "45000") },
            spatialPointers: new[] { Sp(RcnmEdge, reverseSecondEdgeOrder ? 11u : 10u, 1, 0, 0) });
        var openingSpan = Feat(2, 2, 11, featureIdentificationNumber: 11,
            attributes: new[] { Attr(AttlCatbrg, "2,3"), Attr(AttlVerccl, "6"), Attr(AttlScamin, "45000") },
            spatialPointers: new[] { Sp(RcnmEdge, reverseSecondEdgeOrder ? 10u : 11u, 1, 0, 0) });
        var pylon = Feat(3, 1, 98, featureIdentificationNumber: 12,
            attributes: new[] { Attr(49, "2") }, // CATPYL
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 4, 1, 0, 0) });
        return (vectors, fixedSpan, openingSpan, pylon);
    }

    [Fact]
    public void Translate_BridgeCAggr_EmitsSingleBridgeWithSpanAndPylonComponents()
    {
        var (vectors, fixedSpan, openingSpan, pylon) = TwoSpanBridgeParts();
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            attributes: new[] { Attr(AttlObjnam, "Harbour Bridge") },
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 12) });
        var diag = new S57TranslationDiagnostics();

        // C_AGGR first in document order: members are resolved regardless.
        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { aggr, fixedSpan, openingSpan, pylon }), diag);

        var bridge = SingleOfClass(s101, "Bridge");
        var spanFixed = SingleOfClass(s101, "SpanFixed");
        var spanOpening = SingleOfClass(s101, "SpanOpening");
        var pylonFeature = SingleOfClass(s101, "PylonBridgeSupport");
        Assert.Equal(4, s101.Features.Count);

        // The aggregated Bridge takes its identity from the C_AGGR and links
        // every component.
        Assert.Equal(99u, bridge.FeatureIdentificationNumber);
        AssertBridgeComponents(s101, bridge, spanFixed, spanOpening, pylonFeature);
        Assert.Equal(10u, spanFixed.FeatureIdentificationNumber);
        Assert.Equal(11u, spanOpening.FeatureIdentificationNumber);

        // Name comes from the C_AGGR; other Bridge attributes from the
        // representative (opening) member.
        var name = ComplexInstance(s101, bridge.Attributes, "featureName", 1).ToList();
        Assert.Equal("Harbour Bridge", GetSubAttribute(s101, name, "name"));
        Assert.Equal("45000", TopLevelValue(s101, bridge, "scaleMinimum"));
        Assert.DoesNotContain("verticalClearanceClosed", AttributeNames(s101, bridge));

        // Geometry: the two member edges chained into one curve.
        Assert.Equal(
            new[] { (120, 1u, 1), (120, 2u, 1) },
            bridge.SpatialAssociations.Select(a => ((int)a.RecordName, a.RecordId, (int)a.Orientation)));
        Assert.Equal(1u, spanFixed.SpatialAssociations.Single().RecordId);

        Assert.Equal(1, diag.BridgeAggregationsEmitted);
        Assert.Equal(2, diag.BridgeSpansEmitted);
        Assert.Equal(0, diag.RangeSystemsEmitted);
        Assert.False(diag.UnmappedObjectClasses.ContainsKey(400));
    }

    [Fact]
    public void Translate_BridgeCAggr_OutOfOrderEdges_ChainIntoOneCurve()
    {
        // The first member references the far edge (2→3): the chain must still
        // start at a free end and walk both edges.
        var (vectors, fixedSpan, openingSpan, _) = TwoSpanBridgeParts(reverseSecondEdgeOrder: true);
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { fixedSpan, openingSpan, aggr }));

        var bridge = SingleOfClass(s101, "Bridge");
        Assert.Equal(2, bridge.SpatialAssociations.Count);
        Assert.Equal(new[] { 1u, 2u }, bridge.SpatialAssociations.Select(a => a.RecordId).Order());
        // The chained edges connect head-to-tail.
        var first = bridge.SpatialAssociations[0];
        var second = bridge.SpatialAssociations[1];
        // The node a traversal leaves from (trailing = false) or arrives at (trailing = true).
        uint EndOf(S101SpatialAssociation a, bool trailing)
        {
            var seg = s101.CurveSegments[a.RecordId];
            var wantBegin = (a.Orientation == 1) != trailing;
            return seg.PointAssociations.Single(p => p.Topology == (wantBegin ? 1 : 2)).RecordId;
        }
        Assert.Equal(EndOf(first, trailing: true), EndOf(second, trailing: false));
    }

    [Fact]
    public void Translate_BridgeCAggr_NameFromRepresentativeMember_WhenAggregateUnnamed()
    {
        var (vectors, fixedSpan, openingSpan, _) = TwoSpanBridgeParts();
        var namedFixed = Feat(1, 2, 11, featureIdentificationNumber: 10,
            attributes: fixedSpan.Attributes.Append(Attr(AttlObjnam, "Old Bridge")),
            spatialPointers: fixedSpan.SpatialPointers);
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { namedFixed, openingSpan, aggr }));

        var bridge = SingleOfClass(s101, "Bridge");
        var name = ComplexInstance(s101, bridge.Attributes, "featureName", 1).ToList();
        Assert.Equal("Old Bridge", GetSubAttribute(s101, name, "name"));
        Assert.Equal(1, CountComplexInstances(s101, bridge.Attributes, "featureName"));
    }

    [Fact]
    public void Translate_BridgeCAggr_DisjointCurves_ConvertMemberByMember_LightsLinkToTheNearestBridge()
    {
        // Until a multi-part Bridge is supported, a collection whose parts do
        // not join converts member by member: each Bridge keeps its geometry
        // (so its name is drawn) and each light links to the nearest member.
        var vectors = new[]
        {
            Node(1, 0, 0), Node(2, 0, 100), Node(3, 50, 0), Node(4, 50, 100),
            Node(5, 1, 50, RcnmIsolatedNode), Node(6, 49, 50, RcnmIsolatedNode),
            Edge(10, 1, 2), Edge(11, 3, 4),
        };
        var a = Feat(1, 2, 11, featureIdentificationNumber: 10,
            attributes: new[] { Attr(AttlVerclr, "20"), Attr(AttlObjnam, "North Bridge") },
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 0, 0) });
        var b = Feat(2, 2, 11, featureIdentificationNumber: 11,
            attributes: new[] { Attr(AttlObjnam, "South Bridge") },
            spatialPointers: new[] { Sp(RcnmEdge, 11, 1, 0, 0) });
        var nearA = Feat(3, 1, 75, featureIdentificationNumber: 20,
            attributes: new[] { Attr(107, "1"), Attr(75, "4") },
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 5, 1, 0, 0) });
        var nearB = Feat(4, 1, 75, featureIdentificationNumber: 21,
            attributes: new[] { Attr(107, "1"), Attr(75, "3") },
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 6, 1, 0, 0) });
        var aggr = Feat(5, 255, 400, featureIdentificationNumber: 99,
            attributes: new[] { Attr(AttlObjnam, "Twin Bridges") },
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 20), Ffpt(540, 21) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { a, b, nearA, nearB, aggr }), diag);

        var bridges = s101.Features.Where(f => ClassOf(s101, f) == "Bridge").ToList();
        Assert.Equal(new[] { 10u, 11u }, bridges.Select(f => f.FeatureIdentificationNumber).Order());
        Assert.All(bridges, f => Assert.NotEmpty(f.SpatialAssociations));
        var bridgeA = bridges.Single(f => f.FeatureIdentificationNumber == 10);
        var bridgeB = bridges.Single(f => f.FeatureIdentificationNumber == 11);

        uint LightOf(uint fid) => s101.Features.Single(f => f.FeatureIdentificationNumber == fid).RecordId;
        IEnumerable<uint> Equipment(S101FeatureRecord f) => f.FeatureAssociations
            .Where(x => s101.FeatureAssociationCatalogue[x.NumericCode] == "StructureEquipment")
            .Select(x => x.RecordId);
        Assert.Equal(new[] { LightOf(20) }, Equipment(bridgeA));
        Assert.Equal(new[] { LightOf(21) }, Equipment(bridgeB));

        // Bridge A keeps its span component alongside the light.
        AssertBridgeComponentsIncluding(s101, bridgeA, SingleOfClass(s101, "SpanFixed"));
        Assert.Equal(0, diag.BridgeAggregationsEmitted);
        Assert.Equal(1, diag.BridgeCollectionsUnjoined);
        Assert.Equal(2, diag.BridgeEquipmentLinked);
        Assert.Equal(1, diag.UnmappedObjectClasses[400]);
    }

    private static void AssertBridgeComponentsIncluding(
        S101Document doc, S101FeatureRecord bridge, params S101FeatureRecord[] components)
        => Assert.Equal(
            components.Select(c => c.RecordId).Order(),
            bridge.FeatureAssociations
                .Where(a => doc.FeatureAssociationCatalogue[a.NumericCode] == "BridgeAggregation")
                .Select(a => a.RecordId).Order());

    [Theory]
    [InlineData("S-101", null)]
    [InlineData("S-401", "3")]
    public void Translate_BridgeCAggr_KeepsDistinctMemberNamesAsNonDisplayNames(string spec, string? nameUsage)
    {
        // The Bridge carries the C_AGGR's name (IENC Encoding Guide 2.4.1
        // bridge clause I); a member's own, different name is kept as a further
        // featureName that is not for chart display. Spans bind no featureName.
        var target = spec == "S-401" ? S57TranslationTarget.S401 : S57TranslationTarget.S101;
        var (vectors, fixedSpan, openingSpan, _) = TwoSpanBridgeParts();
        fixedSpan = Feat(1, 2, 11, featureIdentificationNumber: 10,
            attributes: [.. fixedSpan.Attributes, Attr(AttlObjnam, "Old Swing Bridge")],
            spatialPointers: fixedSpan.SpatialPointers);
        openingSpan = Feat(2, 2, 11, featureIdentificationNumber: 11,
            attributes: [.. openingSpan.Attributes, Attr(AttlObjnam, "Harbour Bridge")],
            spatialPointers: openingSpan.SpatialPointers);
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            attributes: new[] { Attr(AttlObjnam, "Harbour Bridge") },
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });

        var doc = S57ToS101Translator.ForTarget(target).Translate(
            BuildDocument(vectors, new[] { fixedSpan, openingSpan, aggr }));

        var bridge = SingleOfClass(doc, "Bridge");
        var first = ComplexInstance(doc, bridge.Attributes, "featureName", 1).ToList();
        Assert.Equal("Harbour Bridge", GetSubAttribute(doc, first, "name"));
        Assert.Null(GetSubAttribute(doc, first, "nameUsage"));
        var second = ComplexInstance(doc, bridge.Attributes, "featureName", 2).ToList();
        Assert.Equal("Old Swing Bridge", GetSubAttribute(doc, second, "name"));
        Assert.Equal("eng", GetSubAttribute(doc, second, "language"));
        Assert.Equal(nameUsage, GetSubAttribute(doc, second, "nameUsage"));
        Assert.Empty(ComplexInstance(doc, bridge.Attributes, "featureName", 3));
    }

    [Fact]
    public void Translate_BridgeCAggr_AdjacentSurfaces_DissolveSharedEdge()
    {
        // Two triangles sharing edge 11 (2→3) form one quadrilateral outline.
        var vectors = new[]
        {
            Node(1, 0, 0), Node(2, 0, 100), Node(3, 100, 100), Node(4, 100, 0),
            Edge(10, 1, 2), Edge(11, 2, 3), Edge(12, 3, 1), Edge(13, 3, 4), Edge(14, 4, 1),
        };
        var left = Feat(1, 3, 11, featureIdentificationNumber: 10,
            attributes: new[] { Attr(AttlVerclr, "20") },
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 1, 0), Sp(RcnmEdge, 11, 1, 1, 0), Sp(RcnmEdge, 12, 1, 1, 0) });
        var right = Feat(2, 3, 11, featureIdentificationNumber: 11,
            attributes: new[] { Attr(AttlVerclr, "15") },
            spatialPointers: new[] { Sp(RcnmEdge, 12, 2, 1, 0), Sp(RcnmEdge, 13, 1, 1, 0), Sp(RcnmEdge, 14, 1, 1, 0) });
        var aggr = Feat(3, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });

        var s101 = new S57ToS101Translator().Translate(BuildDocument(vectors, new[] { left, right, aggr }));

        var bridge = SingleOfClass(s101, "Bridge");
        var surfaceId = Assert.Single(bridge.SpatialAssociations).RecordId;
        var ring = Assert.Single(s101.Surfaces[surfaceId].RingAssociations);
        var edges = s101.CompositeCurves[ring.RecordId].CurveComponents.Select(c => c.RecordId).Order();
        // Edge 12 (S-101 curve 3) is shared and dissolved.
        Assert.Equal(new[] { 1u, 2u, 4u, 5u }, edges);
        Assert.Equal(2, s101.Features.Count(f => ClassOf(s101, f) == "SpanFixed"));
    }

    // ── IENC c_brga → S-401 BridgeArchAssociation (#608) ──

    private const ushort ObjlInlandBridge = 17011;
    private const ushort ObjlBridgeArch = 18003;

    // Three adjacent inland arch pieces (bridge, CATBRG 13) with clearances,
    // feature ids 10–12, on edges 10–12 of a straight line.
    private static (EncDotNet.S57.S57VectorRecord[] Vectors, EncDotNet.S57.S57FeatureRecord[] Pieces) ArchPieces(
        params string?[] clearances)
    {
        var vectors = new List<EncDotNet.S57.S57VectorRecord> { Node(1, 0, 0) };
        var pieces = new List<EncDotNet.S57.S57FeatureRecord>();
        for (int i = 0; i < clearances.Length; i++)
        {
            vectors.Add(Node((uint)(i + 2), 0, (i + 1) * 10));
            vectors.Add(Edge((uint)(10 + i), (uint)(i + 1), (uint)(i + 2)));
            var attrs = new List<EncDotNet.S57.S57AttributeValue> { Attr(AttlCatbrg, "13") };
            if (clearances[i] is { } clearance)
                attrs.Add(Attr(AttlVerclr, clearance));
            pieces.Add(Feat((uint)(i + 1), 2, ObjlInlandBridge, featureIdentificationNumber: (uint)(10 + i),
                attributes: attrs, spatialPointers: new[] { Sp(RcnmEdge, (uint)(10 + i), 1, 0, 0) }));
        }
        return (vectors.ToArray(), pieces.ToArray());
    }

    private static void AssertArchComponents(
        S101Document doc, S101FeatureRecord head, params S101FeatureRecord[] components)
    {
        var arch = head.FeatureAssociations
            .Where(fa => doc.FeatureAssociationCatalogue[fa.NumericCode] == "BridgeArchAssociation")
            .ToList();
        Assert.All(arch, fa => Assert.Equal("theComponent", doc.RoleCatalogue[fa.RoleCode]));
        Assert.Equal(components.Select(c => c.RecordId), arch.Select(fa => fa.RecordId));
    }

    [Fact]
    public void Translate_S401Target_BridgeArch_LinksItsFixedSpans()
    {
        var (vectors, pieces) = ArchPieces("7.1", "8.4", "7.2");
        var arch = Feat(9, 255, ObjlBridgeArch, featureIdentificationNumber: 99,
            attributes: new[] { Attr(AttlObjnam, "Arch") },
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 12) });
        var diag = new S57TranslationDiagnostics();

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(vectors, pieces.Append(arch)), diag);

        var spans = s401.Features.Where(f => ClassOf(s401, f) == "SpanFixed").ToList();
        Assert.Equal(3, spans.Count);
        Assert.Equal(3, s401.Features.Count(f => ClassOf(s401, f) == "Bridge"));
        Assert.Equal(6, s401.Features.Count);
        AssertArchComponents(s401, spans[0], spans[1], spans[2]);
        Assert.Empty(spans[1].FeatureAssociations);
        Assert.Empty(spans[2].FeatureAssociations);
        Assert.False(diag.RuleDroppedObjectClasses.ContainsKey(ObjlBridgeArch));
        Assert.False(diag.UnmappedObjectClasses.ContainsKey(ObjlBridgeArch));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlObjnam]); // the arch's name has no home on a span
    }

    [Fact]
    public void Translate_S401Target_BridgeArchInsideBridgeCAggr_LinksSpansOfTheAggregatedBridge()
    {
        // The arch pieces belong to the bridge's C_AGGR; the c_brga is kept
        // out of it (IENC Encoding Guide G.1.2).
        var (vectors, pieces) = ArchPieces("7.1", "8.4");
        var aggr = Feat(8, 255, 400, featureIdentificationNumber: 98,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var arch = Feat(9, 255, ObjlBridgeArch, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(vectors, pieces.Append(arch).Append(aggr)));

        var spans = s401.Features.Where(f => ClassOf(s401, f) == "SpanFixed").ToList();
        Assert.Equal(2, spans.Count);
        AssertBridgeComponents(s401, SingleOfClass(s401, "Bridge"), spans[0], spans[1]);
        AssertArchComponents(s401, spans[0], spans[1]);
    }

    [Fact]
    public void Translate_S401Target_BridgeArchWithOneSpan_IsDropped()
    {
        // Only the first piece carries a clearance, so only it becomes a span.
        var (vectors, pieces) = ArchPieces("7.1", null);
        var arch = Feat(9, 255, ObjlBridgeArch, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var diag = new S57TranslationDiagnostics();

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            BuildDocument(vectors, pieces.Append(arch)), diag);

        Assert.DoesNotContain("BridgeArchAssociation", s401.FeatureAssociationCatalogue.Values);
        Assert.Equal(1, diag.RuleDroppedObjectClasses[ObjlBridgeArch]);
    }

    [Fact]
    public void Translate_S101Target_BridgeArch_IsUnmapped()
    {
        var (vectors, pieces) = ArchPieces("7.1", "8.4");
        var arch = Feat(9, 255, ObjlBridgeArch, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(BuildDocument(vectors, pieces.Append(arch)), diag);

        Assert.Equal(1, diag.UnmappedObjectClasses[ObjlBridgeArch]);
        Assert.DoesNotContain("BridgeArchAssociation", s101.FeatureAssociationCatalogue.Values);
    }

    [Fact]
    public void Translate_BridgeCAggrWithOtherMember_StillAggregatesAndLeavesTheMemberStandalone()
    {
        // IENC collects land, fenders, notice marks and more with a bridge
        // (IENC Encoding Guide 2.4.1, bridge clause H). Such a member no longer
        // disqualifies the collection; it converts on its own and is not linked.
        var (vectors, fixedSpan, openingSpan, _) = TwoSpanBridgeParts();
        var land = Feat(5, 1, 71, featureIdentificationNumber: 13,
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 4, 1, 0, 0) });
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 13) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { fixedSpan, openingSpan, land, aggr }), diag);

        var bridge = SingleOfClass(s101, "Bridge");
        AssertBridgeComponents(s101, bridge, SingleOfClass(s101, "SpanFixed"), SingleOfClass(s101, "SpanOpening"));
        var landArea = SingleOfClass(s101, "LandArea");
        Assert.Empty(landArea.FeatureAssociations);
        Assert.Equal(1, diag.BridgeAggregationsEmitted);
        Assert.Equal(0, diag.BridgeEquipmentLinked);
        Assert.False(diag.UnmappedObjectClasses.ContainsKey(400));
    }

    [Fact]
    public void Translate_CAggrWithPointBridgeMember_IsNotABridgeAggregation()
    {
        // A point BRIDGE converts to a Landmark, so it cannot be a span of an
        // aggregated Bridge; the collection keeps converting member by member.
        var (vectors, fixedSpan, openingSpan, _) = TwoSpanBridgeParts();
        var pointBridge = Feat(5, 1, 11, featureIdentificationNumber: 13,
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 4, 1, 0, 0) });
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 13) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { fixedSpan, openingSpan, pointBridge, aggr }), diag);

        var bridges = s101.Features.Where(f => ClassOf(s101, f) == "Bridge").ToList();
        Assert.Equal(2, bridges.Count);
        Assert.All(bridges, b => Assert.Single(b.FeatureAssociations));
        Assert.Equal(0, diag.BridgeAggregationsEmitted);
        Assert.Equal(1, diag.UnmappedObjectClasses[400]);
    }

    [Fact]
    public void Translate_CAggrWithBridgeAndNavigationLine_IsNotABridgeAggregation()
    {
        // A track grouping that passes through a bridge (NOAA US5WI3FK groups a
        // BRIDGE with a NAVLNE and a RECTRC) is not a bridge collection.
        var (vectors, fixedSpan, _, _) = TwoSpanBridgeParts();
        var navigationLine = Feat(5, 2, 85, featureIdentificationNumber: 13, // NAVLNE
            attributes: new[] { Attr(AttlOrient, "90") },
            spatialPointers: new[] { Sp(RcnmEdge, 11, 1, 0, 0) });
        var aggr = Feat(4, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 13) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { fixedSpan, navigationLine, aggr }), diag);

        Assert.Equal(10u, SingleOfClass(s101, "Bridge").FeatureIdentificationNumber);
        Assert.Equal(0, diag.BridgeAggregationsEmitted);
    }

    // The two-span bridge of TwoSpanBridgeParts with an all-around light on the
    // pylon, a sectored light, and a fender (SLCONS) in its collection.
    private static (EncDotNet.S57.S57VectorRecord[] Vectors, EncDotNet.S57.S57FeatureRecord[] Features)
        LitBridgeCollection()
    {
        var (vectors, fixedSpan, openingSpan, pylon) = TwoSpanBridgeParts();
        var allVectors = vectors
            .Append(Node(5, 5, 60, RcnmIsolatedNode))
            .Append(Node(6, -5, 60, RcnmIsolatedNode))
            .ToArray();
        var allAround = Feat(5, 1, 75, featureIdentificationNumber: 20,
            attributes: new[] { Attr(107, "1"), Attr(75, "3") }, // LITCHR fixed, COLOUR red
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 4, 1, 0, 0) });
        var sectored = Feat(6, 1, 75, featureIdentificationNumber: 21,
            attributes: new[] { Attr(107, "1"), Attr(75, "4"), Attr(136, "90"), Attr(137, "270") },
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 5, 1, 0, 0) });
        var fender = Feat(7, 1, 122, featureIdentificationNumber: 22, // SLCONS
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 6, 1, 0, 0) });
        var aggr = Feat(8, 255, 400, featureIdentificationNumber: 99,
            attributes: new[] { Attr(AttlObjnam, "Harbour Bridge") },
            featurePointers: new[]
            {
                Ffpt(540, 10), Ffpt(540, 11), Ffpt(540, 12), Ffpt(540, 20), Ffpt(540, 21), Ffpt(540, 22),
            });
        return (allVectors, new[] { fixedSpan, openingSpan, pylon, allAround, sectored, fender, aggr });
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-401")]
    public void Translate_BridgeCAggrWithLights_LinksEachLightToTheBridgeAsEquipment(string spec)
    {
        // IENC places bridge lights on the navigable span and the piers bounding
        // it, with no master object (IENC Encoding Guide 2.4.1, bridge light
        // clause C); both FCs bind any number of lights on Bridge.
        var target = spec == "S-401" ? S57TranslationTarget.S401 : S57TranslationTarget.S101;
        var (vectors, features) = LitBridgeCollection();
        var diag = new S57TranslationDiagnostics();

        var doc = S57ToS101Translator.ForTarget(target).Translate(BuildDocument(vectors, features), diag);

        var bridge = SingleOfClass(doc, "Bridge");
        var allAround = SingleOfClass(doc, "LightAllAround");
        var sectored = SingleOfClass(doc, "LightSectored");
        var equipment = bridge.FeatureAssociations
            .Where(a => doc.FeatureAssociationCatalogue[a.NumericCode] == "StructureEquipment")
            .ToList();
        Assert.All(equipment, a => Assert.Equal("theEquipment", doc.RoleCatalogue[a.RoleCode]));
        Assert.Equal(
            new[] { allAround.RecordId, sectored.RecordId }.Order(),
            equipment.Select(a => a.RecordId).Order());

        // Components are unchanged; the fender converts on its own.
        Assert.Equal(3, bridge.FeatureAssociations.Count(
            a => doc.FeatureAssociationCatalogue[a.NumericCode] == "BridgeAggregation"));
        Assert.Empty(SingleOfClass(doc, "ShorelineConstruction").FeatureAssociations);
        Assert.Equal(1, diag.BridgeAggregationsEmitted);
        Assert.Equal(2, diag.BridgeEquipmentLinked);
    }

    [Fact]
    public void Translate_LightInTwoBridgeCAggrs_IsLinkedToTheFirstBridgeOnly()
    {
        // S-101 binds a light to at most one structure (theStructure 0..1).
        var (vectors, fixedSpan, openingSpan, _) = TwoSpanBridgeParts();
        var light = Feat(5, 1, 75, featureIdentificationNumber: 20,
            attributes: new[] { Attr(107, "1"), Attr(75, "4") },
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 4, 1, 0, 0) });
        var first = Feat(6, 255, 400, featureIdentificationNumber: 98,
            featurePointers: new[] { Ffpt(540, 10), Ffpt(540, 20) });
        var second = Feat(7, 255, 400, featureIdentificationNumber: 99,
            featurePointers: new[] { Ffpt(540, 11), Ffpt(540, 20) });
        var diag = new S57TranslationDiagnostics();

        var s101 = new S57ToS101Translator().Translate(
            BuildDocument(vectors, new[] { fixedSpan, openingSpan, light, first, second }), diag);

        var lightId = SingleOfClass(s101, "LightAllAround").RecordId;
        var bridges = s101.Features.Where(f => ClassOf(s101, f) == "Bridge").ToList();
        Assert.Equal(2, bridges.Count);
        bool LinksLight(S101FeatureRecord b) => b.FeatureAssociations.Any(
            a => s101.FeatureAssociationCatalogue[a.NumericCode] == "StructureEquipment" && a.RecordId == lightId);
        Assert.True(LinksLight(bridges.Single(b => b.FeatureIdentificationNumber == 98)));
        Assert.False(LinksLight(bridges.Single(b => b.FeatureIdentificationNumber == 99)));
        Assert.Equal(1, diag.BridgeEquipmentLinked);
    }

    [Fact]
    public void Translate_InlandBridge_S401Target_EmitsSpanOpening()
    {
        // The IENC inland bridge (17011) reuses the BRIDGE rule, so it is
        // decomposed the same way; S-401 defines the span classes too.
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            LineFeatureWithS57Attributes(17011, Attr(AttlCatbrg, "5"), Attr(AttlVerccl, "3.2")));

        var bridge = SingleOfClass(s401, "Bridge");
        var span = SingleOfClass(s401, "SpanOpening");
        AssertBridgeComponents(s401, bridge, span);
        Assert.Equal("3.2", GetSubAttribute(s401, SpanComplex(s401, span, "verticalClearanceClosed"), "verticalClearanceValue"));
        Assert.DoesNotContain("verticalClearanceClosed", AttributeNames(s401, bridge));
    }

    [Fact]
    public void Translate_InlandPointBridge_S401Target_BecomesLandmark()
    {
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17011, Attr(AttlCatbrg, "1")));

        var feat = Assert.Single(s401.Features);
        Assert.Equal("Landmark", ClassOf(s401, feat));
        Assert.Equal("26", TopLevelValue(s401, feat, "categoryOfLandmark"));
        Assert.Equal("2", TopLevelValue(s401, feat, "visualProminence"));
    }

    // ── Vertical clearances on non-bridge features (S-65 Annex B § 2.2.4.3; IEHG 3.13/3.55) ──

    private const int AttlVercsa = 184;

    private static readonly string[] VerticalClearanceComplexes =
    [
        "verticalClearanceFixed", "verticalClearanceClosed", "verticalClearanceOpen", "verticalClearanceSafe",
    ];

    private static IReadOnlyList<S101Attribute> ClearanceComplex(
        S101Document doc, S101FeatureRecord feat, string complexCode)
        => ComplexInstanceStrict(doc, feat.Attributes, complexCode, 1, VerticalClearanceComplexes).ToList();

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-401")]
    public void Translate_OverheadCableWithVerclr_AssemblesVerticalClearanceFixed(string spec)
    {
        // CBLOHD (OBJL 21) → CableOverhead, which binds verticalClearanceFixed
        // and verticalClearanceSafe. VERCLR/VERCSA feed each complex's
        // verticalClearanceValue; VERACC nests as verticalUncertainty.
        var target = spec == "S-401" ? S57TranslationTarget.S401 : S57TranslationTarget.S101;
        var doc = S57ToS101Translator.ForTarget(target).Translate(LineFeatureWithS57Attributes(21,
            Attr(AttlVerclr, "25.5"), Attr(AttlVercsa, "20"), Attr(AttlVeracc, "0.5")));

        var feat = SingleOfClass(doc, "CableOverhead");
        var fixedClearance = ClearanceComplex(doc, feat, "verticalClearanceFixed");
        Assert.Equal("25.5", GetSubAttribute(doc, fixedClearance, "verticalClearanceValue"));
        Assert.Equal("0.5", GetSubAttribute(doc, fixedClearance, "uncertaintyFixed"));
        var safeClearance = ClearanceComplex(doc, feat, "verticalClearanceSafe");
        Assert.Equal(string.Empty, safeClearance[0].Value);
        Assert.Equal("20", GetSubAttribute(doc, safeClearance, "verticalClearanceValue"));

        // Each value sits inside its complex; none is emitted flat.
        Assert.Equal(["verticalClearanceFixed", "verticalClearanceValue", "verticalUncertainty", "uncertaintyFixed",
            "verticalClearanceSafe", "verticalClearanceValue", "verticalUncertainty", "uncertaintyFixed"],
            AttributeNames(doc, feat));
    }

    [Fact]
    public void Translate_OverheadCableWithEmptyVerclr_EmitsUnknownValueAndDropsVeracc()
    {
        // An empty (unknown) VERCLR still yields the complex, with the mandatory
        // verticalClearanceValue empty; VERACC has no known value to qualify.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(21, Attr(AttlVerclr, ""), Attr(AttlVeracc, "0.5")), diag);

        var feat = SingleOfClass(s101, "CableOverhead");
        var fixedClearance = ClearanceComplex(s101, feat, "verticalClearanceFixed");
        Assert.Equal(string.Empty, GetSubAttribute(s101, fixedClearance, "verticalClearanceValue"));
        Assert.DoesNotContain("uncertaintyFixed", AttributeNames(s101, feat));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVeracc]);
    }

    [Fact]
    public void Translate_GateWithVerclr_S101Target_DropsIt()
    {
        // S-101 Gate binds only verticalClearanceOpen, and S-65 Annex B gives no
        // rule for a gate's VERCLR, so it is dropped (with its VERACC).
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(61, Attr(AttlVerclr, "4"), Attr(AttlVeracc, "0.1")), diag);

        var feat = SingleOfClass(s101, "Gate");
        Assert.DoesNotContain("verticalClearanceValue", AttributeNames(s101, feat));
        Assert.DoesNotContain("verticalClearanceOpen", AttributeNames(s101, feat));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVerclr]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVeracc]);
    }

    [Theory]
    [InlineData(61)]    // GATCON
    [InlineData(17031)] // gatcon
    public void Translate_GateWithVerclr_S401Target_AssemblesVerticalClearanceOpen(ushort objl)
    {
        // IEHG S-57 ENC to S-401 Conversion Guidance clause 3.55: VERCLR →
        // verticalClearanceOpen.verticalClearanceValue, VERACC →
        // verticalUncertainty.uncertaintyFixed. A given clearance is not unlimited.
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(objl, Attr(AttlVerclr, "4"), Attr(AttlVeracc, "0.1")));

        var feat = SingleOfClass(s401, "Gate");
        var open = ClearanceComplex(s401, feat, "verticalClearanceOpen");
        Assert.Equal("false", GetSubAttribute(s401, open, "verticalClearanceUnlimited"));
        Assert.Equal("4", GetSubAttribute(s401, open, "verticalClearanceValue"));
        Assert.Equal("0.1", GetSubAttribute(s401, open, "uncertaintyFixed"));
    }

    [Fact]
    public void Translate_VerclrOnFeatureWithoutClearance_IsRuleDropped()
    {
        // A light binds no vertical clearance at all, so VERCLR is dropped and
        // VERACC stays unmapped, as before.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(75, Attr(75, "1"), Attr(AttlVerclr, "4"), Attr(AttlVeracc, "0.1")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.DoesNotContain(AttributeNames(s101, feat), n => n.StartsWith("verticalClearance", StringComparison.Ordinal));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlVerclr]);
        Assert.Equal(1, diag.UnmappedAttributes[new S57AttributeDrop(75, (ushort)AttlVeracc)]);
    }

    // ── Flat attributes the resolved class does not bind are rule-dropped ──

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-401")]
    public void Translate_MorfacPile_DropsUnboundWatlevAndNatcon(string spec)
    {
        // MORFAC CATMOR 5 → Pile. S-65 Annex B 4.6.7.1: WATLEV and NATCON are
        // not converted for Pile; neither FC binds them there. CONDTN is bound
        // and passes through.
        var target = spec == "S-401" ? S57TranslationTarget.S401 : S57TranslationTarget.S101;
        var diag = new S57TranslationDiagnostics();
        var doc = S57ToS101Translator.ForTarget(target).Translate(PointFeatureWithS57Attributes(84,
            Attr(40, "5"), Attr(187, "3"), Attr(112, "1"), Attr(81, "2")), diag);

        var feat = SingleOfClass(doc, "Pile");
        var names = AttributeNames(doc, feat).ToList();
        Assert.DoesNotContain("waterLevelEffect", names);
        Assert.DoesNotContain("natureOfConstruction", names);
        Assert.Equal("2", TopLevelValue(doc, feat, "condition"));
        Assert.Equal(1, diag.RuleDroppedAttributes[187]);
        Assert.Equal(1, diag.RuleDroppedAttributes[112]);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-401")]
    public void Translate_FloatingDock_CarriesHorizontalLengthAndWidth(string spec)
    {
        // Both FCs alias horizontalWidth to HORWID and bind it on FloatingDock,
        // alongside its HORLEN sibling.
        var target = spec == "S-401" ? S57TranslationTarget.S401 : S57TranslationTarget.S101;
        Assert.True(S101FeatureAttributeBindings.ForSpec(spec).Binds("FloatingDock", "horizontalWidth"));
        var diag = new S57TranslationDiagnostics();
        var doc = S57ToS101Translator.ForTarget(target).Translate(
            AreaFeatureWithS57Attributes(57, Attr(99, "120"), Attr(100, "35")), diag);

        var feat = SingleOfClass(doc, "FloatingDock");
        Assert.Equal("120", TopLevelValue(doc, feat, "horizontalLength"));
        Assert.Equal("35", TopLevelValue(doc, feat, "horizontalWidth"));
        Assert.False(diag.RuleDroppedAttributes.ContainsKey(100));
    }

    [Fact]
    public void Translate_PileHorwid_IsRuleDropped()
    {
        // MORFAC CATMOR 5 → Pile, which does not bind horizontalWidth.
        Assert.False(S101FeatureAttributeBindings.ForSpec("S-101").Binds("Pile", "horizontalWidth"));
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(84, Attr(40, "5"), Attr(100, "2")), diag);

        var feat = SingleOfClass(s101, "Pile");
        Assert.DoesNotContain("horizontalWidth", AttributeNames(s101, feat));
        Assert.Equal(1, diag.RuleDroppedAttributes[100]);
    }

    [Fact]
    public void Translate_SeabedAreaColour_IsRuleDropped()
    {
        // S-65 Annex B 2.4: colour is prohibited on Seabed Area.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(121, Attr(113, "4"), Attr(75, "1,3")), diag);

        var feat = SingleOfClass(s101, "SeabedArea");
        Assert.DoesNotContain("colour", AttributeNames(s101, feat));
        Assert.Equal(1, diag.RuleDroppedAttributes[75]);
    }

    [Fact]
    public void Translate_LightAllAroundOrient_IsRuleDropped()
    {
        // LightAllAround binds neither orientationValue nor orientation.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(75, Attr(75, "1"), Attr(AttlOrient, "90")), diag);

        var feat = SingleOfClass(s101, "LightAllAround");
        Assert.DoesNotContain(AttributeNames(s101, feat), n => n.StartsWith("orientation", StringComparison.Ordinal));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlOrient]);
    }

    // ── Directional lights (CATLIT 1/16) → LightSectored/directionalCharacter ──

    [Fact]
    public void Translate_DirectionalLight_RedirectsToLightSectored_WithDirectionalCharacter()
    {
        // S-65 Annex B 12.8.6.1: CATLIT 1 (directional function) sends LIGHTS to
        // LightSectored, and ORIENT becomes
        // lightSector.directionalCharacter.orientation.orientationValue. With no
        // SECTR1/SECTR2 there is no sectorLimit.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(AttlCatlit, "1,4"),
            Attr(107, "1"),        // LITCHR → lightCharacteristic (Fixed)
            Attr(75, "1"),         // COLOUR → colour (White)
            Attr(178, "7"),        // VALNMR → valueOfNominalRange
            Attr(AttlOrient, "343")),
            diag);

        var feat = SingleOfClass(s101, "LightSectored");
        var names = AttributeNames(s101, feat);
        Assert.DoesNotContain("sectorLimit", names);
        Assert.DoesNotContain("moireEffect", names);
        Assert.Equal("4", GetSubAttribute(s101, feat.Attributes, "categoryOfLight"));

        var directional = ComplexInstance(s101, feat.Attributes, "directionalCharacter", 1).ToList();
        Assert.NotEmpty(directional);
        var orientation = ComplexInstance(s101, directional, "orientation", 1).ToList();
        Assert.Equal("343", GetSubAttribute(s101, orientation, "orientationValue"));

        var lightSector = ComplexInstance(s101, feat.Attributes, "lightSector", 1).ToList();
        Assert.Equal("1", GetSubAttribute(s101, lightSector, "colour"));
        Assert.Equal("7", GetSubAttribute(s101, lightSector, "valueOfNominalRange"));

        Assert.False(diag.RuleDroppedAttributes.ContainsKey(AttlOrient));
    }

    [Fact]
    public void Translate_MoireEffectLight_SetsMoireEffect()
    {
        // CATLIT 16 (moiré effect) is directional too and sets moireEffect.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(AttlCatlit, "16"), Attr(107, "1"), Attr(75, "1"), Attr(AttlOrient, "90")));

        var feat = SingleOfClass(s101, "LightSectored");
        var directional = ComplexInstance(s101, feat.Attributes, "directionalCharacter", 1).ToList();
        Assert.Equal("true", GetSubAttribute(s101, directional, "moireEffect"));
        Assert.Equal("90", GetSubAttribute(s101, directional, "orientationValue"));
    }

    [Fact]
    public void Translate_DirectionalLightWithSector_KeepsSectorLimitAndDirectionalCharacter()
    {
        // A directional light encoded with a sector arc keeps its sectorLimit
        // (lightSector binds both [0..1]); the portrayal draws the sector.
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(AttlCatlit, "1"), Attr(107, "2"), Attr(75, "3"),
            Attr(136, "169.6"), Attr(137, "175.6"), Attr(AttlOrient, "172.6")));

        var feat = SingleOfClass(s101, "LightSectored");
        var two = ComplexInstance(s101, feat.Attributes, "sectorLimitTwo", 1).ToList();
        Assert.Equal("175.6", GetSubAttribute(s101, two, "sectorBearing"));
        var directional = ComplexInstance(s101, feat.Attributes, "directionalCharacter", 1).ToList();
        Assert.Equal("172.6", GetSubAttribute(s101, directional, "orientationValue"));
    }

    [Fact]
    public void Translate_DirectionalLightWithoutOrient_OmitsDirectionalCharacter()
    {
        // orientation is mandatory in directionalCharacter, so a directional
        // light without ORIENT (or with an empty one) is a LightSectored with a
        // bare lightSector.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(AttlCatlit, "16"), Attr(107, "1"), Attr(75, "1"), Attr(AttlOrient, "")),
            diag);

        var feat = SingleOfClass(s101, "LightSectored");
        var names = AttributeNames(s101, feat);
        Assert.Contains("lightSector", names);
        Assert.DoesNotContain("directionalCharacter", names);
        Assert.DoesNotContain("orientationValue", names);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlOrient]);
    }

    [Fact]
    public void Translate_NonDirectionalSectorLightOrient_IsRuleDropped()
    {
        // ORIENT on a sector light that is not directional has no S-101 home.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(LightWithS57Attributes(
            Attr(AttlCatlit, "4"), Attr(107, "2"), Attr(75, "3"),
            Attr(136, "10"), Attr(137, "90"), Attr(AttlOrient, "50")),
            diag);

        var feat = SingleOfClass(s101, "LightSectored");
        Assert.DoesNotContain("directionalCharacter", AttributeNames(s101, feat));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlOrient]);
    }

    [Fact]
    public void Translate_CoLocatedDirectionalSectorLights_AbsorbedMemberKeepsOrientation()
    {
        // The merge carries an absorbed directional member's ORIENT into its own
        // sectorCharacteristics instance.
        var s101 = new S57ToS101Translator().Translate(CoLocatedSectorLights(1,
            new[] { Attr(AttlCatlit, "1"), Attr(107, "1"), Attr(75, "3"), Attr(136, "230"), Attr(137, "235") },
            new[] { Attr(AttlCatlit, "1"), Attr(107, "1"), Attr(75, "1"), Attr(136, "235"), Attr(137, "241"), Attr(AttlOrient, "238") }));

        var feat = SingleOfClass(s101, "LightSectored");
        var first = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 1).ToList();
        Assert.DoesNotContain(first, a => s101.AttributeTypeCatalogue[a.NumericCode] == "directionalCharacter");
        var second = ComplexInstance(s101, feat.Attributes, "sectorCharacteristics", 2).ToList();
        Assert.Equal("238", GetSubAttribute(s101, second, "orientationValue"));
    }

    [Fact]
    public void Translate_DirectionalLight_S401Target_EmitsDirectionalCharacter()
    {
        // The IEHG S-57 ENC to S-401 guidance uses the same directionalCharacter
        // structure on S-401 LightSectored.
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(LightWithS57Attributes(
            Attr(AttlCatlit, "1"), Attr(107, "1"), Attr(75, "1"), Attr(AttlOrient, "12.5")));

        var feat = SingleOfClass(s401, "LightSectored");
        var directional = ComplexInstance(s401, feat.Attributes, "directionalCharacter", 1).ToList();
        Assert.Equal("12.5", GetSubAttribute(s401, directional, "orientationValue"));
    }

    [Fact]
    public void Translate_InlandFixedSpan_S401Target_DropsHorizontalClearance()
    {
        // S-401 SpanFixed binds no horizontal clearance (IEHG clause 3.144), so
        // HORCLR/HORACC are dropped there; S-101 SpanFixed keeps them.
        var diag = new S57TranslationDiagnostics();
        var source = LineFeatureWithS57Attributes(17011,
            Attr(AttlCatbrg, "1"), Attr(AttlVerclr, "9"), Attr(AttlHorclr, "30"), Attr(AttlHoracc, "1"));

        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(source, diag);
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11,
                Attr(AttlCatbrg, "1"), Attr(AttlVerclr, "9"), Attr(AttlHorclr, "30"), Attr(AttlHoracc, "1")));

        var span = SingleOfClass(s401, "SpanFixed");
        Assert.DoesNotContain("horizontalClearanceFixed", AttributeNames(s401, span));
        Assert.DoesNotContain("horizontalDistanceUncertainty", AttributeNames(s401, span));
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlHorclr]);
        Assert.Equal(1, diag.RuleDroppedAttributes[AttlHoracc]);
        Assert.Contains("horizontalClearanceFixed", AttributeNames(s101, SingleOfClass(s101, "SpanFixed")));
    }

    // ── ORIENT → orientation (S-65 Annex B § 3.3.1, 3.4, 10.1.1; IEHG 3.28, 3.32) ──

    private const int AttlOrient = 117;
    private const int AttlCatlit = 37;

    [Theory]
    [InlineData(85, "NavigationLine")]          // NAVLNE
    [InlineData(36, "CurrentNonGravitational")] // CURENT
    [InlineData(160, "TidalStreamFloodEbb")]    // TS_FEB
    public void Translate_OrientOnOrientationComplexClass_AssemblesOrientation(ushort objl, string s101Class)
    {
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(objl, Attr(AttlOrient, "123.5")));

        var feat = SingleOfClass(s101, s101Class);
        var orientation = ComplexInstanceStrict(s101, feat.Attributes, "orientation", 1, "orientation").ToList();
        Assert.Equal(["orientation", "orientationValue"],
            orientation.Select(a => s101.AttributeTypeCatalogue[a.NumericCode]));
        Assert.Equal("123.5", orientation[1].Value);
        Assert.Equal(1, AttributeNames(s101, feat).Count(n => n == "orientationValue"));
    }

    [Fact]
    public void Translate_OrientOnRecommendedTrack_StaysFlat()
    {
        // RecommendedTrack binds orientationValue directly, so ORIENT stays a
        // top-level simple attribute there.
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(109, Attr(AttlOrient, "45")));

        var feat = SingleOfClass(s101, "RecommendedTrack");
        Assert.DoesNotContain("orientation", AttributeNames(s101, feat));
        Assert.Equal("45", TopLevelValue(s101, feat, "orientationValue"));
    }

    [Fact]
    public void Translate_OrientOnInlandDaymark_S401Target_AssemblesOrientation()
    {
        // IEHG S-57 ENC to S-401 Conversion Guidance clause 3.32: ORIENT →
        // orientation.orientationValue on Daymark (S-101 Daymark binds neither).
        var s401 = S57ToS101Translator.ForTarget(S57TranslationTarget.S401).Translate(
            PointFeatureWithS57Attributes(17035, Attr(AttlOrient, "270")));

        var feat = SingleOfClass(s401, "Daymark");
        var orientation = ComplexInstanceStrict(s401, feat.Attributes, "orientation", 1, "orientation").ToList();
        Assert.Equal("270", GetSubAttribute(s401, orientation, "orientationValue"));
    }

    // ── CATBRG → S-101 bridge category attributes (S-65 Annex B § 4.8.10) ──

    private static List<(string Code, int Index, string Value)> BridgeAttributes(string catbrg, S57TranslationDiagnostics? diag = null)
    {
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(9, catbrg)), diag);
        var feat = Assert.Single(s101.Features);
        Assert.Equal("Bridge", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        return feat.Attributes
            .Select(a => (s101.AttributeTypeCatalogue[a.NumericCode], (int)a.Index, a.Value))
            .ToList();
    }

    [Fact]
    public void Translate_BridgeCatbrgSwing_SetsCategoryOfOpeningBridgeAndOpeningBridge()
    {
        var attrs = BridgeAttributes("3");

        Assert.Equal(
            [("categoryOfOpeningBridge", 1, "3"), ("openingBridge", 1, "true")],
            attrs);
    }

    [Fact]
    public void Translate_BridgeCatbrgFixed_SetsOpeningBridgeFalseOnly()
    {
        var attrs = BridgeAttributes("1");

        Assert.Equal([("openingBridge", 1, "false")], attrs);
    }

    [Fact]
    public void Translate_BridgeCatbrgOpeningPontoon_IsOpeningPontoonBridge()
    {
        // S-65 Annex B § 4.8.10: a pontoon bridge with an opening section is
        // encoded CATBRG = 2,6 and converts to an opening bridge.
        var attrs = BridgeAttributes("2,6");

        Assert.Equal(
            [("bridgeConstruction", 1, "3"), ("openingBridge", 1, "true")],
            attrs);
    }

    [Fact]
    public void Translate_BridgeCatbrgFunctions_EmitsEachBridgeFunctionOccurrence()
    {
        // bridgeFunction is multi-valued on Bridge; footbridge and aqueduct are
        // not opening bridges.
        var attrs = BridgeAttributes("9,11");

        Assert.Equal(
            [("bridgeFunction", 1, "3"), ("bridgeFunction", 2, "4"), ("openingBridge", 1, "false")],
            attrs);
    }

    [Fact]
    public void Translate_BridgeCatbrgMixedTargets_IndexesEachAttributeFromOne()
    {
        var attrs = BridgeAttributes("5,9,10");

        Assert.Equal(
            [
                ("categoryOfOpeningBridge", 1, "5"),
                ("bridgeFunction", 1, "3"),
                ("bridgeConstruction", 1, "2"),
                ("openingBridge", 1, "true"),
            ],
            attrs);
    }

    [Fact]
    public void Translate_BridgeCatbrgTwoConstructions_KeepsFirstAndReportsDrop()
    {
        // bridgeConstruction is [0..1] on Bridge, so only the first
        // construction survives; the second is reported as rule-dropped.
        var diag = new S57TranslationDiagnostics();
        var attrs = BridgeAttributes("6,12", diag);

        Assert.Equal(
            [("bridgeConstruction", 1, "3"), ("openingBridge", 1, "false")],
            attrs);
        Assert.Equal(1, diag.RuleDroppedAttributes[9]);
    }

    [Fact]
    public void Translate_BridgeCatbrgUnknownValue_IsDroppedWithoutOpeningBridge()
    {
        var diag = new S57TranslationDiagnostics();
        var attrs = BridgeAttributes("99", diag);

        Assert.Empty(attrs);
        Assert.Equal(1, diag.DroppedEnumValues[new S57EnumValueDrop("categoryOfOpeningBridge", "99")]);
    }

    [Fact]
    public void Translate_BridgeCatbrgArch_SetsBridgeConstructionArch()
    {
        // IEHG "S-57 ENC to S-401 Conversion Guidance" Ed 1.3.0 draft 2,
        // clause 3.7: CATBRG 13 (bridge arch) becomes bridgeConstruction 1
        // (arch). An arch is not an opening bridge.
        var diag = new S57TranslationDiagnostics();
        var attrs = BridgeAttributes("13", diag);

        Assert.Equal(
            [("bridgeConstruction", 1, "1"), ("openingBridge", 1, "false")],
            attrs);
        Assert.Empty(diag.DroppedEnumValues);
    }

    [Fact]
    public void Translate_BridgeCatbrgArchList_KeepsBothTargets()
    {
        // CATBRG is a list attribute, so "13,1" must still convert both values.
        var attrs = BridgeAttributes("13,1");

        Assert.Equal(
            [("bridgeConstruction", 1, "1"), ("openingBridge", 1, "false")],
            attrs);
    }

    [Fact]
    public void Translate_BridgeCatbrg_NeverEmitsCategoryOfBridge()
    {
        var attrs = BridgeAttributes("1,2,3,4,5,6,7,8,9,10,11,12");

        Assert.DoesNotContain(attrs, a => a.Code == "categoryOfBridge");
        Assert.Single(attrs, a => a.Code == "openingBridge");
        Assert.Contains(("openingBridge", 1, "true"), attrs);
    }

    // ── SORDAT → reportedDate (feature binding-gated simple attribute) ────

    [Fact]
    public void Translate_SordatOnBindingFeature_BecomesReportedDate()
    {
        // LNDARE (OBJL 71) → LandArea, which binds reportedDate. SORDAT (ATTL
        // 147) feeds it, the YYYYMMDD value carried verbatim.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(71, Attr(147, "20220407")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LandArea", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("reportedDate", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("20220407", attr.Value);
    }

    [Fact]
    public void Translate_SordatOnNonBindingFeature_LeavesSordatUnmapped()
    {
        // ACHARE (OBJL 4) → AnchorageArea, which does NOT bind reportedDate.
        // SORDAT has no conformant home there and is left unmapped (no
        // reportedDate emitted, no other attribute produced).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(4, Attr(147, "20220407")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("AnchorageArea", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.DoesNotContain(feat.Attributes,
            a => s101.AttributeTypeCatalogue[a.NumericCode] == "reportedDate");
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_SorindOnBindingFeature_StaysUnmapped()
    {
        // SORIND (ATTL 148) has no general S-101 equivalent, so even on a
        // reportedDate-binding feature it produces nothing; only SORDAT →
        // reportedDate is emitted.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(71,
                Attr(147, "20220407"),                // SORDAT → reportedDate
                Attr(148, "US,US,graph,L-105-2022"))); // SORIND → (unmapped)

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("reportedDate", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("20220407", attr.Value);
    }

    // ── CURVEL → speed, MLTYLT → multiplicityOfFeatures ───────────────────

    [Fact]
    public void Translate_CurrentWithCurvel_AssemblesSpeedComplex()
    {
        // CURENT (OBJL 36) → CurrentNonGravitational, which binds speed.
        // CURVEL (ATTL 84) feeds the mandatory speedMaximum sub-attribute.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(36, Attr(84, "1.3")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("CurrentNonGravitational", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var instance = ComplexInstance(s101, feat.Attributes, "speed", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("1.3", GetSubAttribute(s101, instance, "speedMaximum"));
        // speedMinimum has no S-57 source.
        Assert.Null(GetSubAttribute(s101, instance, "speedMinimum"));
    }

    [Fact]
    public void Translate_TidalStreamWithCurvel_AssemblesSpeedComplex()
    {
        // TS_FEB (OBJL 160) → TidalStreamFloodEbb, which also binds speed.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(160, Attr(84, "0.9")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("TidalStreamFloodEbb", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var instance = ComplexInstance(s101, feat.Attributes, "speed", 1).ToList();
        Assert.Equal("0.9", GetSubAttribute(s101, instance, "speedMaximum"));
    }

    [Fact]
    public void Translate_CurvelOnNonBindingFeature_LeavesCurvelUnmapped()
    {
        // ACHARE (OBJL 4) → AnchorageArea does not bind speed; CURVEL has no
        // conformant home there and is left unmapped.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(4, Attr(84, "1.3")));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "speed", 1).ToList());
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_LightWithMltylt_AssemblesMultiplicityOfFeatures()
    {
        // LIGHTS (OBJL 75) → LightAllAround, which binds
        // multiplicityOfFeatures. MLTYLT (ATTL 110) feeds numberOfFeatures with
        // multiplicityKnown set true.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(75, Attr(110, "3")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LightAllAround", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var instance = ComplexInstance(s101, feat.Attributes, "multiplicityOfFeatures", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("true", GetSubAttribute(s101, instance, "multiplicityKnown"));
        Assert.Equal("3", GetSubAttribute(s101, instance, "numberOfFeatures"));
    }

    [Fact]
    public void Translate_MltyltOnNonBindingFeature_LeavesMltyltUnmapped()
    {
        // ACHARE (OBJL 4) → AnchorageArea does not bind
        // multiplicityOfFeatures; MLTYLT has no conformant home and is left
        // unmapped.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(4, Attr(110, "3")));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "multiplicityOfFeatures", 1).ToList());
        Assert.Empty(feat.Attributes);
    }

    // ── VALLMA → valueOfLocalMagneticAnomaly, RADWAL → radarWaveLength ─────
    [Fact]
    public void Translate_LocalMagneticAnomalyWithVallma_AssemblesValueComplex()
    {
        // LOCMAG (OBJL 78) → LocalMagneticAnomaly, which binds
        // valueOfLocalMagneticAnomaly. VALLMA (ATTL 175) feeds the mandatory
        // magneticAnomalyValue sub-attribute (the value carried verbatim).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(78, Attr(175, "300")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("LocalMagneticAnomaly", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var instance = ComplexInstance(s101, feat.Attributes, "valueOfLocalMagneticAnomaly", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("300", GetSubAttribute(s101, instance, "magneticAnomalyValue"));
        // referenceDirection has no S-57 source and is not populated.
        Assert.Null(GetSubAttribute(s101, instance, "referenceDirection"));
        // VALLMA is not passed through as a top-level simple attribute.
        Assert.DoesNotContain(feat.Attributes,
            a => s101.AttributeTypeCatalogue[a.NumericCode] == "valueOfLocalMagneticAnomalyValue");
    }

    [Fact]
    public void Translate_RadarTransponderBeaconWithRadwal_AssemblesSingleWaveLength()
    {
        // RTPBCN (OBJL 103) → RadarTransponderBeacon, which binds
        // radarWaveLength. A single "value-band" pair yields one complex
        // instance (waveLengthValue real + radarBand text).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(103, Attr(126, "0.03-X")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("RadarTransponderBeacon", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);

        var instance = ComplexInstance(s101, feat.Attributes, "radarWaveLength", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("0.03", GetSubAttribute(s101, instance, "waveLengthValue"));
        Assert.Equal("X", GetSubAttribute(s101, instance, "radarBand"));
        // Only one instance for a single pair.
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "radarWaveLength", 2).ToList());
    }

    [Fact]
    public void Translate_RadarTransponderBeaconWithRadwalList_AssemblesTwoWaveLengths()
    {
        // A comma-separated RADWAL list yields one radarWaveLength instance per
        // "value-band" pair (RadarTransponderBeacon binds radarWaveLength [0..2]).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(103, Attr(126, "0.03-X,0.10-S")));

        var feat = Assert.Single(s101.Features);
        var first = ComplexInstance(s101, feat.Attributes, "radarWaveLength", 1).ToList();
        Assert.Equal("0.03", GetSubAttribute(s101, first, "waveLengthValue"));
        Assert.Equal("X", GetSubAttribute(s101, first, "radarBand"));

        var second = ComplexInstance(s101, feat.Attributes, "radarWaveLength", 2).ToList();
        Assert.Equal("0.10", GetSubAttribute(s101, second, "waveLengthValue"));
        Assert.Equal("S", GetSubAttribute(s101, second, "radarBand"));
    }

    [Fact]
    public void Translate_RadwalListExceedingFcUpperBound_CapsAtTwoAndReportsDrop()
    {
        // RadarTransponderBeacon binds radarWaveLength with multiplicity
        // upper=2, so a three-pair RADWAL list must emit only two instances;
        // the surplus pair is dropped and reported.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(103, Attr(126, "0.03-X,0.10-S,0.05-C")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.NotEmpty(ComplexInstance(s101, feat.Attributes, "radarWaveLength", 1).ToList());
        Assert.NotEmpty(ComplexInstance(s101, feat.Attributes, "radarWaveLength", 2).ToList());
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "radarWaveLength", 3).ToList());
        Assert.True(diag.RuleDroppedAttributes.TryGetValue(126, out var dropped) && dropped >= 1);
    }

    [Fact]
    public void Translate_RadwalMalformedPair_DropsInstance()
    {
        // A RADWAL element that lacks a band token (no '-') cannot fill both
        // mandatory sub-attributes, so no radarWaveLength instance is emitted.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(103, Attr(126, "0.03")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "radarWaveLength", 1).ToList());
    }

    [Fact]
    public void Translate_VallmaOnNonBindingFeature_LeavesVallmaUnmapped()
    {
        // ACHARE (OBJL 4) → AnchorageArea, which does not bind
        // valueOfLocalMagneticAnomaly. VALLMA has no conformant home there and
        // is left unmapped.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(4, Attr(175, "300")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("AnchorageArea", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(ComplexInstance(s101, feat.Attributes, "valueOfLocalMagneticAnomaly", 1).ToList());
        Assert.Empty(feat.Attributes);
    }

    // ── DATSTA/DATEND, PERSTA/PEREND, SURSTA/SUREND → date-range complexes ──

    private static EncDotNet.S57.S57Document PointFeatureWithS57Attributes(
        ushort objectClass,
        params EncDotNet.S57.S57AttributeValue[] attrs)
    {
        var n1 = Node(1, 1000, 2000);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: objectClass,
            attributes: attrs,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1, 1, 0, 0) });
        return BuildDocument(vectorRecords: new[] { n1 }, features: new[] { feature });
    }

    private static EncDotNet.S57.S57Document LineFeatureWithS57Attributes(
        ushort objectClass,
        params EncDotNet.S57.S57AttributeValue[] attrs)
    {
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 100, 100);
        var e1 = Edge(10, 1, 2);
        var feature = Feat(
            recordId: 1, primitive: 2, objectClass: objectClass,
            attributes: attrs,
            spatialPointers: new[] { Sp(RcnmEdge, 10, 1, 0, 0) });
        return BuildDocument(vectorRecords: new[] { n1, n2, e1 }, features: new[] { feature });
    }

    private static EncDotNet.S57.S57Document AreaFeatureWithS57Attributes(
        ushort objectClass,
        params EncDotNet.S57.S57AttributeValue[] attrs)
    {
        var n1 = Node(1, 0, 0);
        var n2 = Node(2, 0, 100);
        var n3 = Node(3, 100, 50);
        var e1 = Edge(10, 1, 2);
        var e2 = Edge(11, 2, 3);
        var e3 = Edge(12, 3, 1);
        var feature = Feat(
            recordId: 1, primitive: 3, objectClass: objectClass,
            attributes: attrs,
            spatialPointers: new[]
            {
                Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 11, 1, 1, 0),
                Sp(RcnmEdge, 12, 1, 1, 0),
            });
        return BuildDocument(vectorRecords: new[] { n1, n2, n3, e1, e2, e3 }, features: new[] { feature });
    }

    private static ushort? ResolveAttributeCode(S101Document doc, string name)
    {
        foreach (var (c, n) in doc.AttributeTypeCatalogue)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    // Collects one complex-attribute instance, delimiting at the next marker of
    // ANY of the named complex attributes — mirroring the S-101 data provider's
    // ResolveAttributeScope. This is required for complexes that share
    // sub-attribute codes (dateStart / dateEnd), where the simpler
    // same-code-only delimiter would wrongly absorb a sibling complex's rows.
    private static IEnumerable<S101Attribute> ComplexInstanceStrict(
        S101Document doc,
        IReadOnlyList<S101Attribute> attrs,
        string complexCode,
        int instanceIndex,
        params string[] allComplexCodes)
    {
        var code = ResolveAttributeCode(doc, complexCode);
        if (code is null) yield break;

        var markerCodes = new HashSet<ushort>();
        foreach (var name in allComplexCodes)
            if (ResolveAttributeCode(doc, name) is { } c)
                markerCodes.Add(c);

        int found = 0;
        bool collecting = false;
        foreach (var a in attrs)
        {
            if (a.NumericCode == code && a.Index == 1)
            {
                if (collecting) break;
                found++;
                if (found == instanceIndex)
                {
                    collecting = true;
                    yield return a;
                    continue;
                }
            }
            else if (collecting)
            {
                if (a.Index == 1 && markerCodes.Contains(a.NumericCode))
                    break; // sibling complex instance begins
                yield return a;
            }
        }
    }

    [Fact]
    public void Translate_DatstaDatend_BecomeFixedDateRangeComplex()
    {
        // Curve BRIDGE (OBJL 11) → Bridge, which binds fixedDateRange (with no
        // clearance attributes the bridge has no span).
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(86, "20200101"), Attr(85, "20201231")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "fixedDateRange", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("20200101", GetSubAttribute(s101, instance, "dateStart"));
        Assert.Equal("20201231", GetSubAttribute(s101, instance, "dateEnd"));

        // The first row naming `dateStart` must be preceded by the
        // fixedDateRange marker — it is never a bare top-level attribute.
        var fixedCode = ResolveAttributeCode(s101, "fixedDateRange");
        var startCode = ResolveAttributeCode(s101, "dateStart");
        Assert.NotNull(fixedCode);
        Assert.NotNull(startCode);
        int markerIdx = feat.Attributes.ToList().FindIndex(a => a.NumericCode == fixedCode);
        int startIdx = feat.Attributes.ToList().FindIndex(a => a.NumericCode == startCode);
        Assert.True(markerIdx >= 0 && markerIdx < startIdx);
    }

    [Fact]
    public void Translate_FixedDateRange_EmittedWithSingleEndpoint()
    {
        // fixedDateRange allows dateStart [0..1] / dateEnd [0..1]; a lone DATEND
        // still yields an instance carrying only dateEnd.
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(11, Attr(85, "20201231")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "fixedDateRange", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Null(GetSubAttribute(s101, instance, "dateStart"));
        Assert.Equal("20201231", GetSubAttribute(s101, instance, "dateEnd"));
    }

    [Fact]
    public void Translate_PerstaPerend_BecomePeriodicDateRangeComplex()
    {
        // ACHARE (OBJL 4) → AnchorageArea, which binds periodicDateRange.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(4, Attr(119, "20200401"), Attr(118, "20200930")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "periodicDateRange", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("20200401", GetSubAttribute(s101, instance, "dateStart"));
        Assert.Equal("20200930", GetSubAttribute(s101, instance, "dateEnd"));
    }

    [Fact]
    public void Translate_PeriodicDateRange_DroppedWhenMissingMandatoryEndpoint()
    {
        // periodicDateRange makes both dateStart and dateEnd mandatory [1..1];
        // a lone PERSTA cannot form a conformant instance, so none is emitted.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(4, Attr(119, "20200401")));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstanceStrict(s101, feat.Attributes, "periodicDateRange", 1).ToList());
    }

    [Fact]
    public void Translate_SurstaSurend_BecomeSurveyDateRangeComplex()
    {
        // M_QUAL (OBJL 308) → QualityOfBathymetricData, which binds surveyDateRange.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(152, "20190501"), Attr(151, "20190815")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "surveyDateRange", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("20190501", GetSubAttribute(s101, instance, "dateStart"));
        Assert.Equal("20190815", GetSubAttribute(s101, instance, "dateEnd"));
    }

    [Fact]
    public void Translate_SurveyDateRange_DroppedWhenMissingDateEnd()
    {
        // surveyDateRange makes dateEnd mandatory [1..1] (dateStart is optional);
        // a lone SURSTA cannot form a conformant instance.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(152, "20190501")));

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstanceStrict(s101, feat.Attributes, "surveyDateRange", 1).ToList());
    }

    [Fact]
    public void Translate_DateRange_NotEmittedOnFeatureThatDoesNotBindIt()
    {
        // LNDRGN (OBJL 73) → LandRegion binds none of the date-range complexes,
        // so DATSTA/DATEND have no conformant home and no complex is emitted.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(73, Attr(86, "20200101"), Attr(85, "20201231")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstanceStrict(s101, feat.Attributes, "fixedDateRange", 1).ToList());
        Assert.DoesNotContain("fixedDateRange", s101.AttributeTypeCatalogue.Values);
    }

    [Fact]
    public void Translate_FixedAndPeriodicDateRange_EmittedAsDistinctInstances()
    {
        // BERTHS (OBJL 10) → Berth binds BOTH fixedDateRange and
        // periodicDateRange. Each S-57 pair must land in its own complex; the
        // shared dateStart/dateEnd sub-attributes must not cross-contaminate.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(10,
                Attr(86, "20200101"), Attr(85, "20201231"),   // DATSTA/DATEND → fixed
                Attr(119, "20200401"), Attr(118, "20200930"))); // PERSTA/PEREND → periodic

        var feat = Assert.Single(s101.Features);

        var fixedInstance = ComplexInstanceStrict(
            s101, feat.Attributes, "fixedDateRange", 1, "fixedDateRange", "periodicDateRange").ToList();
        Assert.Equal("20200101", GetSubAttribute(s101, fixedInstance, "dateStart"));
        Assert.Equal("20201231", GetSubAttribute(s101, fixedInstance, "dateEnd"));

        var periodicInstance = ComplexInstanceStrict(
            s101, feat.Attributes, "periodicDateRange", 1, "fixedDateRange", "periodicDateRange").ToList();
        Assert.Equal("20200401", GetSubAttribute(s101, periodicInstance, "dateStart"));
        Assert.Equal("20200930", GetSubAttribute(s101, periodicInstance, "dateEnd"));
    }

    [Fact]
    public void Translate_Catzoc_BecomesZoneOfConfidenceComplex()
    {
        // M_QUAL (OBJL 308) → QualityOfBathymetricData, the sole feature class
        // binding zoneOfConfidence. CATZOC=3 (Zone of Confidence B) is carried
        // as the categoryOfZoneOfConfidenceInData sub-attribute (identical enum).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(72, "3")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "zoneOfConfidence", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("3", GetSubAttribute(s101, instance, "categoryOfZoneOfConfidenceInData"));
    }

    [Fact]
    public void Translate_Catzoc_OutOfRangeValueDropsInstance()
    {
        // categoryOfZoneOfConfidenceInData permits codes 1..6; an out-of-range
        // CATZOC leaves the mandatory sub-attribute unpopulated, so the whole
        // instance is dropped and the value is recorded as a dropped enum.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(72, "9")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstanceStrict(s101, feat.Attributes, "zoneOfConfidence", 1).ToList());
        Assert.Contains(
            new S57EnumValueDrop("categoryOfZoneOfConfidenceInData", "9"),
            diag.DroppedEnumValues.Keys);
    }

    [Fact]
    public void Translate_Catzoc_NotEmittedOnFeatureThatDoesNotBindIt()
    {
        // LNDRGN (OBJL 73) → LandRegion does not bind zoneOfConfidence, so a
        // (non-conformant) CATZOC has no home and no complex is emitted.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(73, Attr(72, "3")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Empty(ComplexInstanceStrict(s101, feat.Attributes, "zoneOfConfidence", 1).ToList());
        Assert.DoesNotContain("zoneOfConfidence", s101.AttributeTypeCatalogue.Values);
    }

    [Fact]
    public void Translate_CatzocAndSurveyDateRange_EmittedAsDistinctInstances()
    {
        // QualityOfBathymetricData binds BOTH zoneOfConfidence and
        // surveyDateRange. Both complexes must be emitted as separate instances
        // on the same feature without cross-contaminating one another.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308,
                Attr(72, "4"),                                 // CATZOC → zoneOfConfidence
                Attr(152, "20190501"), Attr(151, "20190815"))); // SURSTA/SUREND → surveyDateRange

        var feat = Assert.Single(s101.Features);

        var zocInstance = ComplexInstanceStrict(
            s101, feat.Attributes, "zoneOfConfidence", 1, "zoneOfConfidence", "surveyDateRange").ToList();
        Assert.Equal("4", GetSubAttribute(s101, zocInstance, "categoryOfZoneOfConfidenceInData"));

        var surveyInstance = ComplexInstanceStrict(
            s101, feat.Attributes, "surveyDateRange", 1, "zoneOfConfidence", "surveyDateRange").ToList();
        Assert.Equal("20190501", GetSubAttribute(s101, surveyInstance, "dateStart"));
        Assert.Equal("20190815", GetSubAttribute(s101, surveyInstance, "dateEnd"));
    }

    [Fact]
    public void Translate_CatzocA1_PopulatesHorizontalAndVerticalUncertainty()
    {
        // ZOC A1 (CATZOC=1) is the only zone with a horizontal variable factor
        // (±5 m + 5% depth). Vertical accuracy is 0.50 m + 1% depth. The full
        // pre-order layout of the zoneOfConfidence subtree is asserted so the two
        // nested uncertainty complexes are unambiguously scoped.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(72, "1")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(
            new (string, string)[]
            {
                ("zoneOfConfidence", ""),
                ("categoryOfZoneOfConfidenceInData", "1"),
                ("horizontalPositionUncertainty", ""),
                ("uncertaintyFixed", "5"),
                ("uncertaintyVariableFactor", "5"),
                ("verticalUncertainty", ""),
                ("uncertaintyFixed", "0.5"),
                ("uncertaintyVariableFactor", "1"),
            },
            AttributeSequence(s101, feat.Attributes));
    }

    [Fact]
    public void Translate_CatzocB_OmitsHorizontalVariableFactor()
    {
        // ZOC B (CATZOC=3) has a fixed-only horizontal accuracy (±50 m, no
        // variable factor) and a vertical accuracy of 1.00 m + 2% depth.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(72, "3")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(
            new (string, string)[]
            {
                ("zoneOfConfidence", ""),
                ("categoryOfZoneOfConfidenceInData", "3"),
                ("horizontalPositionUncertainty", ""),
                ("uncertaintyFixed", "50"),
                ("verticalUncertainty", ""),
                ("uncertaintyFixed", "1"),
                ("uncertaintyVariableFactor", "2"),
            },
            AttributeSequence(s101, feat.Attributes));
    }

    [Fact]
    public void Translate_CatzocD_EmitsCategoryOnlyWithoutUncertainty()
    {
        // ZOC D (CATZOC=5) is "worse than C" and unquantified, so no uncertainty
        // sub-complexes are emitted — only categoryOfZoneOfConfidenceInData.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(308, Attr(72, "5")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(
            new (string, string)[]
            {
                ("zoneOfConfidence", ""),
                ("categoryOfZoneOfConfidenceInData", "5"),
            },
            AttributeSequence(s101, feat.Attributes));
    }

    // Projects a feature's attribute rows onto their (catalogue-name, value)
    // pairs in emission order, for asserting nested complex pre-order layouts.
    private static (string, string)[] AttributeSequence(
        S101Document doc, IReadOnlyList<S101Attribute> attrs) =>
        attrs.Select(a => (doc.AttributeTypeCatalogue[a.NumericCode], a.Value)).ToArray();

    // ── MORFAC → Dolphin / ShorelineConstruction / Bollard / Pile / MooringBuoy ──

    [Fact]
    public void Translate_PointMorfac_BecomesDolphin()
    {
        // S-101 removed the generic MooringWarpingFacility class; a point
        // MORFAC (OBJL 84) maps to Dolphin (S-101 portrayal wires MORFAC point
        // symbols to Dolphin.lua).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(84));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Dolphin", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_PointMorfac_DolphinCatmor_MapsToMooringDolphin()
    {
        // CATMOR=1 (dolphin) → categoryOfDolphin=1 (Mooring Dolphin).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(84, Attr(40, "1")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Dolphin", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("categoryOfDolphin", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("1", attr.Value);
    }

    [Fact]
    public void Translate_PointMorfac_DeviationDolphinCatmor_MapsToDeviationDolphin()
    {
        // CATMOR=2 (deviation dolphin) → categoryOfDolphin=2 (Deviation
        // Dolphin) — the one clean cross-enumeration match.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(84, Attr(40, "2")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Dolphin", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("categoryOfDolphin", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("2", attr.Value);
    }

    [Theory]
    [InlineData("4")] // tie-up wall
    [InlineData("6")] // chain/wire/cable
    public void Translate_PointMorfac_NonDolphinCatmor_LeavesDolphinWithoutCategory(string catmor)
    {
        // CATMOR values with no S-101 dolphin-category equivalent (and no
        // dedicated class) leave a Dolphin with no categoryOfDolphin.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(84, Attr(40, catmor)));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Dolphin", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(feat.Attributes);
    }

    [Theory]
    [InlineData("3", "Bollard")]     // bollard
    [InlineData("5", "Pile")]        // post or pile
    [InlineData("7", "MooringBuoy")] // mooring buoy
    public void Translate_PointMorfac_DedicatedCatmor_RedirectsToNamedClass(string catmor, string expected)
    {
        // CATMOR values that name a facility with its own S-101 class redirect
        // there, and CATMOR is dropped (those classes do not bind
        // categoryOfDolphin).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(84, Attr(40, catmor)));

        var feat = Assert.Single(s101.Features);
        Assert.Equal(expected, s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Empty(feat.Attributes);
    }

    [Fact]
    public void Translate_AreaMorfac_BecomesShorelineConstruction()
    {
        // Line/area MORFAC maps to ShorelineConstruction (S-101 portrayal wires
        // MORFAC line/area symbols to ShorelineConstruction.lua).
        var s101 = new S57ToS101Translator().Translate(
            AreaFeatureWithS57Attributes(84));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("ShorelineConstruction", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
    }

    [Fact]
    public void Translate_LineMorfac_BecomesShorelineConstruction()
    {
        var s101 = new S57ToS101Translator().Translate(
            LineFeatureWithS57Attributes(84));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("ShorelineConstruction", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
    }

    [Fact]
    public void Translate_AreaMorfac_WithCatmor_DoesNotEmitCategoryOfDolphin()
    {
        // A CATMOR on a line/area MORFAC must not leak categoryOfDolphin onto
        // ShorelineConstruction (which does not bind it); the geometry redirect
        // drops CATMOR. (No such instance exists in NOAA data, but the mapping
        // must remain conformant.)
        var s101 = new S57ToS101Translator().Translate(
            AreaFeatureWithS57Attributes(84, Attr(40, "2")));

        var feat = Assert.Single(s101.Features);
        Assert.Equal("ShorelineConstruction", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.DoesNotContain(feat.Attributes,
            a => s101.AttributeTypeCatalogue[a.NumericCode] == "categoryOfDolphin");
    }

    // ── CATPRA → categoryOfProductionArea / categoryOfOffshoreProductionArea ──

    [Fact]
    public void Translate_Catpra_OnProductionStorageArea_PassesThroughAsCategoryOfProductionArea()
    {
        // PRDARE (OBJL 97) → ProductionStorageArea binds categoryOfProductionArea,
        // whose enumeration shares codes 1..12 with S-57 CATPRA, so the value
        // (8 = Tank Farm) passes through unchanged.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(97, Attr(48, "8")));

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("categoryOfProductionArea", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("8", attr.Value);
    }

    [Fact]
    public void Translate_Catpra_OnOffshoreProductionArea_RedirectsAndRemapsToOffshoreCategory()
    {
        // OSPARE (OBJL 88) → OffshoreProductionArea binds the distinct
        // categoryOfOffshoreProductionArea enumeration. S-57 CATPRA=9 (Wind Farm)
        // remaps to offshore code 1 (Wind Farm).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(88, Attr(48, "9")));

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("categoryOfOffshoreProductionArea", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("1", attr.Value);
        Assert.DoesNotContain("categoryOfProductionArea", s101.AttributeTypeCatalogue.Values);
    }

    [Fact]
    public void Translate_Catpra_OnOffshoreProductionArea_TankFarmRemapsToOffshoreTankFarm()
    {
        // S-57 CATPRA=8 (Tank Farm) → offshore code 4 (Tank Farm).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(88, Attr(48, "8")));

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("categoryOfOffshoreProductionArea", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("4", attr.Value);
    }

    [Fact]
    public void Translate_Catpra_OnOffshoreProductionArea_NonOffshoreValueIsDropped()
    {
        // S-57 CATPRA=2 (Mine) has no categoryOfOffshoreProductionArea equivalent,
        // so the attribute is dropped on OffshoreProductionArea and recorded.
        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(88, Attr(48, "2")), diag);

        var feat = Assert.Single(s101.Features);
        Assert.Empty(feat.Attributes);
        Assert.Contains((ushort)48, diag.RuleDroppedAttributes.Keys);
    }

    // ── NATSUR / NATQUA → surfaceCharacteristics (SeabedArea) ──

    [Fact]
    public void Translate_NatsurAndNatqua_OnSeabedArea_BecomeSurfaceCharacteristicsInstance()
    {
        // SBDARE (OBJL 121) → SeabedArea, the sole feature class binding
        // surfaceCharacteristics. NATSUR=4 (sand) + NATQUA=1 (fine) pair into a
        // single instance carrying natureOfSurface and natureOfSurfaceQualifyingTerms.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(121, Attr(113, "4"), Attr(114, "1")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "surfaceCharacteristics", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("4", GetSubAttribute(s101, instance, "natureOfSurface"));
        Assert.Equal("1", GetSubAttribute(s101, instance, "natureOfSurfaceQualifyingTerms"));
        // NATSUR/NATQUA must NOT also leak out as top-level simple attributes
        // (SeabedArea binds neither); the only emitted rows are the complex
        // marker and its two sub-attributes.
        Assert.Equal(3, feat.Attributes.Count);
    }

    [Fact]
    public void Translate_NatquaOnly_OnSeabedArea_BecomesQualifyingTermsInstance()
    {
        // The dominant corpus case: NATQUA present with no NATSUR. Since
        // natureOfSurface is optional within surfaceCharacteristics, this still
        // forms a valid instance carrying only natureOfSurfaceQualifyingTerms.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(121, Attr(114, "4")));

        var feat = Assert.Single(s101.Features);
        var instance = ComplexInstance(s101, feat.Attributes, "surfaceCharacteristics", 1).ToList();
        Assert.NotEmpty(instance);
        Assert.Equal("4", GetSubAttribute(s101, instance, "natureOfSurfaceQualifyingTerms"));
        Assert.Null(GetSubAttribute(s101, instance, "natureOfSurface"));
    }

    [Fact]
    public void Translate_NatsurAndNatquaLists_OnSeabedArea_PairPositionally()
    {
        // NATSUR="4,3" (sand, mud) + NATQUA="1" (fine): position 0 pairs
        // (4,1); position 1 has surface only (3).
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(121, Attr(113, "4,3"), Attr(114, "1")));

        var feat = Assert.Single(s101.Features);

        var first = ComplexInstance(s101, feat.Attributes, "surfaceCharacteristics", 1).ToList();
        Assert.Equal("4", GetSubAttribute(s101, first, "natureOfSurface"));
        Assert.Equal("1", GetSubAttribute(s101, first, "natureOfSurfaceQualifyingTerms"));

        var second = ComplexInstance(s101, feat.Attributes, "surfaceCharacteristics", 2).ToList();
        Assert.Equal("3", GetSubAttribute(s101, second, "natureOfSurface"));
        Assert.Null(GetSubAttribute(s101, second, "natureOfSurfaceQualifyingTerms"));
    }

    [Fact]
    public void Translate_Natsur_OnNonSeabedFeature_StaysDirectSimpleAttribute()
    {
        // LandRegion (OBJL 73) binds a top-level natureOfSurface and does NOT
        // bind surfaceCharacteristics, so NATSUR passes through unchanged and no
        // complex is assembled.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(73, Attr(113, "4")));

        var feat = Assert.Single(s101.Features);
        var attr = Assert.Single(feat.Attributes);
        Assert.Equal("natureOfSurface", s101.AttributeTypeCatalogue[attr.NumericCode]);
        Assert.Equal("4", attr.Value);
        Assert.DoesNotContain("surfaceCharacteristics", s101.AttributeTypeCatalogue.Values);
    }

    // ── Area ring chaining (multi-hole "spike" regression) ─────────────

    // Builds a LandArea (LNDARE, OBJL 71) with a square exterior ring and two
    // disjoint square holes. Boundary pointers are deliberately shuffled, with
    // the first edge for each ring referenced in reverse orientation, so chaining
    // must use node identity and FSPT orientation instead of input order.
    private static EncDotNet.S57.S57Document BuildTwoHoleArea()
    {
        // Exterior square 0..1000.
        var ext = new[]
        {
            Node(1, 0, 0), Node(2, 0, 1000), Node(3, 1000, 1000), Node(4, 1000, 0),
            Edge(10, 1, 2), Edge(11, 2, 3), Edge(12, 3, 4), Edge(13, 4, 1),
        };
        // Hole A square 100..200.
        var holeA = new[]
        {
            Node(101, 100, 100), Node(102, 100, 200), Node(103, 200, 200), Node(104, 200, 100),
            Edge(20, 101, 102), Edge(21, 102, 103), Edge(22, 103, 104), Edge(23, 104, 101),
        };
        // Hole B square 700..800 (far from hole A).
        var holeB = new[]
        {
            Node(201, 700, 700), Node(202, 700, 800), Node(203, 800, 800), Node(204, 800, 700),
            Edge(30, 201, 202), Edge(31, 202, 203), Edge(32, 203, 204), Edge(33, 204, 201),
        };

        var feature = Feat(
            recordId: 1, primitive: 3, objectClass: 71, // LNDARE → LandArea
            spatialPointers: new[]
            {
                // Exterior ring (USAG = 1), shuffled and seeded in reverse.
                Sp(RcnmEdge, 12, 2, 1, 0), Sp(RcnmEdge, 10, 1, 1, 0),
                Sp(RcnmEdge, 13, 1, 1, 0), Sp(RcnmEdge, 11, 1, 1, 0),
                // Both holes' edges remain one USAG = 2 pool, but each hole's
                // order is shuffled and each first edge is referenced in reverse.
                Sp(RcnmEdge, 22, 2, 2, 0), Sp(RcnmEdge, 32, 2, 2, 0),
                Sp(RcnmEdge, 20, 1, 2, 0), Sp(RcnmEdge, 33, 1, 2, 0),
                Sp(RcnmEdge, 23, 1, 2, 0), Sp(RcnmEdge, 30, 1, 2, 0),
                Sp(RcnmEdge, 21, 1, 2, 0), Sp(RcnmEdge, 31, 1, 2, 0),
            });

        return BuildDocument(
            vectorRecords: ext.Concat(holeA).Concat(holeB).ToArray(),
            features: new[] { feature });
    }

    [Fact]
    public void Translate_AreaWithTwoHoles_ProducesSeparateInteriorRings()
    {
        // The two disjoint holes must resolve to two distinct interior rings,
        // not one merged ring that jumps between them.
        var s101 = new S57ToS101Translator().Translate(BuildTwoHoleArea());
        var source = new S101VectorSource(S101Dataset.FromDocument(s101));

        var feature = Assert.Single(source.GetFeatures());
        Assert.Equal("LandArea", feature.FeatureType);
        Assert.Equal(2, feature.InteriorRings.Count);
    }

    [Fact]
    public void Translate_AreaWithTwoHoles_HasNoCrossHoleSpike()
    {
        // Every segment within each resolved ring must stay local to its hole;
        // a merged ring would contain a long jump between the two holes (the
        // spike artifact). Hole edges span ~1e-5 deg; the inter-hole jump would
        // be ~8e-5 deg, so a 3e-5 deg ceiling cleanly distinguishes them.
        const double spikeThresholdDegrees = 3e-5;

        var s101 = new S57ToS101Translator().Translate(BuildTwoHoleArea());
        var source = new S101VectorSource(S101Dataset.FromDocument(s101));
        var feature = Assert.Single(source.GetFeatures());

        static double MaxSegment(IReadOnlyList<EncDotNet.S100.DataModel.GeoPosition> ring)
        {
            double max = 0;
            for (var i = 0; i < ring.Count - 1; i++)
            {
                var dLat = ring[i + 1].Latitude - ring[i].Latitude;
                var dLon = ring[i + 1].Longitude - ring[i].Longitude;
                max = Math.Max(max, Math.Sqrt((dLat * dLat) + (dLon * dLon)));
            }

            return max;
        }

        foreach (var ring in feature.InteriorRings)
        {
            var maxSegment = MaxSegment(ring);
            Assert.True(
                maxSegment < spikeThresholdDegrees,
                $"Interior ring contains a cross-hole spike segment of {maxSegment:G4} degrees.");
        }
    }
}
