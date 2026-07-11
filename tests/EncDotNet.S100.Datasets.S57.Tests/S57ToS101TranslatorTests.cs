using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

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
        IEnumerable<EncDotNet.S57.S57SpatialPointer>? spatialPointers = null)
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

    // ── v3.4: INFORM/NINFOM/TXTDSC/NTXTDS → information complex attribute ──

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
    public void Translate_InformAttribute_BecomesInformationComplexAttribute_WithEnglish()
    {
        var doc = LandRegionWithS57Attributes(Attr(102, "Visible all around. Higher intensity on rangeline"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = InformationInstance(s101, feat.Attributes, 1).ToList();

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
        var instance = InformationInstance(s101, feat.Attributes, 1).ToList();

        Assert.Equal("US5WA23A.TXT", GetSubAttribute(s101, instance, "fileReference"));
        Assert.Equal("eng", GetSubAttribute(s101, instance, "language"));
        Assert.Null(GetSubAttribute(s101, instance, "text"));
    }

    [Fact]
    public void Translate_NinfomAttribute_BecomesInformationComplex_WithBlankLanguage()
    {
        var doc = LandRegionWithS57Attributes(Attr(300, "Información en español"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);
        var instance = InformationInstance(s101, feat.Attributes, 1).ToList();

        Assert.Equal("Información en español", GetSubAttribute(s101, instance, "text"));
        Assert.Equal("", GetSubAttribute(s101, instance, "language"));
    }

    [Fact]
    public void Translate_InformAndNinfom_EmitTwoInformationInstances()
    {
        var doc = LandRegionWithS57Attributes(
            Attr(102, "English text"),
            Attr(300, "National text"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        var first = InformationInstance(s101, feat.Attributes, 1).ToList();
        var second = InformationInstance(s101, feat.Attributes, 2).ToList();

        Assert.Equal("English text", GetSubAttribute(s101, first, "text"));
        Assert.Equal("eng", GetSubAttribute(s101, first, "language"));
        Assert.Equal("National text", GetSubAttribute(s101, second, "text"));
        Assert.Equal("", GetSubAttribute(s101, second, "language"));
    }

    [Fact]
    public void Translate_InformAndTxtdscTogether_EmitOneInstanceWithBothSubAttrs()
    {
        var doc = LandRegionWithS57Attributes(
            Attr(102, "Inline note"),
            Attr(158, "EXTRA.TXT"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        var first = InformationInstance(s101, feat.Attributes, 1).ToList();
        Assert.Equal("Inline note", GetSubAttribute(s101, first, "text"));
        Assert.Equal("EXTRA.TXT", GetSubAttribute(s101, first, "fileReference"));
        Assert.Equal("eng", GetSubAttribute(s101, first, "language"));

        Assert.Empty(InformationInstance(s101, feat.Attributes, 2).ToList());
    }

    [Fact]
    public void Translate_NoTextualAttributes_EmitsNoInformationInstance()
    {
        var doc = LandRegionWithS57Attributes(Attr(34, "1"));

        var s101 = new S57ToS101Translator().Translate(doc);
        var feat = Assert.Single(s101.Features);

        Assert.Empty(InformationInstance(s101, feat.Attributes, 1).ToList());
    }

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
        // BRIDGE (OBJL 11) → Bridge, which binds fixedDateRange.
        var s101 = new S57ToS101Translator().Translate(
            PointFeatureWithS57Attributes(11, Attr(86, "20200101"), Attr(85, "20201231")));

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
            PointFeatureWithS57Attributes(11, Attr(85, "20201231")));

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
}
