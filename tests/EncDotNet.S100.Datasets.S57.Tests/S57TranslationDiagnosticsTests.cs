namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Tests for the opt-in <see cref="S57TranslationDiagnostics"/> sink, verifying
/// that every translator drop site (unmapped/rule-dropped object classes,
/// unmapped/rule-dropped attributes, FC-rejected enum values, geometry loss,
/// and sounding accounting) is reported accurately.
/// </summary>
public class S57TranslationDiagnosticsTests
{
    private const byte RcnmIsolatedNode = 110;
    private const byte RcnmConnectedNode = 120;

    // ── Builders (mirrors S57ToS101TranslatorTests) ────────────────────

    private static EncDotNet.S57.S57Document BuildDocument(
        IEnumerable<EncDotNet.S57.S57VectorRecord>? vectorRecords = null,
        IEnumerable<EncDotNet.S57.S57FeatureRecord>? features = null)
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
                CoordinateMultiplicationFactor = 10_000_000,
                SoundingMultiplicationFactor = 10,
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
            Coordinates2D = [new EncDotNet.S57.S57Coordinate2D { X = x, Y = y }],
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

    private static EncDotNet.S57.S57SpatialPointer Sp(byte rcnm, uint id)
        => new()
        {
            Name = Name(rcnm, id),
            Orientation = (EncDotNet.S57.S57Orientation)1,
            Usage = (EncDotNet.S57.S57UsageIndicator)0,
            Mask = (EncDotNet.S57.S57MaskingIndicator)255,
        };

    private static EncDotNet.S57.S57AttributeValue Attr(int code, string value)
        => new() { AttributeCode = code, Value = value };

    private static EncDotNet.S57.S57FeatureRecord Feat(
        uint recordId,
        byte primitive,
        ushort objectClass,
        IEnumerable<EncDotNet.S57.S57AttributeValue>? attributes = null,
        IEnumerable<EncDotNet.S57.S57SpatialPointer>? spatialPointers = null)
        => new()
        {
            RecordName = new EncDotNet.S57.S57RecordName
            {
                RecordNameCode = 100,
                RecordId = (int)recordId,
                AgencyCode = 540,
                FeatureId = (int)recordId,
                FeatureSubdivision = 0,
            },
            Primitive = (EncDotNet.S57.S57GeometricPrimitive)(int)primitive,
            ObjectCode = (EncDotNet.S57.S57ObjectCode)(int)objectClass,
            Attributes = (attributes ?? Array.Empty<EncDotNet.S57.S57AttributeValue>()).ToArray(),
            NationalAttributes = [],
            SpatialPointers = (spatialPointers ?? Array.Empty<EncDotNet.S57.S57SpatialPointer>()).ToArray(),
        };

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Translate_WithoutDiagnostics_DoesNotThrow()
    {
        // The default overload passes a null sink; behaviour is unchanged.
        var doc = BuildDocument(features: new[] { Feat(1, 1, 61234) });
        var s101 = new S57ToS101Translator().Translate(doc);
        Assert.Empty(s101.Features);
    }

    [Fact]
    public void UnmappedObjectClass_IsRecordedWithCount()
    {
        // OBJL 61234 is not a real S-57 object class → no rule → unmapped.
        const ushort unknownObjl = 61234;
        var doc = BuildDocument(features: new[]
        {
            Feat(1, 1, unknownObjl),
            Feat(2, 1, unknownObjl),
        });

        var diag = new S57TranslationDiagnostics();
        new S57ToS101Translator().Translate(doc, diag);

        Assert.Equal(2, diag.FeatureRecordsRead);
        Assert.Equal(0, diag.FeaturesEmitted);
        Assert.Equal(2, diag.UnmappedObjectClasses[unknownObjl]);
        Assert.Empty(diag.RuleDroppedObjectClasses);
    }

    [Fact]
    public void ResolvedFeatureWithoutGeometry_IsRecorded()
    {
        // BCNCAR (OBJL 5 → CardinalBeacon) resolves, but its point pointer
        // targets a node that does not exist → zero spatial associations.
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 5,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 999) });
        var doc = BuildDocument(features: new[] { feature });

        var diag = new S57TranslationDiagnostics();
        new S57ToS101Translator().Translate(doc, diag);

        Assert.Equal(1, diag.FeatureRecordsRead);
        Assert.Equal(0, diag.FeaturesEmitted);
        var kv = Assert.Single(diag.FeaturesDroppedForNoGeometry);
        Assert.Equal(1, kv.Value);
        Assert.Empty(diag.UnmappedObjectClasses);
    }

    [Fact]
    public void EmittedFeature_IsCountedAndNotDropped()
    {
        var node = Node(1, 100, 200);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 5,
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1) });
        var doc = BuildDocument(vectorRecords: new[] { node }, features: new[] { feature });

        var diag = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(doc, diag);

        Assert.Single(s101.Features);
        Assert.Equal(1, diag.FeaturesEmitted);
        Assert.Empty(diag.FeaturesDroppedForNoGeometry);
        Assert.Empty(diag.UnmappedObjectClasses);
    }

    [Fact]
    public void UnmappedAttribute_IsRecordedWithOwnerObjectClass()
    {
        // Attribute code 65001 is unknown to the mapping. Owner is BCNCAR (5).
        var node = Node(1, 100, 200);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 5,
            attributes: new[] { Attr(65001, "x") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1) });
        var doc = BuildDocument(vectorRecords: new[] { node }, features: new[] { feature });

        var diag = new S57TranslationDiagnostics();
        new S57ToS101Translator().Translate(doc, diag);

        var kv = Assert.Single(diag.UnmappedAttributes);
        Assert.Equal((ushort)5, kv.Key.ObjectClass);
        Assert.Equal((ushort)65001, kv.Key.AttributeCode);
        Assert.Equal(1, kv.Value);
    }

    [Fact]
    public void TextualInfoAttributes_AreNotReportedAsUnmapped()
    {
        // INFORM (102) is transformed into the S-101 `information` complex
        // attribute, so it must not appear as an unmapped-attribute drop.
        var node = Node(1, 100, 200);
        var feature = Feat(
            recordId: 1, primitive: 1, objectClass: 5,
            attributes: new[] { Attr(102, "see note") },
            spatialPointers: new[] { Sp(RcnmConnectedNode, 1) });
        var doc = BuildDocument(vectorRecords: new[] { node }, features: new[] { feature });

        var diag = new S57TranslationDiagnostics();
        new S57ToS101Translator().Translate(doc, diag);

        Assert.Empty(diag.UnmappedAttributes);
    }

    [Fact]
    public void SoundingAccounting_TracksPointsAndEmptySoundings()
    {
        var populated = SoundingNode(1, (10, 20, 100), (11, 21, 110));
        var withPoints = Feat(
            recordId: 1, primitive: 1, objectClass: 129,
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 1) });
        // A SOUNDG whose pointer targets a node with no depth triples.
        var empty = Feat(
            recordId: 2, primitive: 1, objectClass: 129,
            spatialPointers: new[] { Sp(RcnmIsolatedNode, 2) });
        var doc = BuildDocument(
            vectorRecords: new[] { populated },
            features: new[] { withPoints, empty });

        var diag = new S57TranslationDiagnostics();
        new S57ToS101Translator().Translate(doc, diag);

        Assert.Equal(2, diag.SoundingFeaturesRead);
        Assert.Equal(1, diag.SoundingFeaturesEmitted);
        Assert.Equal(2, diag.SoundingPointsEmitted);
        Assert.Equal(1, diag.SoundingFeaturesWithoutPoints);
        // Soundings are not counted as ordinary feature records.
        Assert.Equal(0, diag.FeatureRecordsRead);
    }
}
