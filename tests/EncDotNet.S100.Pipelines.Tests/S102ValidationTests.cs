using System.Collections.Immutable;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S102.Validation;
using EncDotNet.S100.Validation;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Synthetic-fixture tests for the V-1 S-102 rule pack
/// (<see cref="S102DatasetRules"/>) and the
/// <see cref="ConcatReports"/> helper. No real HDF5 files
/// required; every fixture is constructed in-memory.
/// </summary>
public class S102ValidationTests
{
    private const float NoData = 1_000_000f;

    private static BathymetryCoverage MakeCoverage(
        int rows = 2,
        int cols = 2,
        double originLat = 50.0,
        double originLon = -1.0,
        double spacingLat = 0.01,
        double spacingLon = 0.01,
        BathymetryValue[]? values = null,
        string? groupPath = "/BathymetryCoverage/BathymetryCoverage.01")
        => new()
        {
            OriginLatitude = originLat,
            OriginLongitude = originLon,
            SpacingLatitudinal = spacingLat,
            SpacingLongitudinal = spacingLon,
            NumPointsLatitudinal = rows,
            NumPointsLongitudinal = cols,
            GroupPath = groupPath,
            Values = values ?? FillDepths(rows * cols, 10f),
        };

    private static BathymetryValue[] FillDepths(int count, float depth, float uncertainty = 0.1f)
    {
        var arr = new BathymetryValue[count];
        for (var i = 0; i < count; i++)
            arr[i] = new BathymetryValue(depth, uncertainty);
        return arr;
    }

    private static S102Dataset MakeDataset(
        params BathymetryCoverage[] coverages)
        => new()
        {
            HorizontalCRS = 4326,
            IssueDate = "2024-05-01",
            Coverages = coverages.Length == 0
                ? new[] { MakeCoverage() }
                : coverages,
        };

    // ----- R-1.1 -----

    [Fact]
    public void R1_1_Fires_When_Values_Length_Mismatches_Shape()
    {
        var coverage = MakeCoverage(rows: 2, cols: 3, values: FillDepths(5, 10f)); // expected 6
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-1.1");
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
    }

    [Fact]
    public void R1_1_Does_Not_Fire_When_Shape_Matches()
    {
        var coverage = MakeCoverage(rows: 2, cols: 3, values: FillDepths(6, 10f));
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-1.1");
    }

    // ----- R-2.1 -----

    [Fact]
    public void R2_1_Fires_On_NaN_Depth_Sentinel()
    {
        var values = FillDepths(4, 10f);
        values[1] = new BathymetryValue(float.NaN, 0.1f);
        values[2] = new BathymetryValue(float.PositiveInfinity, 0.1f);
        var coverage = MakeCoverage(rows: 2, cols: 2, values: values);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        var r21 = report.Findings.Where(x => x.RuleId == "S102-R-2.1").ToList();
        Assert.Equal(2, r21.Count);
        Assert.All(r21, f => Assert.Equal(ValidationSeverity.Error, f.Severity));
        Assert.Contains(r21, f => f.RelatedFeatureId == "/BathymetryCoverage/BathymetryCoverage.01[0,1]");
        Assert.Contains(r21, f => f.RelatedFeatureId == "/BathymetryCoverage/BathymetryCoverage.01[1,0]");
    }

    [Fact]
    public void R2_1_Does_Not_Fire_When_NoData_Is_Canonical_Sentinel()
    {
        var values = FillDepths(4, 10f);
        values[3] = new BathymetryValue(NoData, NoData);
        var coverage = MakeCoverage(rows: 2, cols: 2, values: values);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-2.1");
    }

    [Fact]
    public void R2_1_Does_Not_Flag_Finite_Out_Of_Range_Values()
    {
        // Conservative per design §6.1: finite values are addressed by R-5.1, not R-2.1.
        var values = FillDepths(4, 99_999_999f);
        var coverage = MakeCoverage(rows: 2, cols: 2, values: values);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-2.1");
    }

    // ----- R-3.1 -----

    [Fact]
    public void R3_1_Fires_On_Unknown_Epsg()
    {
        var dataset = new S102Dataset
        {
            HorizontalCRS = 12345,
            Coverages = new[] { MakeCoverage() },
        };

        var report = S102DatasetRules.Default.Run(dataset);
        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-3.1");
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
    }

    [Theory]
    [InlineData(4326)]
    [InlineData(4269)]
    [InlineData(32630)]
    [InlineData(32750)]
    public void R3_1_Does_Not_Fire_On_Known_Epsg(int code)
    {
        var dataset = new S102Dataset
        {
            HorizontalCRS = code,
            Coverages = new[] { MakeCoverage() },
        };

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-3.1");
    }

    [Fact]
    public void R3_1_Does_Not_Fire_When_HorizontalCRS_Unset()
    {
        var dataset = new S102Dataset
        {
            HorizontalCRS = null,
            Coverages = new[] { MakeCoverage() },
        };

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-3.1");
    }

    // ----- R-3.2 -----

    [Fact]
    public void R3_2_Fires_On_Unparseable_IssueDate()
    {
        var dataset = new S102Dataset
        {
            IssueDate = "not-a-date",
            Coverages = new[] { MakeCoverage() },
        };

        var report = S102DatasetRules.Default.Run(dataset);
        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-3.2");
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
    }

    [Theory]
    [InlineData("2024-05-01")]
    [InlineData("2024-05-01T12:00:00Z")]
    [InlineData("2024-05-01T12:00:00+02:00")]
    public void R3_2_Does_Not_Fire_On_Iso8601(string date)
    {
        var dataset = new S102Dataset
        {
            IssueDate = date,
            Coverages = new[] { MakeCoverage() },
        };

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-3.2");
    }

    // ----- R-4.1 -----

    [Fact]
    public void R4_1_Fires_On_Out_Of_Range_Origin()
    {
        var coverage = MakeCoverage(originLat: 95.0, originLon: -1.0);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-4.1");
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
    }

    [Fact]
    public void R4_1_Does_Not_Fire_On_In_Range_Origin()
    {
        var dataset = MakeDataset(MakeCoverage(originLat: 50.0, originLon: -1.0));
        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-4.1");
    }

    // ----- R-4.2 -----

    [Fact]
    public void R4_2_Fires_When_Extent_Wraps_Antimeridian()
    {
        var coverage = MakeCoverage(
            rows: 2, cols: 10,
            originLat: 50.0, originLon: 179.0,
            spacingLat: 0.01, spacingLon: 1.0); // lonEnd = 179 + 9 = 188
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-4.2");
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.NotNull(f.BoundingBox);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
    }

    [Fact]
    public void R4_2_Does_Not_Fire_When_Extent_In_Range()
    {
        var dataset = MakeDataset(MakeCoverage(rows: 2, cols: 2, spacingLat: 0.01, spacingLon: 0.01));
        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-4.2");
    }

    // ----- R-5.1 -----

    [Fact]
    public void R5_1_Fires_Once_Per_Coverage_With_Out_Of_Range_Cells()
    {
        var values = FillDepths(4, 10f);
        values[0] = new BathymetryValue(-100f, 0.1f); // outside [-50, 12000]
        values[1] = new BathymetryValue(20_000f, 0.1f);
        var coverage = MakeCoverage(rows: 2, cols: 2, values: values);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-5.1");
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Contains("2 non-NODATA depth value(s)", f.Message);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
    }

    [Fact]
    public void R5_1_Does_Not_Fire_When_All_Depths_In_Range_Or_NoData()
    {
        var values = FillDepths(4, 10f);
        values[3] = new BathymetryValue(NoData, NoData);
        var coverage = MakeCoverage(rows: 2, cols: 2, values: values);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-R-5.1");
    }

    // ----- S102-PROJ-SCHEMA -----

    [Fact]
    public void ProjectionSchemaSurrogate_Body_Emits_Nothing()
    {
        var dataset = MakeDataset();
        var report = S102DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S102-PROJ-SCHEMA");
    }

    // ----- Clean dataset & multi-coverage RelatedFeatureId -----

    [Fact]
    public void Clean_Dataset_Produces_Valid_Report()
    {
        var dataset = MakeDataset();
        var report = S102DatasetRules.Default.Run(dataset);
        Assert.True(report.IsValid, $"Expected no findings, got: {string.Join("; ", report.Findings.Select(f => f.RuleId))}");
        Assert.Equal(8, report.RulesEvaluated);
    }

    [Fact]
    public void Multi_Coverage_Findings_Carry_Per_Coverage_GroupPath()
    {
        var a = MakeCoverage(rows: 2, cols: 2, values: FillDepths(3, 10f), // length mismatch
            groupPath: "/BathymetryCoverage/BathymetryCoverage.01");
        var b = MakeCoverage(rows: 2, cols: 2, values: FillDepths(3, 10f),
            groupPath: "/BathymetryCoverage/BathymetryCoverage.02");
        var dataset = MakeDataset(a, b);

        var report = S102DatasetRules.Default.Run(dataset);
        var r11 = report.Findings.Where(x => x.RuleId == "S102-R-1.1").ToList();
        Assert.Equal(2, r11.Count);
        Assert.Contains(r11, f => f.RelatedFeatureId == a.GroupPath);
        Assert.Contains(r11, f => f.RelatedFeatureId == b.GroupPath);
    }

    [Fact]
    public void Coverage_Without_GroupPath_Falls_Back_To_Synthesised_Path()
    {
        var coverage = MakeCoverage(rows: 2, cols: 2, values: FillDepths(3, 10f), groupPath: null);
        var dataset = MakeDataset(coverage);

        var report = S102DatasetRules.Default.Run(dataset);
        var f = Assert.Single(report.Findings, x => x.RuleId == "S102-R-1.1");
        Assert.Equal("/BathymetryCoverage/BathymetryCoverage.01", f.RelatedFeatureId);
    }

    // ----- ConcatReports -----

    [Fact]
    public void ConcatReports_Sums_Counters_And_Preserves_Order()
    {
        var a = new ValidationReport(
            ImmutableArray.Create(new ValidationFinding
            {
                RuleId = "A-1",
                Severity = ValidationSeverity.Error,
                Message = "first",
            }),
            RulesEvaluated: 2,
            RulesWithFindings: 1);

        var b = new ValidationReport(
            ImmutableArray.Create(
                new ValidationFinding { RuleId = "B-1", Severity = ValidationSeverity.Warning, Message = "second" },
                new ValidationFinding { RuleId = "B-2", Severity = ValidationSeverity.Info, Message = "third" }),
            RulesEvaluated: 3,
            RulesWithFindings: 2);

        var combined = ConcatReports.Concat(a, b);

        Assert.Equal(5, combined.RulesEvaluated);
        Assert.Equal(3, combined.RulesWithFindings);
        Assert.Equal(new[] { "A-1", "B-1", "B-2" }, combined.Findings.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void ConcatReports_Rebadges_Second_Reports_Rule_Ids()
    {
        var a = new ValidationReport(
            ImmutableArray.Create(new ValidationFinding
            {
                RuleId = "S57-R-1.1",
                Severity = ValidationSeverity.Error,
                Message = "from pre",
            }),
            RulesEvaluated: 1,
            RulesWithFindings: 1);

        var b = new ValidationReport(
            ImmutableArray.Create(new ValidationFinding
            {
                RuleId = "S101-R-2.1",
                Severity = ValidationSeverity.Warning,
                Message = "from translated",
            }),
            RulesEvaluated: 1,
            RulesWithFindings: 1);

        var combined = ConcatReports.Concat(a, b, rebadgePrefix: "S101-as-S57/");

        Assert.Equal("S57-R-1.1", combined.Findings[0].RuleId);
        Assert.Equal("S101-as-S57/S101-R-2.1", combined.Findings[1].RuleId);
    }
}
