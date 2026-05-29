using System;
using System.Linq;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S104.Validation;
using EncDotNet.S100.Validation;
using Xunit;

namespace EncDotNet.S100.Datasets.S104.Tests.Validation;

/// <summary>
/// Synthetic-fixture tests for the V-2 S-104 rule pack
/// (<see cref="S104DatasetRules"/>). No real HDF5 files
/// required; every fixture is constructed in-memory.
/// </summary>
public class S104ValidationTests
{
    private const float NoData = -9999.0f;

    private static readonly DateTime BaseTime =
        new(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);

    private static WaterLevelCoverage MakeCoverage(
        int rows = 2,
        int cols = 2,
        double originLat = 50.0,
        double originLon = -1.0,
        double spacingLat = 0.01,
        double spacingLon = 0.01,
        DateTime? timePoint = null,
        WaterLevelValue[]? values = null,
        string? groupPath = "/WaterLevel/WaterLevel.01")
        => new()
        {
            OriginLatitude = originLat,
            OriginLongitude = originLon,
            SpacingLatitudinal = spacingLat,
            SpacingLongitudinal = spacingLon,
            NumPointsLatitudinal = rows,
            NumPointsLongitudinal = cols,
            GroupPath = groupPath,
            TimePoint = timePoint ?? BaseTime,
            Values = values ?? FillHeights(rows * cols, 1.5f),
        };

    private static WaterLevelValue[] FillHeights(int count, float height, byte trend = 3)
    {
        var arr = new WaterLevelValue[count];
        for (var i = 0; i < count; i++)
            arr[i] = new WaterLevelValue(height, trend);
        return arr;
    }

    private static S104Dataset MakeDataset(
        int dcf = 2,
        string? method = "TidalForecast",
        params WaterLevelCoverage[] coverages)
        => new()
        {
            HorizontalCRS = 4326,
            IssueDate = "2026-05-28T00:00:00Z",
            DataCodingFormat = dcf,
            MethodWaterLevelProduct = method,
            Coverages = coverages.Length == 0
                ? new[] { MakeCoverage() }
                : coverages,
        };

    // ----- Default rule set / clean dataset -----

    [Fact]
    public void Default_Run_On_Clean_Dataset_Is_Valid()
    {
        // A clean single-coverage dataset (method allowed to be null since Count == 1).
        var dataset = new S104Dataset
        {
            HorizontalCRS = 4326,
            IssueDate = "2026-05-28T00:00:00Z",
            DataCodingFormat = 2,
            Coverages = new[] { MakeCoverage() },
        };

        var report = S104DatasetRules.Default.Run(dataset);

        Assert.True(report.IsValid, $"Expected clean dataset to be valid; got: {string.Join(", ", report.Findings.Select(f => f.RuleId))}");
        Assert.Equal(9, report.RulesEvaluated);
        Assert.Equal(0, report.RulesWithFindings);
    }

    // ----- R-1.1 -----

    [Fact]
    public void R1_1_Fires_When_Values_Length_Mismatches_Shape()
    {
        var coverage = MakeCoverage(rows: 2, cols: 3, values: FillHeights(5, 1.5f));
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-1.1");
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
    }

    [Fact]
    public void R1_1_Does_Not_Fire_When_Shape_Matches()
    {
        var coverage = MakeCoverage(rows: 2, cols: 3, values: FillHeights(6, 1.5f));
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-1.1");
    }

    // ----- R-1.2 -----

    [Fact]
    public void R1_2_Fires_On_Unsupported_DataCodingFormat()
    {
        var dataset = MakeDataset(dcf: 8, coverages: MakeCoverage());

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-1.2");
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Contains("8", f.Message);
    }

    [Fact]
    public void R1_2_Does_Not_Fire_For_Supported_DataCodingFormats()
    {
        var report2 = S104DatasetRules.Default.Run(MakeDataset(dcf: 2, coverages: MakeCoverage()));
        var report3 = S104DatasetRules.Default.Run(MakeDataset(dcf: 3, coverages: MakeCoverage()));

        Assert.DoesNotContain(report2.Findings, x => x.RuleId == "S104-R-1.2");
        Assert.DoesNotContain(report3.Findings, x => x.RuleId == "S104-R-1.2");
    }

    // ----- R-2.1 -----

    [Fact]
    public void R2_1_Fires_On_Non_Increasing_TimePoints()
    {
        var c1 = MakeCoverage(timePoint: BaseTime, groupPath: "/WaterLevel/WaterLevel.01");
        var c2 = MakeCoverage(timePoint: BaseTime, groupPath: "/WaterLevel/WaterLevel.01"); // equal — not strictly increasing
        var dataset = MakeDataset(coverages: new[] { c1, c2 });

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-2.1");
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal($"{c2.GroupPath}#timePoint", f.RelatedFeatureId);
    }

    [Fact]
    public void R2_1_Does_Not_Fire_When_TimePoints_Strictly_Increase()
    {
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var c3 = MakeCoverage(timePoint: BaseTime.AddHours(2));
        var dataset = MakeDataset(coverages: new[] { c1, c2, c3 });

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-2.1");
    }

    [Fact]
    public void R2_1_Emits_Only_One_Finding_For_Multiple_Violations()
    {
        // Three back-to-back violations; only the first should be reported.
        var c1 = MakeCoverage(timePoint: BaseTime.AddHours(5));
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(3));
        var c3 = MakeCoverage(timePoint: BaseTime.AddHours(2));
        var c4 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var dataset = MakeDataset(coverages: new[] { c1, c2, c3, c4 });

        var report = S104DatasetRules.Default.Run(dataset);

        var r21 = report.Findings.Where(x => x.RuleId == "S104-R-2.1").ToList();
        Assert.Single(r21);
    }

    // ----- R-2.2 -----

    [Fact]
    public void R2_2_Fires_When_Cadence_Outside_Tolerance()
    {
        // Median delta = 1h. Inject a 2h gap → 200% of median → outside ±10%.
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var c3 = MakeCoverage(timePoint: BaseTime.AddHours(2));
        var c4 = MakeCoverage(timePoint: BaseTime.AddHours(4), groupPath: "/WaterLevel/WaterLevel.04"); // 2h gap
        var c5 = MakeCoverage(timePoint: BaseTime.AddHours(5));
        var dataset = MakeDataset(coverages: new[] { c1, c2, c3, c4, c5 });

        var report = S104DatasetRules.Default.Run(dataset);

        var r22 = report.Findings.Where(x => x.RuleId == "S104-R-2.2").ToList();
        Assert.Single(r22);
        Assert.Equal(ValidationSeverity.Warning, r22[0].Severity);
        Assert.Equal($"{c4.GroupPath}#timePoint", r22[0].RelatedFeatureId);
    }

    [Fact]
    public void R2_2_Does_Not_Fire_For_Uniform_Cadence()
    {
        var coverages = Enumerable.Range(0, 5)
            .Select(i => MakeCoverage(timePoint: BaseTime.AddHours(i)))
            .ToArray();
        var dataset = MakeDataset(coverages: coverages);

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-2.2");
    }

    [Fact]
    public void R2_2_Is_Skipped_When_Coverages_Count_Less_Than_3()
    {
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(100)); // huge gap, no comparison possible
        var dataset = MakeDataset(coverages: new[] { c1, c2 });

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-2.2");
    }

    // ----- R-3.1 -----

    [Fact]
    public void R3_1_Fires_When_Method_Missing_On_TimeSeries()
    {
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var dataset = MakeDataset(method: null, coverages: new[] { c1, c2 });

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-3.1");
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
    }

    [Fact]
    public void R3_1_Does_Not_Fire_When_Method_Set_On_TimeSeries()
    {
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var dataset = MakeDataset(method: "TidalForecast", coverages: new[] { c1, c2 });

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-3.1");
    }

    [Fact]
    public void R3_1_Does_Not_Fire_For_Single_Coverage_Without_Method()
    {
        var dataset = MakeDataset(method: null, coverages: MakeCoverage());

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-3.1");
    }

    // ----- R-4.1 -----

    [Fact]
    public void R4_1_Fires_On_Implausible_Height_Values()
    {
        var values = FillHeights(4, 1.5f);
        values[1] = new WaterLevelValue(50f, 3); // > 15 m
        values[2] = new WaterLevelValue(-20f, 3); // < -15 m
        var coverage = MakeCoverage(values: values);
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-4.1");
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
        // One finding per coverage (count in message), not per cell.
        Assert.Contains("2 non-NODATA", f.Message);
    }

    [Fact]
    public void R4_1_Does_Not_Fire_For_In_Range_Values()
    {
        var values = FillHeights(4, 1.5f);
        values[0] = new WaterLevelValue(-15f, 3); // boundary inclusive
        values[1] = new WaterLevelValue(15f, 3);
        var coverage = MakeCoverage(values: values);
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-4.1");
    }

    [Fact]
    public void R4_1_Treats_Minus_9999_As_NoData()
    {
        var values = FillHeights(4, 1.5f);
        values[1] = new WaterLevelValue(NoData, 0); // NODATA sentinel — must be skipped
        values[2] = new WaterLevelValue(NoData, 0);
        var coverage = MakeCoverage(values: values);
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-4.1");
    }

    [Fact]
    public void R4_1_Skips_NaN_Values()
    {
        var values = FillHeights(4, 1.5f);
        values[1] = new WaterLevelValue(float.NaN, 0);
        values[2] = new WaterLevelValue(float.PositiveInfinity, 0);
        var coverage = MakeCoverage(values: values);
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);
        // NaN / infinity are skipped (not flagged as out of plausible range).
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-4.1");
    }

    // ----- R-4.2 -----

    [Fact]
    public void R4_2_Fires_On_Out_Of_Range_Origin()
    {
        var coverage = MakeCoverage(originLat: 100.0, originLon: 200.0);
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-4.2");
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal(coverage.GroupPath, f.RelatedFeatureId);
        Assert.NotNull(f.BoundingBox);
    }

    [Fact]
    public void R4_2_Fires_On_Extent_Wrapping_Antimeridian()
    {
        // origin lon 179, spacing 1, 5 points → end = 183 (wraps)
        var coverage = MakeCoverage(
            originLon: 179.0,
            spacingLon: 1.0,
            cols: 5,
            values: FillHeights(10, 1.5f),
            rows: 2);
        var dataset = MakeDataset(coverages: coverage);

        var report = S104DatasetRules.Default.Run(dataset);

        var f = Assert.Single(report.Findings, x => x.RuleId == "S104-R-4.2");
        Assert.Contains("longitude end", f.Message);
    }

    [Fact]
    public void R4_2_Does_Not_Fire_For_In_Range_Georeferencing()
    {
        var dataset = MakeDataset(coverages: MakeCoverage());

        var report = S104DatasetRules.Default.Run(dataset);
        Assert.DoesNotContain(report.Findings, x => x.RuleId == "S104-R-4.2");
    }

    // ----- Multi-coverage RelatedFeatureId reflects GroupPath -----

    [Fact]
    public void Per_Coverage_Findings_Use_GroupPath_As_RelatedFeatureId()
    {
        var c1 = MakeCoverage(
            rows: 2, cols: 3,
            values: FillHeights(5, 1.5f), // shape mismatch — R-1.1 fires
            timePoint: BaseTime,
            groupPath: "/WaterLevel/WaterLevel.01");
        var c2 = MakeCoverage(
            rows: 2, cols: 3,
            values: FillHeights(7, 1.5f), // shape mismatch — R-1.1 fires
            timePoint: BaseTime.AddHours(1),
            groupPath: "/WaterLevel/WaterLevel.02");
        var dataset = MakeDataset(coverages: new[] { c1, c2 });

        var report = S104DatasetRules.Default.Run(dataset);

        var r11 = report.Findings.Where(x => x.RuleId == "S104-R-1.1").ToList();
        Assert.Equal(2, r11.Count);
        Assert.Contains(r11, f => f.RelatedFeatureId == "/WaterLevel/WaterLevel.01");
        Assert.Contains(r11, f => f.RelatedFeatureId == "/WaterLevel/WaterLevel.02");
    }
}
