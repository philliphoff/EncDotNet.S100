using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S101.Tests.Validation;

/// <summary>
/// Tests for the parts of the S-101 rule pack that read nesting and geometry
/// the way S-100 Part 10a encodes them: complex sub-attributes identified by
/// their <c>PAIX</c> (S101-R-1.2), ring paths walked in their encoded
/// orientation (S101-R-3.2), and the map location findings carry.
/// </summary>
public partial class S101ValidationTests
{
    private const ushort FeatureNameCode = 20;  // FC acronym "featureName" (complex)
    private const ushort NameCode = 21;         // FC acronym "name"
    private const ushort LanguageCode = 22;     // FC acronym "language"

    private static IReadOnlyDictionary<ushort, string> AttributeCatalogueWithFeatureName =>
        new Dictionary<ushort, string>(AttributeCatalogueByDefault)
        {
            [FeatureNameCode] = "featureName",
            [NameCode] = "name",
            [LanguageCode] = "language",
        };

    // DepthArea binds featureName { name, language } as well as DRVAL1/DRVAL2/OBJNAM.
    private static FeatureCatalogueDecoder BuildDecoderWithFeatureName()
    {
        var fc = BuildDecoder().Catalogue;
        var unbounded = new Multiplicity { Lower = 0, Upper = null };
        var optional = new Multiplicity { Lower = 0, Upper = 1 };
        return new FeatureCatalogueDecoder(new FeatureCatalogue
        {
            Name = fc.Name,
            VersionNumber = fc.VersionNumber,
            VersionDate = fc.VersionDate,
            ProductId = fc.ProductId,
            SimpleAttributes =
            [
                .. fc.SimpleAttributes,
                new SimpleAttribute { Code = "name", Name = "Name", ValueType = "Text" },
                new SimpleAttribute { Code = "language", Name = "Language", ValueType = "Text" },
            ],
            ComplexAttributes =
            [
                new ComplexAttribute
                {
                    Code = "featureName",
                    Name = "Feature name",
                    SubAttributeBindings =
                    [
                        new SubAttributeBinding { AttributeRef = "name", Multiplicity = optional },
                        new SubAttributeBinding { AttributeRef = "language", Multiplicity = optional },
                    ],
                },
            ],
            FeatureTypes =
            [
                .. fc.FeatureTypes.Select(ft => ft.Code != "DepthArea" ? ft : new FeatureType
                {
                    Code = ft.Code,
                    Name = ft.Name,
                    AttributeBindings =
                    [
                        .. ft.AttributeBindings,
                        new AttributeBinding { AttributeRef = "featureName", Multiplicity = unbounded },
                    ],
                }),
            ],
        });
    }

    private static S101Document DocumentWithFeatureName(params S101Attribute[] attributes)
        => Document(
            features: [Feature(1, attributes: attributes, spatial: [new S101SpatialAssociation(110, 1, 1)])],
            points: [new S101PointRecord { RecordId = 1, Y = 474_000_000, X = -1_226_000_000 }],
            attributeCatalogue: AttributeCatalogueWithFeatureName);

    // ---------------------------------------------------------------
    // S101-R-1.2  sub-attributes are checked against their complex
    // ---------------------------------------------------------------

    [Fact]
    public void R1_2_Passes_For_Sub_Attributes_Whose_Parent_Complex_Binds_Them()
    {
        var doc = DocumentWithFeatureName(
            new S101Attribute(FeatureNameCode, 1, ""),
            new S101Attribute(NameCode, 1, "Elliott Bay", ParentIndex: 1),
            new S101Attribute(LanguageCode, 1, "eng", ParentIndex: 1),
            new S101Attribute(Drval1Code, 1, "5"));

        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoderWithFeatureName()));

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-1.2");
    }

    [Fact]
    public void R1_2_Fires_For_A_Sub_Attribute_Placed_At_The_Top_Level()
    {
        // `name` is only valid inside featureName; with PAIX 0 it claims a
        // binding to DepthArea that the catalogue does not declare.
        var doc = DocumentWithFeatureName(
            new S101Attribute(FeatureNameCode, 1, ""),
            new S101Attribute(NameCode, 1, "Elliott Bay"));

        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoderWithFeatureName()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.2");
        Assert.Contains("'name'", finding.Message);
        Assert.Contains("not bound to that feature class", finding.Message);
    }

    [Fact]
    public void R1_2_Fires_For_A_Sub_Attribute_Its_Parent_Complex_Does_Not_Bind()
    {
        var doc = DocumentWithFeatureName(
            new S101Attribute(FeatureNameCode, 1, ""),
            new S101Attribute(Drval1Code, 1, "5", ParentIndex: 1));

        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoderWithFeatureName()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.2");
        Assert.Contains("inside complex attribute 'featureName'", finding.Message);
    }

    [Fact]
    public void R1_2_Fires_When_Parent_Index_Does_Not_Point_At_A_Complex_Attribute()
    {
        var doc = DocumentWithFeatureName(
            new S101Attribute(Drval1Code, 1, "5"),
            new S101Attribute(NameCode, 1, "Elliott Bay", ParentIndex: 1),
            new S101Attribute(LanguageCode, 1, "eng", ParentIndex: 9));

        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoderWithFeatureName()));

        var findings = report.Findings.Where(f => f.RuleId == "S101-R-1.2").ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Contains("does not point at a complex attribute", f.Message));
    }

    [Fact]
    public void R1_2_Finding_On_A_Point_Feature_Carries_Its_Position()
    {
        var doc = DocumentWithFeatureName(new S101Attribute(NameCode, 1, "Elliott Bay"));

        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoderWithFeatureName()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.2");
        Assert.NotNull(finding.Point);
        Assert.Equal(47.4, finding.Point!.Value.Latitude, 9);
        Assert.Equal(-122.6, finding.Point!.Value.Longitude, 9);
        Assert.Null(finding.BoundingBox);
    }

    // ---------------------------------------------------------------
    // S101-R-3.2  orientation-aware ring walking
    // ---------------------------------------------------------------

    // A square ring p1 → p2 → p3 → p4 → p1 whose second edge is stored p3 → p2
    // and is therefore traversed in reverse (CUCO orientation 2).
    private static (S101PointRecord[] Points, S101CurveSegmentRecord[] Curves, S101CompositeCurveRecord Ring)
        SquareWithReversedEdge(uint firstPoint, uint firstCurve, uint compositeId, int offset, int size)
    {
        uint p1 = firstPoint, p2 = firstPoint + 1, p3 = firstPoint + 2, p4 = firstPoint + 3;
        S101PointRecord[] points =
        [
            new() { RecordId = p1, Y = offset, X = offset },
            new() { RecordId = p2, Y = offset, X = offset + size },
            new() { RecordId = p3, Y = offset + size, X = offset + size },
            new() { RecordId = p4, Y = offset + size, X = offset },
        ];
        S101CurveSegmentRecord[] curves =
        [
            MakeCurve(firstCurve, p1, p2),
            MakeCurve(firstCurve + 1, p3, p2),
            MakeCurve(firstCurve + 2, p3, p4),
            MakeCurve(firstCurve + 3, p4, p1),
        ];
        var ring = new S101CompositeCurveRecord
        {
            RecordId = compositeId,
            CurveComponents =
            [
                new S101CurveUsage(120, firstCurve, 1),
                new S101CurveUsage(120, firstCurve + 1, 2),
                new S101CurveUsage(120, firstCurve + 2, 1),
                new S101CurveUsage(120, firstCurve + 3, 1),
            ],
        };
        return (points, curves, ring);
    }

    [Fact]
    public void R3_2_Passes_For_Composite_Rings_With_Reversed_Components_And_Several_Holes()
    {
        var outer = SquareWithReversedEdge(firstPoint: 1, firstCurve: 101, compositeId: 201, offset: 0, size: 1000);
        var holeA = SquareWithReversedEdge(firstPoint: 11, firstCurve: 111, compositeId: 202, offset: 100, size: 100);
        var holeB = SquareWithReversedEdge(firstPoint: 21, firstCurve: 121, compositeId: 203, offset: 500, size: 100);
        var surface = new S101SurfaceRecord
        {
            RecordId = 300,
            RingAssociations =
            [
                new S101RingAssociation(125, 201, 1, 1),
                new S101RingAssociation(125, 202, 1, 2),
                new S101RingAssociation(125, 203, 1, 2),
            ],
        };
        var doc = Document(
            points: [.. outer.Points, .. holeA.Points, .. holeB.Points],
            curves: [.. outer.Curves, .. holeA.Curves, .. holeB.Curves],
            composites: [outer.Ring, holeA.Ring, holeB.Ring],
            surfaces: [surface]);

        var report = S101DatasetRules.Default.Run(ViewOf(doc));

        Assert.DoesNotContain(report.Findings, f => f.RuleId is "S101-R-3.2" or "S101-R-3.3");
    }

    [Fact]
    public void R3_2_Open_Ring_Finding_Is_Located_At_The_Gap()
    {
        var p1 = new S101PointRecord { RecordId = 1, Y = 474_000_000, X = -1_226_000_000 };
        var p2 = new S101PointRecord { RecordId = 2, Y = 474_000_000, X = -1_225_000_000 };
        var p3 = new S101PointRecord { RecordId = 3, Y = 475_000_000, X = -1_225_000_000 };
        var composite = new S101CompositeCurveRecord
        {
            RecordId = 50,
            CurveComponents = [new S101CurveUsage(120, 11, 1), new S101CurveUsage(120, 12, 1)],
        };
        var surface = new S101SurfaceRecord
        {
            RecordId = 60,
            RingAssociations = [new S101RingAssociation(125, 50, 1, 1)],
        };
        var doc = Document(
            points: [p1, p2, p3],
            curves: [MakeCurve(11, 1, 2), MakeCurve(12, 2, 3)],
            composites: [composite],
            surfaces: [surface]);

        var report = S101DatasetRules.Default.Run(ViewOf(doc));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-3.2");
        Assert.Contains("not closed", finding.Message);
        Assert.Equal(47.5, finding.Point!.Value.Latitude, 9);
        Assert.Equal(-122.5, finding.Point!.Value.Longitude, 9);
        var box = finding.BoundingBox!;
        Assert.Equal(47.4, box.SouthLatitude, 9);
        Assert.Equal(47.5, box.NorthLatitude, 9);
        Assert.Equal(-122.6, box.WestLongitude, 9);
        Assert.Equal(-122.5, box.EastLongitude, 9);
    }

    [Fact]
    public void Finding_On_A_Surface_Feature_Carries_Its_Envelope()
    {
        var square = SquareWithReversedEdge(firstPoint: 1, firstCurve: 101, compositeId: 201,
            offset: 474_000_000, size: 1_000_000);
        var surface = new S101SurfaceRecord
        {
            RecordId = 300,
            RingAssociations = [new S101RingAssociation(125, 201, 1, 1)],
        };
        // DRVAL1 is not bound to Sounding.
        var doc = Document(
            features: [Feature(1, typeCode: SoundingCode, attributes: [new S101Attribute(Drval1Code, 1, "5")],
                spatial: [new S101SpatialAssociation(130, 300, 1)])],
            points: square.Points,
            curves: square.Curves,
            composites: [square.Ring],
            surfaces: [surface]);

        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.2");
        Assert.Null(finding.Point);
        var box = finding.BoundingBox!;
        Assert.Equal(47.4, box.SouthLatitude, 9);
        Assert.Equal(47.5, box.NorthLatitude, 9);
        Assert.Equal(47.4, box.WestLongitude, 9);
        Assert.Equal(47.5, box.EastLongitude, 9);
    }
}
