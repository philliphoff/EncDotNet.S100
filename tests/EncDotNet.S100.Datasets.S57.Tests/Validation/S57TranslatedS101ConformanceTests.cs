using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Validation;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests.Validation;

/// <summary>
/// Checks that the S-57 → S-101 translation is itself conformant: running the
/// S-101 rule pack (with the bundled S-101 Feature Catalogue) over the
/// translated document reports nothing the source cell does not contain.
/// Covers the three translation artefacts NOAA cells exposed — complex
/// sub-attributes without a parent index (S101-R-1.2), ring paths walked
/// without their edge orientation (S101-R-3.2), and features derived from one
/// S-57 object sharing its FOID (S101-R-2.1).
/// </summary>
public class S57TranslatedS101ConformanceTests
{
    private const ushort ObjlBridge = 11;
    private const ushort ObjlBoylat = 17;
    private const ushort ObjlDepare = 42;
    private const ushort ObjlMQual = 308;
    private const int AttlCatbrg = 9;
    private const int AttlCatzoc = 72;
    private const int AttlDrval1 = 87;
    private const int AttlObjnam = 116;
    private const int AttlVerclr = 181;

    private static readonly Lazy<FeatureCatalogueDecoder> Decoder = new(() =>
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("Bundled S-101 Feature Catalogue not found.");
        return new FeatureCatalogueDecoder(FeatureCatalogueReader.Read(stream));
    });

    // ── Fixture helpers ────────────────────────────────────────────────

    private static S57Document Document(IEnumerable<S57VectorRecord> vectors, IEnumerable<S57FeatureRecord> features)
        => new()
        {
            DataSetIdentification = new S57DataSetIdentification
            {
                DataSetName = "TEST.000",
                EditionNumber = "1",
                UpdateNumber = "0",
                IssueDate = "20260101",
            },
            DataSetParameters = new S57DataSetParameters
            {
                CompilationScale = 50_000,
                CoordinateMultiplicationFactor = 10_000_000,
                SoundingMultiplicationFactor = 10,
            },
            VectorRecords = vectors.ToArray(),
            FeatureRecords = features.ToArray(),
        };

    private static S57RecordName Name(int rcnm, int id) => new() { RecordNameCode = rcnm, RecordId = id };

    private static S57VectorRecord Node(int id, int y, int x) => new()
    {
        RecordName = Name(S57RecordNameCodes.ConnectedNode, id),
        VectorPointers = [],
        Coordinates2D = [new S57Coordinate2D { X = x, Y = y }],
        Soundings = [],
        Attributes = [],
    };

    private static S57VectorRecord Edge(int id, int beginNode, int endNode) => new()
    {
        RecordName = Name(S57RecordNameCodes.Edge, id),
        VectorPointers =
        [
            new S57VectorPointer
            {
                Name = Name(S57RecordNameCodes.ConnectedNode, beginNode),
                Orientation = S57Orientation.Forward,
                Topology = (S57TopologyIndicator)1,
            },
            new S57VectorPointer
            {
                Name = Name(S57RecordNameCodes.ConnectedNode, endNode),
                Orientation = S57Orientation.Forward,
                Topology = (S57TopologyIndicator)2,
            },
        ],
        Coordinates2D = [],
        Soundings = [],
        Attributes = [],
    };

    private static S57SpatialPointer EdgeRef(int edgeId, S57Orientation orientation = S57Orientation.Forward,
        S57UsageIndicator usage = S57UsageIndicator.Exterior) => new()
        {
            Name = Name(S57RecordNameCodes.Edge, edgeId),
            Orientation = orientation,
            Usage = usage,
        };

    private static S57SpatialPointer NodeRef(int nodeId) => new()
    {
        Name = Name(S57RecordNameCodes.ConnectedNode, nodeId),
        Orientation = S57Orientation.Forward,
    };

    private static S57FeatureRecord Feature(
        int recordId, S57GeometricPrimitive primitive, ushort objectClass,
        IEnumerable<S57SpatialPointer> spatial, int fidn = 1, int fids = 0,
        params S57AttributeValue[] attributes) => new()
        {
            RecordName = new S57RecordName
            {
                RecordNameCode = 100,
                RecordId = recordId,
                AgencyCode = 550,
                FeatureId = fidn,
                FeatureSubdivision = fids,
            },
            Primitive = primitive,
            ObjectCode = (S57ObjectCode)objectClass,
            Attributes = attributes,
            NationalAttributes = [],
            SpatialPointers = spatial.ToArray(),
            FeaturePointers = [],
        };

    private static S57AttributeValue Attr(int code, string value) => new() { AttributeCode = code, Value = value };

    // A unit square (nodes 1..4, edges 10..13 running anticlockwise).
    private static IEnumerable<S57VectorRecord> Square() =>
    [
        Node(1, 0, 0), Node(2, 0, 1000), Node(3, 1000, 1000), Node(4, 1000, 0),
        Edge(10, 1, 2), Edge(11, 2, 3), Edge(12, 3, 4), Edge(13, 4, 1),
    ];

    private static IEnumerable<S57SpatialPointer> SquareRing() => [EdgeRef(10), EdgeRef(11), EdgeRef(12), EdgeRef(13)];

    private static ValidationReport Validate(S101Document document)
        => S101DatasetRules.Default.Run(S101DatasetView.From(document, Decoder.Value));

    private static string Describe(ValidationReport report)
        => string.Join("; ", report.Findings.Select(f => $"{f.RuleId}: {f.Message}"));

    private static string Code(S101Document doc, S101Attribute row) => doc.AttributeTypeCatalogue[row.NumericCode];

    // ── S101-R-1.2: complex sub-attributes carry their parent index ─────

    [Fact]
    public void ZoneOfConfidence_SubAttributes_Point_At_Their_Parent_Complex()
    {
        // CATZOC 1 (A1) yields zoneOfConfidence { categoryOfZoneOfConfidenceInData,
        // horizontalPositionUncertainty { uncertaintyFixed, ... },
        // verticalUncertainty { uncertaintyFixed, uncertaintyVariableFactor } }.
        var s101 = new S57ToS101Translator().Translate(Document(
            Square(),
            [Feature(1, S57GeometricPrimitive.Area, ObjlMQual, SquareRing(), attributes: Attr(AttlCatzoc, "1"))]));

        var rows = Assert.Single(s101.Features).Attributes;
        int Position(string code) => rows.ToList().FindIndex(r => Code(s101, r) == code) + 1;

        var zoc = Position("zoneOfConfidence");
        var horizontal = Position("horizontalPositionUncertainty");
        var vertical = Position("verticalUncertainty");
        Assert.Equal(0, rows[zoc - 1].ParentIndex);
        Assert.Equal(zoc, rows[Position("categoryOfZoneOfConfidenceInData") - 1].ParentIndex);
        Assert.Equal(zoc, rows[horizontal - 1].ParentIndex);
        Assert.Equal(zoc, rows[vertical - 1].ParentIndex);
        Assert.Equal(horizontal, rows[horizontal].ParentIndex);
        Assert.All(rows.Skip(vertical), r => Assert.Equal(vertical, r.ParentIndex));

        var report = Validate(s101);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-1.2");
    }

    [Fact]
    public void FeatureName_SubAttributes_Point_At_FeatureName_And_Simple_Attributes_Stay_Top_Level()
    {
        var s101 = new S57ToS101Translator().Translate(Document(
            [Node(1, 0, 0)],
            [Feature(1, S57GeometricPrimitive.Point, ObjlBoylat, [NodeRef(1)],
                attributes: [Attr(AttlObjnam, "Green 3"), Attr(75, "4")])]));

        var rows = Assert.Single(s101.Features).Attributes;
        var featureName = rows.ToList().FindIndex(r => Code(s101, r) == "featureName") + 1;
        Assert.True(featureName > 0);
        Assert.Equal(0, rows[featureName - 1].ParentIndex);
        Assert.All(
            rows.Where(r => Code(s101, r) is "name" or "language"),
            r => Assert.Equal(featureName, r.ParentIndex));
        Assert.All(rows.Where(r => Code(s101, r) == "colour"), r => Assert.Equal(0, r.ParentIndex));

        Assert.DoesNotContain(Validate(s101).Findings, f => f.RuleId == "S101-R-1.2");
    }

    // ── S101-R-2.1: derived features get their own FIDS ─────────────────

    [Fact]
    public void Bridge_Span_Takes_The_Next_Free_Subdivision_And_Bridge_Keeps_The_S57_Identifier()
    {
        var diagnostics = new S57TranslationDiagnostics();
        var s101 = new S57ToS101Translator().Translate(Document(
            [Node(1, 0, 0), Node(2, 100, 100), Edge(10, 1, 2)],
            [Feature(1, S57GeometricPrimitive.Line, ObjlBridge, [EdgeRef(10)], fidn: 77, fids: 5,
                attributes: [Attr(AttlCatbrg, "1"), Attr(AttlVerclr, "12.4")])]),
            diagnostics);

        var bridge = Assert.Single(s101.Features, f => s101.FeatureTypeCatalogue[f.FeatureTypeCode] == "Bridge");
        var span = Assert.Single(s101.Features, f => s101.FeatureTypeCatalogue[f.FeatureTypeCode] == "SpanFixed");
        Assert.Equal((550, 77u, 5), (bridge.ProducingAgency, bridge.FeatureIdentificationNumber, bridge.FeatureIdentificationSubdivision));
        Assert.Equal((550, 77u, 6), (span.ProducingAgency, span.FeatureIdentificationNumber, span.FeatureIdentificationSubdivision));
        Assert.Equal(1, diagnostics.DerivedFeatureIdentifiersAssigned);

        Assert.DoesNotContain(Validate(s101).Findings, f => f.RuleId == "S101-R-2.1");
    }

    [Fact]
    public void Derived_Subdivision_Skips_One_Another_Feature_Already_Uses()
    {
        // A second S-57 object already holds FIDS 6, so the span moves on to 7.
        var s101 = new S57ToS101Translator().Translate(Document(
            [Node(1, 0, 0), Node(2, 100, 100), Edge(10, 1, 2), Node(3, 500, 500)],
            [
                Feature(1, S57GeometricPrimitive.Line, ObjlBridge, [EdgeRef(10)], fidn: 77, fids: 5,
                    attributes: [Attr(AttlCatbrg, "1"), Attr(AttlVerclr, "12.4")]),
                Feature(2, S57GeometricPrimitive.Point, ObjlBoylat, [NodeRef(3)], fidn: 77, fids: 6),
            ]));

        var span = Assert.Single(s101.Features, f => s101.FeatureTypeCatalogue[f.FeatureTypeCode] == "SpanFixed");
        Assert.Equal(7, span.FeatureIdentificationSubdivision);
        Assert.DoesNotContain(Validate(s101).Findings, f => f.RuleId == "S101-R-2.1");
    }

    [Fact]
    public void Source_Duplicate_Foids_Are_Kept_And_Reported_With_A_Location()
    {
        // Two distinct S-57 objects sharing a FOID is a source data error the
        // translation must not hide.
        var s101 = new S57ToS101Translator().Translate(Document(
            [Node(1, 474_000_000, -1_226_000_000), Node(2, 474_100_000, -1_226_100_000)],
            [
                Feature(1, S57GeometricPrimitive.Point, ObjlBoylat, [NodeRef(1)], fidn: 9),
                Feature(2, S57GeometricPrimitive.Point, ObjlBoylat, [NodeRef(2)], fidn: 9),
            ]));

        var finding = Assert.Single(Validate(s101).Findings, f => f.RuleId == "S101-R-2.1");
        Assert.NotNull(finding.Point);
        Assert.Equal(47.41, finding.Point!.Value.Latitude, 6);
        Assert.Equal(-122.61, finding.Point!.Value.Longitude, 6);
    }

    // ── S101-R-3.2: rings are walked in their encoded orientation ───────

    [Fact]
    public void Area_With_Reversed_Edges_And_Two_Holes_Has_Closed_Rings()
    {
        // Exterior: the square with edge 11 stored the other way round (so the
        // ring walks it in reverse). Two separate triangular holes, each
        // listed with USAG = interior, one of them with a reversed edge too.
        var vectors = new List<S57VectorRecord>
        {
            Node(1, 0, 0), Node(2, 0, 1000), Node(3, 1000, 1000), Node(4, 1000, 0),
            Edge(10, 1, 2), Edge(11, 3, 2), Edge(12, 3, 4), Edge(13, 4, 1),
            Node(5, 100, 100), Node(6, 100, 300), Node(7, 300, 200),
            Edge(20, 5, 6), Edge(21, 6, 7), Edge(22, 7, 5),
            Node(8, 600, 600), Node(9, 600, 800), Node(14, 800, 700),
            Edge(30, 8, 9), Edge(31, 14, 9), Edge(32, 14, 8),
        };
        S57SpatialPointer Hole(int edge, S57Orientation o = S57Orientation.Forward)
            => EdgeRef(edge, o, S57UsageIndicator.Interior);
        var s101 = new S57ToS101Translator().Translate(Document(
            vectors,
            [Feature(1, S57GeometricPrimitive.Area, ObjlDepare,
                [
                    EdgeRef(10), EdgeRef(11, S57Orientation.Reverse), EdgeRef(12), EdgeRef(13),
                    Hole(20), Hole(21), Hole(22),
                    Hole(30), Hole(31, S57Orientation.Reverse), Hole(32),
                ],
                attributes: [Attr(AttlDrval1, "5"), Attr(88, "10")])]));

        var surface = Assert.Single(s101.Surfaces.Values);
        Assert.Equal(3, surface.RingAssociations.Count);

        var report = Validate(s101);
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "S101-R-3.2" or "S101-R-3.3");
        Assert.True(report.IsValid, Describe(report));
    }
}
