using System;
using System.Linq;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S111.Validation;
using EncDotNet.S100.Validation;
using Xunit;

namespace EncDotNet.S100.Datasets.S111.Tests.Validation;

/// <summary>
/// Synthetic-fixture unit tests for the V-3 S-111 surface-current
/// validation rule pack (<see cref="S111SurfaceCurrentRules"/>).
/// Mirrors the structure of <c>S104ValidationTests</c> from V-2 PR #135.
/// </summary>
public class S111ValidationTests
{
    private const float NoData = -9999.0f;
    private const float KnotsToMps = 0.5144444f;

    private static readonly DateTime BaseTime =
        new(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);

    private static SurfaceCurrentCoverage MakeCoverage(
        int rows = 2,
        int cols = 2,
        double originLat = 50.0,
        double originLon = -1.0,
        double spacingLat = 0.01,
        double spacingLon = 0.01,
        DateTime? timePoint = null,
        SurfaceCurrentValue[]? values = null,
        string? groupPath = "/SurfaceCurrent/SurfaceCurrent.01")
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
            Values = values ?? FillValues(rows * cols, speedKnots: 2.0f, direction: 90.0f),
        };

    private static SurfaceCurrentValue[] FillValues(int count, float speedKnots, float direction)
    {
        var arr = new SurfaceCurrentValue[count];
        for (var i = 0; i < count; i++)
            arr[i] = new SurfaceCurrentValue(speedKnots, direction);
        return arr;
    }

    private static S111Dataset MakeDataset(
        int dcf = 2,
        float? depth = 2.0f,
        int? typeOfCurrentData = 6,
        params SurfaceCurrentCoverage[] coverages)
        => new()
        {
            HorizontalCRS = 4326,
            IssueDate = "2026-05-28T00:00:00Z",
            DataCodingFormat = dcf,
            SurfaceCurrentDepth = depth,
            TypeOfCurrentData = typeOfCurrentData,
            Coverages = coverages.Length == 0
                ? new[] { MakeCoverage() }
                : coverages,
        };

    // ----- Default rule set / clean dataset -----

    [Fact]
    public void Default_Run_On_Clean_Dataset_Is_Valid()
    {
        var dataset = new S111Dataset
        {
            HorizontalCRS = 4326,
            IssueDate = "2026-05-28T00:00:00Z",
            DataCodingFormat = 2,
            SurfaceCurrentDepth = 2.0f,
            TypeOfCurrentData = 6,
            Coverages = new[] { MakeCoverage() },
        };

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.True(report.IsValid, $"Expected clean dataset to be valid; got: {string.Join("; ", report.Findings.Select(f => f.Message))}");
        Assert.Equal(8, report.RulesEvaluated); // 6 normative + 2 PROJ surrogates
        Assert.Equal(0, report.RulesWithFindings);
        Assert.Empty(report.Findings);
    }

    // ----- R-1.1: Values length matches shape -----

    [Fact]
    public void R1_1_Fires_When_Values_Length_Mismatches_Shape()
    {
        var coverage = MakeCoverage(rows: 2, cols: 3, values: FillValues(5, 1.0f, 45.0f));
        var dataset = MakeDataset(coverages: coverage);

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r11 = report.Findings.Where(f => f.RuleId == "S111-R-1.1").ToList();
        Assert.Single(r11);
        Assert.Equal(ValidationSeverity.Error, r11[0].Severity);
        Assert.Equal("/SurfaceCurrent/SurfaceCurrent.01", r11[0].RelatedFeatureId);
        Assert.Contains("expected", r11[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void R1_1_Does_Not_Fire_When_Shape_Matches()
    {
        var coverage = MakeCoverage(rows: 2, cols: 3, values: FillValues(6, 1.0f, 45.0f));
        var dataset = MakeDataset(coverages: coverage);

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-1.1");
    }

    // ----- R-2.1: TimePoint monotonicity + cadence (folded) -----

    [Fact]
    public void R2_1_Fires_On_Non_Increasing_TimePoints()
    {
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime); // equal — not strictly increasing
        var dataset = MakeDataset(coverages: new[] { c1, c2 });

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r21 = report.Findings.Where(f => f.RuleId == "S111-R-2.1").ToList();
        Assert.Single(r21);
        Assert.Equal(ValidationSeverity.Warning, r21[0].Severity);
        Assert.EndsWith("#timePoint", r21[0].RelatedFeatureId);
    }

    [Fact]
    public void R2_1_Does_Not_Fire_When_TimePoints_Strictly_Increase_With_Steady_Cadence()
    {
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var c3 = MakeCoverage(timePoint: BaseTime.AddHours(2));
        var dataset = MakeDataset(coverages: new[] { c1, c2, c3 });

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-2.1");
    }

    [Fact]
    public void R2_1_Emits_Only_One_Finding_On_Multiple_Monotonicity_Violations()
    {
        // Descending sequence — every step violates; rule should early-return after first.
        var c1 = MakeCoverage(timePoint: BaseTime.AddHours(5));
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(3));
        var c3 = MakeCoverage(timePoint: BaseTime.AddHours(2));
        var c4 = MakeCoverage(timePoint: BaseTime.AddHours(1));
        var dataset = MakeDataset(coverages: new[] { c1, c2, c3, c4 });

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r21 = report.Findings.Where(f => f.RuleId == "S111-R-2.1").ToList();
        Assert.Single(r21);
        Assert.Contains("index 1", r21[0].Message);
    }

    [Fact]
    public void R2_1_Fires_On_Cadence_Outside_Tolerance()
    {
        // Three coverages with a 5x cadence outlier — well beyond ±10%.
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddMinutes(60));
        var c3 = MakeCoverage(timePoint: BaseTime.AddMinutes(60 + 60));
        var c4 = MakeCoverage(timePoint: BaseTime.AddMinutes(60 + 60 + 300)); // 5-hour gap
        var dataset = MakeDataset(coverages: new[] { c1, c2, c3, c4 });

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r21 = report.Findings.Where(f => f.RuleId == "S111-R-2.1").ToList();
        Assert.NotEmpty(r21);
        Assert.All(r21, f => Assert.Equal(ValidationSeverity.Warning, f.Severity));
        Assert.Contains(r21, f => f.Message.Contains("cadence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void R2_1_Skips_Cadence_When_Fewer_Than_Three_Coverages()
    {
        // Two coverages → one delta → cadence cannot be assessed; rule must not fire.
        var c1 = MakeCoverage(timePoint: BaseTime);
        var c2 = MakeCoverage(timePoint: BaseTime.AddHours(99));
        var dataset = MakeDataset(coverages: new[] { c1, c2 });

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-2.1");
    }

    // ----- R-3.1: SurfaceCurrentDepth in [0, 1500] when present -----

    [Fact]
    public void R3_1_Fires_When_Depth_Is_Negative()
    {
        var dataset = MakeDataset(depth: -1.0f, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r31 = report.Findings.Where(f => f.RuleId == "S111-R-3.1").ToList();
        Assert.Single(r31);
        Assert.Equal(ValidationSeverity.Warning, r31[0].Severity);
    }

    [Fact]
    public void R3_1_Fires_When_Depth_Exceeds_1500m()
    {
        var dataset = MakeDataset(depth: 2000.0f, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.Single(report.Findings, f => f.RuleId == "S111-R-3.1");
    }

    [Fact]
    public void R3_1_Does_Not_Fire_When_Depth_Is_Null()
    {
        var dataset = MakeDataset(depth: null, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-3.1");
    }

    [Fact]
    public void R3_1_Does_Not_Fire_When_Depth_Is_In_Range()
    {
        var dataset = MakeDataset(depth: 750.0f, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-3.1");
    }

    // ----- R-3.2: TypeOfCurrentData in {1..6} when present -----

    [Fact]
    public void R3_2_Fires_When_TypeOfCurrentData_Out_Of_Set()
    {
        var dataset = MakeDataset(typeOfCurrentData: 99, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r32 = report.Findings.Where(f => f.RuleId == "S111-R-3.2").ToList();
        Assert.Single(r32);
        Assert.Equal(ValidationSeverity.Warning, r32[0].Severity);
    }

    [Fact]
    public void R3_2_Does_Not_Fire_When_TypeOfCurrentData_Is_Null()
    {
        var dataset = MakeDataset(typeOfCurrentData: null, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-3.2");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void R3_2_Does_Not_Fire_For_Each_Enumerated_Value(int value)
    {
        var dataset = MakeDataset(typeOfCurrentData: value, coverages: MakeCoverage());

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-3.2");
    }

    // ----- R-4.1: Speed in [0, 15] m/s after knots→m/s conversion -----

    [Fact]
    public void R4_1_Fires_When_Speed_Exceeds_15_Mps()
    {
        // 40 knots ≈ 20.58 m/s — exceeds 15 m/s cap.
        var bad = new[]
        {
            new SurfaceCurrentValue(2.0f, 90.0f),
            new SurfaceCurrentValue(40.0f, 90.0f),
            new SurfaceCurrentValue(2.0f, 90.0f),
            new SurfaceCurrentValue(2.0f, 90.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: bad));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r41 = report.Findings.Where(f => f.RuleId == "S111-R-4.1").ToList();
        Assert.Single(r41);
        Assert.Equal(ValidationSeverity.Warning, r41[0].Severity);
        Assert.Equal("/SurfaceCurrent/SurfaceCurrent.01", r41[0].RelatedFeatureId);
    }

    [Fact]
    public void R4_1_Fires_When_Speed_Is_Negative()
    {
        var bad = new[]
        {
            new SurfaceCurrentValue(2.0f, 90.0f),
            new SurfaceCurrentValue(-1.0f, 90.0f),
            new SurfaceCurrentValue(2.0f, 90.0f),
            new SurfaceCurrentValue(2.0f, 90.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: bad));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.Single(report.Findings, f => f.RuleId == "S111-R-4.1");
    }

    [Fact]
    public void R4_1_Skips_NoData_NaN_And_Infinity_Speeds()
    {
        // All four cells are sentinels — should be excluded from the range check.
        var values = new[]
        {
            new SurfaceCurrentValue(NoData, NoData),
            new SurfaceCurrentValue(float.NaN, 90.0f),
            new SurfaceCurrentValue(float.PositiveInfinity, 90.0f),
            new SurfaceCurrentValue(float.NegativeInfinity, 90.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: values));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-4.1");
    }

    [Fact]
    public void R4_1_Does_Not_Fire_For_Speeds_Just_Below_15_Mps()
    {
        // 14.9 m/s / 0.5144444 ≈ 28.96 knots — at the very edge but inside.
        const float justBelow = 14.9f / KnotsToMps;
        var values = new[]
        {
            new SurfaceCurrentValue(justBelow, 90.0f),
            new SurfaceCurrentValue(justBelow, 90.0f),
            new SurfaceCurrentValue(justBelow, 90.0f),
            new SurfaceCurrentValue(justBelow, 90.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: values));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-4.1");
    }

    // ----- R-4.2: Direction in [0, 360) (half-open) -----

    [Fact]
    public void R4_2_Fires_When_Direction_Equals_360()
    {
        // 360.0 is the spec wrap-around boundary and must be reported as invalid.
        var values = new[]
        {
            new SurfaceCurrentValue(1.0f, 0.0f),
            new SurfaceCurrentValue(1.0f, 360.0f),
            new SurfaceCurrentValue(1.0f, 180.0f),
            new SurfaceCurrentValue(1.0f, 270.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: values));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r42 = report.Findings.Where(f => f.RuleId == "S111-R-4.2").ToList();
        Assert.Single(r42);
        Assert.Equal(ValidationSeverity.Error, r42[0].Severity);
        Assert.Equal("/SurfaceCurrent/SurfaceCurrent.01", r42[0].RelatedFeatureId);
    }

    [Fact]
    public void R4_2_Fires_When_Direction_Is_Negative()
    {
        var values = new[]
        {
            new SurfaceCurrentValue(1.0f, -10.0f),
            new SurfaceCurrentValue(1.0f, 90.0f),
            new SurfaceCurrentValue(1.0f, 180.0f),
            new SurfaceCurrentValue(1.0f, 270.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: values));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.Single(report.Findings, f => f.RuleId == "S111-R-4.2");
    }

    [Fact]
    public void R4_2_Does_Not_Fire_For_Boundaries_0_And_Almost_360()
    {
        var values = new[]
        {
            new SurfaceCurrentValue(1.0f, 0.0f),
            new SurfaceCurrentValue(1.0f, 359.999f),
            new SurfaceCurrentValue(1.0f, 90.0f),
            new SurfaceCurrentValue(1.0f, 180.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: values));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-4.2");
    }

    [Fact]
    public void R4_2_Skips_NoData_NaN_And_Infinity_Directions()
    {
        var values = new[]
        {
            new SurfaceCurrentValue(1.0f, NoData),
            new SurfaceCurrentValue(1.0f, float.NaN),
            new SurfaceCurrentValue(1.0f, float.PositiveInfinity),
            new SurfaceCurrentValue(1.0f, 90.0f),
        };
        var dataset = MakeDataset(coverages: MakeCoverage(values: values));

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S111-R-4.2");
    }

    // ----- GroupPath propagation across multiple coverages -----

    [Fact]
    public void Findings_Carry_Per_Coverage_GroupPath_On_Multi_Coverage_Dataset()
    {
        // Two distinct coverages, each broken in a different way to trigger
        // a per-coverage finding; verify each finding's RelatedFeatureId is
        // the offending coverage's GroupPath rather than a shared fallback.
        var bad1 = MakeCoverage(
            rows: 2, cols: 2,
            timePoint: BaseTime,
            values: FillValues(4, 200.0f /* ~102.9 m/s — out of range */, 90.0f),
            groupPath: "/SurfaceCurrent/SurfaceCurrent.01");
        var bad2 = MakeCoverage(
            rows: 2, cols: 2,
            timePoint: BaseTime.AddHours(1),
            values: new[]
            {
                new SurfaceCurrentValue(1.0f, 400.0f), // out-of-range direction
                new SurfaceCurrentValue(1.0f, 90.0f),
                new SurfaceCurrentValue(1.0f, 90.0f),
                new SurfaceCurrentValue(1.0f, 90.0f),
            },
            groupPath: "/SurfaceCurrent/SurfaceCurrent.02");
        var dataset = MakeDataset(coverages: new[] { bad1, bad2 });

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r41 = Assert.Single(report.Findings, f => f.RuleId == "S111-R-4.1");
        Assert.Equal("/SurfaceCurrent/SurfaceCurrent.01", r41.RelatedFeatureId);

        var r42 = Assert.Single(report.Findings, f => f.RuleId == "S111-R-4.2");
        Assert.Equal("/SurfaceCurrent/SurfaceCurrent.02", r42.RelatedFeatureId);
    }

    [Fact]
    public void Findings_Fall_Back_To_SurfaceCurrent_When_GroupPath_Is_Null()
    {
        var coverage = MakeCoverage(
            rows: 2, cols: 3,
            values: FillValues(5, 1.0f, 90.0f),
            groupPath: null);
        var dataset = MakeDataset(coverages: coverage);

        var report = S111SurfaceCurrentRules.Default.Run(dataset);

        var r11 = Assert.Single(report.Findings, f => f.RuleId == "S111-R-1.1");
        Assert.Equal("/SurfaceCurrent", r11.RelatedFeatureId);
    }

    // ----- Validate() helper -----

    [Fact]
    public void Validate_Helper_Throws_On_Null_Dataset()
    {
        Assert.Throws<ArgumentNullException>(() => S111SurfaceCurrentRules.Validate(null!));
    }
}
