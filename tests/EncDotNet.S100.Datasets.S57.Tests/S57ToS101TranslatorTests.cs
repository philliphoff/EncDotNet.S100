using System.Collections.Immutable;
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
            VectorRecords = (vectorRecords ?? Array.Empty<EncDotNet.S57.S57VectorRecord>()).ToImmutableArray(),
            FeatureRecords = (features ?? Array.Empty<EncDotNet.S57.S57FeatureRecord>()).ToImmutableArray(),
        };

    private static EncDotNet.S57.S57RecordName Name(byte rcnm, uint id)
        => new() { RecordNameCode = rcnm, RecordId = (int)id };

    private static EncDotNet.S57.S57VectorRecord Node(uint id, int y, int x, byte rcnm = RcnmConnectedNode)
        => new()
        {
            RecordName = Name(rcnm, id),
            VectorPointers = ImmutableArray<EncDotNet.S57.S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray.Create(
                new EncDotNet.S57.S57Coordinate2D { X = x, Y = y }),
            Soundings = ImmutableArray<EncDotNet.S57.S57Sounding>.Empty,
            Attributes = ImmutableArray<EncDotNet.S57.S57AttributeValue>.Empty,
        };

    private static EncDotNet.S57.S57VectorRecord Edge(
        uint id, uint beginNodeId, uint endNodeId,
        params (int Y, int X)[] intermediates)
        => new()
        {
            RecordName = Name(RcnmEdge, id),
            VectorPointers = ImmutableArray.Create(
                Vp(RcnmConnectedNode, beginNodeId, ornt: 1, usage: 0, topo: 1, mask: 255),
                Vp(RcnmConnectedNode, endNodeId,   ornt: 1, usage: 0, topo: 2, mask: 255)),
            Coordinates2D = intermediates
                .Select(c => new EncDotNet.S57.S57Coordinate2D { X = c.X, Y = c.Y })
                .ToImmutableArray(),
            Soundings = ImmutableArray<EncDotNet.S57.S57Sounding>.Empty,
            Attributes = ImmutableArray<EncDotNet.S57.S57AttributeValue>.Empty,
        };

    private static EncDotNet.S57.S57VectorRecord SoundingNode(
        uint id, params (int Y, int X, int Z)[] soundings)
        => new()
        {
            RecordName = Name(RcnmIsolatedNode, id),
            VectorPointers = ImmutableArray<EncDotNet.S57.S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray<EncDotNet.S57.S57Coordinate2D>.Empty,
            Soundings = soundings
                .Select(s => new EncDotNet.S57.S57Sounding { X = s.X, Y = s.Y, Depth = s.Z })
                .ToImmutableArray(),
            Attributes = ImmutableArray<EncDotNet.S57.S57AttributeValue>.Empty,
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
            Attributes = (attributes ?? Array.Empty<EncDotNet.S57.S57AttributeValue>()).ToImmutableArray(),
            NationalAttributes = ImmutableArray<EncDotNet.S57.S57AttributeValue>.Empty,
            SpatialPointers = (spatialPointers ?? Array.Empty<EncDotNet.S57.S57SpatialPointer>()).ToImmutableArray(),
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
        Assert.Equal(2, cs.PointAssociations.Length);
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
        Assert.Equal(2, feat.Attributes.Length);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(130, sa.RecordName);

        var surface = s101.Surfaces[sa.RecordId];
        var ring = Assert.Single(surface.RingAssociations);
        Assert.Equal(1, ring.Usage);
        Assert.Equal(125, ring.RecordName);

        var composite = s101.CompositeCurves[ring.RecordId];
        Assert.Equal(3, composite.CurveComponents.Length);
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
        Assert.Equal(3, mp.Points.Length);
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
        Assert.Equal(3, mp.Points.Length);
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
        Assert.Equal(2, feat.Attributes.Length);
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

    // ── v3.4: INFORM/NINFOM/TXTDSC/NTXTDS → information complex attribute ──

    private static IEnumerable<S101Attribute> InformationInstance(
        S101Document doc,
        ImmutableArray<S101Attribute> attrs,
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
}
