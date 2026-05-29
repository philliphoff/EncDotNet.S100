using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Validation;
using Xunit;

namespace EncDotNet.S100.Datasets.S101.Tests.Validation;

/// <summary>
/// Synthetic-fixture tests for the V-4 S-101 rule pack
/// (<see cref="S101DatasetRules"/>). Fixtures are built in-memory by
/// composing <see cref="S101Document"/> instances directly; no real
/// ENC datasets are required.
/// </summary>
public class S101ValidationTests
{
    // ---------------------------------------------------------------
    // Fixture helpers
    // ---------------------------------------------------------------

    private const ushort DepthAreaCode = 1;       // FC acronym "DepthArea"
    private const ushort SoundingCode = 2;        // FC acronym "Sounding"
    private const ushort ObstrCode = 3;           // FC acronym "Obstruction"
    private const ushort UnknownFeatureCode = 99; // intentionally not in any catalogue

    private const ushort Drval1Code = 10;   // FC acronym "DRVAL1"
    private const ushort Drval2Code = 11;   // FC acronym "DRVAL2"
    private const ushort Objnam = 12;       // FC acronym "OBJNAM"
    private const ushort CatpibCode = 13;   // FC acronym "CATPIB" (enumerated)
    private const ushort UnknownAttrCode = 999;

    private static S101DatasetIdentification Identification(string name = "TESTDS") => new()
    {
        RecordName = 10,
        RecordId = 1,
        EncodingSpecification = "S-100 Part 10a",
        EncodingSpecificationEdition = "1.0",
        ProductSpecification = "INT.IHO.S-101.1.0",
        ProductSpecificationEdition = "1.0",
        ApplicationProfile = "",
        DatasetName = name,
        DatasetTitle = name,
        DatasetReferenceDate = "2026-05-28",
        DatasetLanguage = "eng",
    };

    private static S101DatasetStructureInfo StructureInfo(uint cmf = 10_000_000) => new()
    {
        CoordinateMultiplicationFactorX = cmf,
        CoordinateMultiplicationFactorY = cmf,
        CoordinateMultiplicationFactorZ = cmf,
    };

    private static ImmutableDictionary<ushort, string> FeatureCatalogueByDefault =>
        new Dictionary<ushort, string>
        {
            [DepthAreaCode] = "DepthArea",
            [SoundingCode] = "Sounding",
            [ObstrCode] = "Obstruction",
        }.ToImmutableDictionary();

    private static ImmutableDictionary<ushort, string> AttributeCatalogueByDefault =>
        new Dictionary<ushort, string>
        {
            [Drval1Code] = "DRVAL1",
            [Drval2Code] = "DRVAL2",
            [Objnam] = "OBJNAM",
            [CatpibCode] = "CATPIB",
        }.ToImmutableDictionary();

    private static S101Document Document(
        IEnumerable<S101FeatureRecord>? features = null,
        IEnumerable<S101PointRecord>? points = null,
        IEnumerable<S101CurveSegmentRecord>? curves = null,
        IEnumerable<S101CompositeCurveRecord>? composites = null,
        IEnumerable<S101SurfaceRecord>? surfaces = null,
        IEnumerable<S101InformationRecord>? information = null,
        ImmutableDictionary<ushort, string>? featureCatalogue = null,
        ImmutableDictionary<ushort, string>? attributeCatalogue = null)
        => new()
        {
            Identification = Identification(),
            StructureInfo = StructureInfo(),
            FeatureTypeCatalogue = featureCatalogue ?? FeatureCatalogueByDefault,
            AttributeTypeCatalogue = attributeCatalogue ?? AttributeCatalogueByDefault,
            Points = (points ?? Array.Empty<S101PointRecord>()).ToImmutableDictionary(p => p.RecordId),
            CurveSegments = (curves ?? Array.Empty<S101CurveSegmentRecord>()).ToImmutableDictionary(c => c.RecordId),
            CompositeCurves = (composites ?? Array.Empty<S101CompositeCurveRecord>()).ToImmutableDictionary(c => c.RecordId),
            Surfaces = (surfaces ?? Array.Empty<S101SurfaceRecord>()).ToImmutableDictionary(s => s.RecordId),
            Features = (features ?? Array.Empty<S101FeatureRecord>()).ToImmutableArray(),
            InformationTypes = (information ?? Array.Empty<S101InformationRecord>()).ToImmutableDictionary(i => i.RecordId),
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };

    private static S101FeatureRecord Feature(
        uint rcid,
        ushort typeCode = DepthAreaCode,
        ushort agency = 1,
        uint fidn = 1,
        ushort fids = 1,
        ImmutableArray<S101Attribute>? attributes = null,
        ImmutableArray<S101SpatialAssociation>? spatial = null,
        ImmutableArray<S101InformationAssociation>? info = null)
        => new()
        {
            RecordId = rcid,
            FeatureTypeCode = typeCode,
            ProducingAgency = agency,
            FeatureIdentificationNumber = fidn,
            FeatureIdentificationSubdivision = fids,
            Attributes = attributes ?? ImmutableArray<S101Attribute>.Empty,
            SpatialAssociations = spatial ?? ImmutableArray<S101SpatialAssociation>.Empty,
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = info ?? ImmutableArray<S101InformationAssociation>.Empty,
        };

    private static S101DatasetView ViewOf(S101Document doc, FeatureCatalogueDecoder? decoder = null)
        => S101DatasetView.From(doc, decoder);

    /// <summary>
    /// Synthesises an in-memory <see cref="FeatureCatalogueDecoder"/>
    /// matching the synthetic feature-type / attribute catalogues used
    /// by these tests. Keeps the test suite independent of bundled FC
    /// XML.
    /// </summary>
    private static FeatureCatalogueDecoder BuildDecoder()
    {
        var fc = new FeatureCatalogue
        {
            Name = "Test",
            VersionNumber = "0.0",
            VersionDate = "2026-05-28",
            ProductId = "S-101",
            SimpleAttributes = new SimpleAttribute[]
            {
                new() { Code = "DRVAL1", Name = "Depth range value 1", ValueType = "Real" },
                new() { Code = "DRVAL2", Name = "Depth range value 2", ValueType = "Real" },
                new() { Code = "OBJNAM", Name = "Object name", ValueType = "Text" },
                new() {
                    Code = "CATPIB", Name = "Category of pilot boarding place", ValueType = "Enumeration",
                    ListedValues = new ListedValue[]
                    {
                        new() { Code = "1", Label = "Helicopter" },
                        new() { Code = "2", Label = "Boat" },
                    },
                },
            },
            FeatureTypes = new FeatureType[]
            {
                new() {
                    Code = "DepthArea", Name = "Depth area",
                    AttributeBindings = new AttributeBinding[]
                    {
                        new() { AttributeRef = "DRVAL1", Multiplicity = new Multiplicity { Lower = 0, Upper = 1 } },
                        new() { AttributeRef = "DRVAL2", Multiplicity = new Multiplicity { Lower = 0, Upper = 1 } },
                        new() { AttributeRef = "OBJNAM", Multiplicity = new Multiplicity { Lower = 0, Upper = 1 } },
                    },
                },
                new() {
                    Code = "Sounding", Name = "Sounding",
                    AttributeBindings = new AttributeBinding[]
                    {
                        new() { AttributeRef = "OBJNAM", Multiplicity = new Multiplicity { Lower = 0, Upper = 1 } },
                    },
                },
                new() {
                    Code = "Obstruction", Name = "Obstruction",
                    AttributeBindings = new AttributeBinding[]
                    {
                        new() { AttributeRef = "CATPIB", Multiplicity = new Multiplicity { Lower = 0, Upper = 1 } },
                    },
                },
            },
        };
        return new FeatureCatalogueDecoder(fc);
    }

    // ---------------------------------------------------------------
    // Default / all-green
    // ---------------------------------------------------------------

    [Fact]
    public void Default_RuleSet_Has_Ten_Rules()
    {
        Assert.Equal(10, S101DatasetRules.Default.Rules.Length);
    }

    [Fact]
    public void Clean_Dataset_Produces_No_Findings()
    {
        var doc = Document(features: new[] { Feature(1) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));

        Assert.True(report.IsValid,
            $"Expected no findings; got: {string.Join(", ", report.Findings.Select(f => f.RuleId + ": " + f.Message))}");
        Assert.Equal(10, report.RulesEvaluated);
        Assert.Equal(0, report.RulesWithFindings);
    }

    // ---------------------------------------------------------------
    // S101-R-1.1  Feature type code resolves
    // ---------------------------------------------------------------

    [Fact]
    public void R1_1_Fires_When_Feature_Type_Code_Not_In_Catalogue()
    {
        var doc = Document(features: new[] { Feature(1, typeCode: UnknownFeatureCode) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.1");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains(UnknownFeatureCode.ToString(), finding.Message);
        Assert.Equal("1:1.1", finding.RelatedFeatureId);
    }

    [Fact]
    public void R1_1_Passes_When_All_Feature_Type_Codes_Resolve()
    {
        var doc = Document(features: new[]
        {
            Feature(1, typeCode: DepthAreaCode),
            Feature(2, typeCode: SoundingCode, fidn: 2),
        });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-1.1");
    }

    // ---------------------------------------------------------------
    // S101-R-1.2  Attribute code resolution + FC binding
    // ---------------------------------------------------------------

    [Fact]
    public void R1_2_Fires_When_Attribute_Code_Not_In_Catalogue()
    {
        var attrs = ImmutableArray.Create(new S101Attribute(UnknownAttrCode, 1, "x"));
        var doc = Document(features: new[] { Feature(1, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.2");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains(UnknownAttrCode.ToString(), finding.Message);
    }

    [Fact]
    public void R1_2_Fires_When_Attribute_Not_Bound_To_Feature_Class()
    {
        // DRVAL1 is bound to DepthArea, not to Sounding.
        var attrs = ImmutableArray.Create(new S101Attribute(Drval1Code, 1, "5.0"));
        var doc = Document(features: new[] { Feature(1, typeCode: SoundingCode, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-1.2");
        Assert.Contains("DRVAL1", finding.Message);
        Assert.Contains("Sounding", finding.Message);
    }

    [Fact]
    public void R1_2_Passes_When_Attribute_Bound_To_Feature_Class()
    {
        var attrs = ImmutableArray.Create(new S101Attribute(Drval1Code, 1, "5.0"));
        var doc = Document(features: new[] { Feature(1, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-1.2");
    }

    [Fact]
    public void R1_2_Skipped_When_Decoder_Unavailable()
    {
        // Attribute not bound to feature class — without a decoder the
        // FC-conformance half of the rule is silently skipped.
        var attrs = ImmutableArray.Create(new S101Attribute(Drval1Code, 1, "5.0"));
        var doc = Document(features: new[] { Feature(1, typeCode: SoundingCode, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, decoder: null));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-1.2");
    }

    // ---------------------------------------------------------------
    // S101-R-2.1  FOID uniqueness
    // ---------------------------------------------------------------

    [Fact]
    public void R2_1_Fires_Once_Per_Duplicate_With_First_As_Anchor()
    {
        // Anchor (rcid 1) + two duplicates (rcid 2, 3) all share FOID 1:1.1
        var doc = Document(features: new[]
        {
            Feature(rcid: 1, agency: 1, fidn: 1, fids: 1),
            Feature(rcid: 2, agency: 1, fidn: 1, fids: 1),
            Feature(rcid: 3, agency: 1, fidn: 1, fids: 1),
        });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        var foid = report.Findings.Where(f => f.RuleId == "S101-R-2.1").ToList();
        Assert.Equal(2, foid.Count);
        Assert.All(foid, f => Assert.Equal("1:1.1", f.RelatedFeatureId));
        Assert.Contains(foid, f => f.Message.Contains("RCID 2") && f.Message.Contains("RCID 1"));
        Assert.Contains(foid, f => f.Message.Contains("RCID 3") && f.Message.Contains("RCID 1"));
    }

    [Fact]
    public void R2_1_Passes_When_Foids_Are_Unique()
    {
        var doc = Document(features: new[]
        {
            Feature(1, fidn: 1),
            Feature(2, fidn: 2),
            Feature(3, fidn: 3),
        });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-2.1");
    }

    // ---------------------------------------------------------------
    // S101-R-3.1  Spatial associations resolve
    // ---------------------------------------------------------------

    [Fact]
    public void R3_1_Fires_When_Spatial_Reference_Is_Dangling()
    {
        var spas = ImmutableArray.Create(new S101SpatialAssociation(RecordName: 110, RecordId: 999, Orientation: 1));
        var doc = Document(features: new[] { Feature(1, spatial: spas) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-3.1");
        Assert.Contains("RCID=999", finding.Message);
        Assert.Contains("RCNM=110", finding.Message);
    }

    [Fact]
    public void R3_1_Passes_When_Spatial_Reference_Resolves()
    {
        var pt = new S101PointRecord { RecordId = 5, Y = 500_000_000, X = 100_000_000 };
        var spas = ImmutableArray.Create(new S101SpatialAssociation(110, 5, 1));
        var doc = Document(
            features: new[] { Feature(1, spatial: spas) },
            points: new[] { pt });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-3.1");
    }

    // ---------------------------------------------------------------
    // S101-R-3.2  Surface ring closure
    // ---------------------------------------------------------------

    [Fact]
    public void R3_2_Fires_When_Surface_Ring_Does_Not_Close()
    {
        var p1 = new S101PointRecord { RecordId = 1, Y = 0, X = 0 };
        var p2 = new S101PointRecord { RecordId = 2, Y = 10, X = 0 };
        var p3 = new S101PointRecord { RecordId = 3, Y = 10, X = 10 };
        var p4 = new S101PointRecord { RecordId = 4, Y = 99, X = 99 }; // does NOT match p1

        // Curve A: p1 → p2; Curve B: p2 → p3; Curve C: p3 → p4
        var curveA = MakeCurve(rcid: 11, beginPoint: 1, endPoint: 2);
        var curveB = MakeCurve(rcid: 12, beginPoint: 2, endPoint: 3);
        var curveC = MakeCurve(rcid: 13, beginPoint: 3, endPoint: 4);

        var surface = new S101SurfaceRecord
        {
            RecordId = 20,
            RingAssociations = ImmutableArray.Create(
                new S101RingAssociation(RecordName: 120, RecordId: 11, Orientation: 1, Usage: 1),
                new S101RingAssociation(120, 12, 1, 1),
                new S101RingAssociation(120, 13, 1, 1)),
        };

        var doc = Document(
            points: new[] { p1, p2, p3, p4 },
            curves: new[] { curveA, curveB, curveC },
            surfaces: new[] { surface });

        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        var f = Assert.Single(report.Findings, x => x.RuleId == "S101-R-3.2");
        Assert.Equal("surf:20", f.RelatedFeatureId);
        Assert.Contains("not closed", f.Message);
    }

    [Fact]
    public void R3_2_Fires_When_Ring_Has_Fewer_Than_Three_Distinct_Vertices()
    {
        var p1 = new S101PointRecord { RecordId = 1, Y = 0, X = 0 };
        var p2 = new S101PointRecord { RecordId = 2, Y = 10, X = 10 };
        var curveA = MakeCurve(11, 1, 2);
        var curveB = MakeCurve(12, 2, 1);
        var surface = new S101SurfaceRecord
        {
            RecordId = 30,
            RingAssociations = ImmutableArray.Create(
                new S101RingAssociation(120, 11, 1, 1),
                new S101RingAssociation(120, 12, 1, 1)),
        };
        var doc = Document(
            points: new[] { p1, p2 },
            curves: new[] { curveA, curveB },
            surfaces: new[] { surface });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.Contains(report.Findings, f => f.RuleId == "S101-R-3.2" && f.Message.Contains("distinct"));
    }

    [Fact]
    public void R3_2_Passes_For_Closed_Triangle_Ring()
    {
        var p1 = new S101PointRecord { RecordId = 1, Y = 0, X = 0 };
        var p2 = new S101PointRecord { RecordId = 2, Y = 10, X = 0 };
        var p3 = new S101PointRecord { RecordId = 3, Y = 5, X = 10 };

        var cA = MakeCurve(11, 1, 2);
        var cB = MakeCurve(12, 2, 3);
        var cC = MakeCurve(13, 3, 1);

        var surface = new S101SurfaceRecord
        {
            RecordId = 40,
            RingAssociations = ImmutableArray.Create(
                new S101RingAssociation(120, 11, 1, 1),
                new S101RingAssociation(120, 12, 1, 1),
                new S101RingAssociation(120, 13, 1, 1)),
        };

        var doc = Document(
            points: new[] { p1, p2, p3 },
            curves: new[] { cA, cB, cC },
            surfaces: new[] { surface });

        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-3.2");
    }

    // ---------------------------------------------------------------
    // S101-R-3.3  Composite curve continuity
    // ---------------------------------------------------------------

    [Fact]
    public void R3_3_Fires_When_Composite_Curve_Components_Discontinuous()
    {
        var p1 = new S101PointRecord { RecordId = 1, Y = 0, X = 0 };
        var p2 = new S101PointRecord { RecordId = 2, Y = 10, X = 0 };
        var p3 = new S101PointRecord { RecordId = 3, Y = 50, X = 50 }; // discontinuity
        var p4 = new S101PointRecord { RecordId = 4, Y = 60, X = 60 };

        var cA = MakeCurve(11, 1, 2);
        var cB = MakeCurve(12, 3, 4); // begin (3) != previous end (2)

        var composite = new S101CompositeCurveRecord
        {
            RecordId = 50,
            CurveComponents = ImmutableArray.Create(
                new S101CurveUsage(RecordName: 120, RecordId: 11, Orientation: 1),
                new S101CurveUsage(120, 12, 1)),
        };

        var doc = Document(
            points: new[] { p1, p2, p3, p4 },
            curves: new[] { cA, cB },
            composites: new[] { composite });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-3.3");
        Assert.Equal("composite:50", finding.RelatedFeatureId);
    }

    [Fact]
    public void R3_3_Passes_For_Continuous_Composite_Curve()
    {
        var p1 = new S101PointRecord { RecordId = 1, Y = 0, X = 0 };
        var p2 = new S101PointRecord { RecordId = 2, Y = 10, X = 0 };
        var p3 = new S101PointRecord { RecordId = 3, Y = 20, X = 0 };
        var cA = MakeCurve(11, 1, 2);
        var cB = MakeCurve(12, 2, 3);
        var composite = new S101CompositeCurveRecord
        {
            RecordId = 60,
            CurveComponents = ImmutableArray.Create(
                new S101CurveUsage(120, 11, 1),
                new S101CurveUsage(120, 12, 1)),
        };
        var doc = Document(
            points: new[] { p1, p2, p3 },
            curves: new[] { cA, cB },
            composites: new[] { composite });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-3.3");
    }

    // ---------------------------------------------------------------
    // S101-R-4.1  Enumerated attribute domain
    // ---------------------------------------------------------------

    [Fact]
    public void R4_1_Fires_When_Enumerated_Value_Out_Of_Domain()
    {
        // CATPIB listed values are "1" and "2"; "9" is out of domain.
        var attrs = ImmutableArray.Create(new S101Attribute(CatpibCode, 1, "9"));
        var doc = Document(features: new[] { Feature(1, typeCode: ObstrCode, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-4.1");
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Contains("CATPIB", finding.Message);
        Assert.Contains("'9'", finding.Message);
    }

    [Fact]
    public void R4_1_Passes_When_Enumerated_Value_In_Domain()
    {
        var attrs = ImmutableArray.Create(new S101Attribute(CatpibCode, 1, "1"));
        var doc = Document(features: new[] { Feature(1, typeCode: ObstrCode, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-4.1");
    }

    [Fact]
    public void R4_1_Skipped_For_Non_Enumerated_Attribute()
    {
        var attrs = ImmutableArray.Create(new S101Attribute(Objnam, 1, "anything"));
        var doc = Document(features: new[] { Feature(1, attributes: attrs) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-4.1");
    }

    // ---------------------------------------------------------------
    // S101-R-5.1  Coordinates within WGS-84 range
    // ---------------------------------------------------------------

    [Fact]
    public void R5_1_Fires_When_Point_Latitude_Out_Of_Range()
    {
        // CMF default = 10^7; Y = 100*10^7 = 100 degrees, out of [-90, 90].
        var pt = new S101PointRecord { RecordId = 1, Y = 1_000_000_000, X = 0 };
        var doc = Document(points: new[] { pt });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.Contains(report.Findings, f => f.RuleId == "S101-R-5.1");
    }

    [Fact]
    public void R5_1_Passes_For_In_Range_Coordinates()
    {
        var pt = new S101PointRecord { RecordId = 1, Y = 500_000_000, X = 0 };
        var doc = Document(points: new[] { pt });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-5.1");
    }

    // ---------------------------------------------------------------
    // S101-R-5.2  Information associations resolve
    // ---------------------------------------------------------------

    [Fact]
    public void R5_2_Fires_When_Information_Association_Dangling()
    {
        var info = ImmutableArray.Create(new S101InformationAssociation(0, 999, 0));
        var doc = Document(features: new[] { Feature(1, info: info) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        var finding = Assert.Single(report.Findings, f => f.RuleId == "S101-R-5.2");
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Contains("999", finding.Message);
    }

    [Fact]
    public void R5_2_Passes_When_Information_Association_Resolves()
    {
        var iRec = new S101InformationRecord { RecordId = 7, InformationTypeCode = 1, Attributes = ImmutableArray<S101Attribute>.Empty };
        var info = ImmutableArray.Create(new S101InformationAssociation(0, 7, 0));
        var doc = Document(
            features: new[] { Feature(1, info: info) },
            information: new[] { iRec });
        var report = S101DatasetRules.Default.Run(ViewOf(doc));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-R-5.2");
    }

    // ---------------------------------------------------------------
    // S101-PROJ-PARSE  Placeholder rule
    // ---------------------------------------------------------------

    [Fact]
    public void ProjParse_Rule_Is_Registered_But_Emits_No_Findings_For_Stance_A()
    {
        var doc = Document(features: new[] { Feature(1) });
        var report = S101DatasetRules.Default.Run(ViewOf(doc, BuildDecoder()));

        // The rule is in the set (so consumers can see / filter by its
        // id) but Stance A keeps the body empty.
        Assert.Contains(S101DatasetRules.Default.Rules, r => r.RuleId == "S101-PROJ-PARSE");
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S101-PROJ-PARSE");
    }

    // ---------------------------------------------------------------
    // Façade smoke tests
    // ---------------------------------------------------------------

    [Fact]
    public void View_OfType_Returns_Matching_Features()
    {
        var doc = Document(features: new[]
        {
            Feature(1, typeCode: DepthAreaCode, fidn: 1),
            Feature(2, typeCode: SoundingCode, fidn: 2),
            Feature(3, typeCode: DepthAreaCode, fidn: 3),
        });
        var view = ViewOf(doc);
        Assert.Equal(2, view.OfType("DepthArea").Count());
        Assert.Single(view.OfType("Sounding"));
        Assert.Empty(view.OfType("Bogus"));
    }

    [Fact]
    public void Feature_GetSimple_Returns_Attribute_Value()
    {
        var attrs = ImmutableArray.Create(
            new S101Attribute(Drval1Code, 1, "5.0"),
            new S101Attribute(Drval2Code, 1, "10.0"));
        var doc = Document(features: new[] { Feature(1, attributes: attrs) });
        var feature = ViewOf(doc).Features[0];
        Assert.Equal("5.0", feature.GetSimple("DRVAL1"));
        Assert.Equal("10.0", feature.GetSimple("DRVAL2"));
        Assert.Null(feature.GetSimple("OBJNAM"));
    }

    [Fact]
    public void Feature_FoidKey_Follows_Design_Note_Convention()
    {
        var doc = Document(features: new[] { Feature(1, agency: 7, fidn: 42, fids: 3) });
        var feature = ViewOf(doc).Features[0];
        Assert.Equal("7:42.3", feature.FoidKey);
    }

    [Fact]
    public void View_TryGetSpatial_Routes_By_RecordName()
    {
        var pt = new S101PointRecord { RecordId = 1, Y = 0, X = 0 };
        var sur = new S101SurfaceRecord { RecordId = 10, RingAssociations = ImmutableArray<S101RingAssociation>.Empty };
        var doc = Document(points: new[] { pt }, surfaces: new[] { sur });
        var view = ViewOf(doc);

        Assert.True(view.TryGetSpatial(new S101SpatialAssociation(110, 1, 1), out var p) && p is S101PointRecord);
        Assert.True(view.TryGetSpatial(new S101SpatialAssociation(130, 10, 1), out var s) && s is S101SurfaceRecord);
        Assert.False(view.TryGetSpatial(new S101SpatialAssociation(110, 999, 1), out _));
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static S101CurveSegmentRecord MakeCurve(uint rcid, uint beginPoint, uint endPoint)
        => new()
        {
            RecordId = rcid,
            PointAssociations = ImmutableArray.Create(
                new S101PointAssociation(RecordName: 110, RecordId: beginPoint, Topology: 1),
                new S101PointAssociation(110, endPoint, 2)),
            IntermediateCoordinates = ImmutableArray<(int, int)>.Empty,
        };
}
