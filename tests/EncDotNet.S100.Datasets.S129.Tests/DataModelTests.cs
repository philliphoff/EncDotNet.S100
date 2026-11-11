using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S129.DataModel;

namespace EncDotNet.S100.Datasets.S129.Tests;

/// <summary>
/// Tests for the strongly-typed projection
/// <see cref="S129UnderKeelClearancePlan"/>.
/// </summary>
public class DataModelTests
{
    private const string TestDataDir = "TestData";

    private static S129Dataset LoadFixture(string name) =>
        S129Dataset.Open(Path.Combine(TestDataDir, name));

    private static S129UnderKeelClearancePlan Project(
        string name,
        out IReadOnlyList<ProjectionDiagnostic> diagnostics) =>
        S129UnderKeelClearancePlan.From(LoadFixture(name), out diagnostics);

    private static S129Dataset OpenGml(string gml)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        return S129Dataset.Open(stream);
    }

    // ── Real-fixture coverage ────────────────────────────────────────

    [Theory]
    [InlineData("12900MCTDS130TS.gml")]
    [InlineData("12900MCTDS200TS.gml")]
    public void From_RealFixture_PopulatesPlanAndPlanArea(string fileName)
    {
        var typed = Project(fileName, out var diagnostics);

        Assert.Equal("S-129", typed.ProductIdentifier);
        Assert.NotNull(typed.Plan);
        Assert.NotNull(typed.PlanArea);
        Assert.Equal(S129GeometryKind.Surface, typed.PlanArea!.GeometryKind);
        Assert.NotEmpty(typed.PlanArea.Coordinates);

        // No parse errors expected against the bundled fixtures.
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void From_TorresStraitFixture_ParsesPlanMetadata()
    {
        var typed = Project("12900MCTDS130TS.gml", out _);
        var plan = typed.Plan!;

        Assert.Equal("TEST_PLAN_TORRES_STRAIT", plan.Id);
        Assert.Equal("9800738", plan.VesselId);
        Assert.Equal(12.2, plan.MaximumDraught);
        Assert.NotNull(plan.SourceRoute);
        Assert.Equal("S-421 route", plan.SourceRoute!.Kind);
        Assert.Equal("Test Route Name", plan.SourceRoute.Identifier);
        Assert.Equal("1152192", plan.SourceRoute.Version);

        Assert.NotNull(plan.FixedTimeRange);
        Assert.Equal(
            new DateTimeOffset(2024, 4, 17, 21, 41, 0, TimeSpan.Zero),
            plan.FixedTimeRange!.Start);
        Assert.Equal(
            new DateTimeOffset(2024, 4, 18, 1, 13, 0, TimeSpan.Zero),
            plan.FixedTimeRange.End);
    }

    [Fact]
    public void From_TorresStraitFixture_ControlPointsCarryUkcMeasurements()
    {
        var typed = Project("12900MCTDS130TS.gml", out _);

        Assert.NotEmpty(typed.ControlPoints);
        var cp01 = typed.ControlPoints.Single(cp => cp.Id == "CP_01");
        Assert.Equal(0.113, cp01.DistanceAboveUkcLimit);
        Assert.Equal(6.0, cp01.ExpectedPassingSpeed);
        Assert.Equal(
            new DateTimeOffset(2024, 4, 17, 22, 0, 0, TimeSpan.Zero),
            cp01.ExpectedPassingTime);
        Assert.NotNull(cp01.Position);
        Assert.InRange(cp01.Position!.Value.Latitude, -10.6, -10.4);
        Assert.InRange(cp01.Position.Value.Longitude, 142.3, 142.4);

        Assert.NotNull(cp01.FeatureName);
        Assert.Equal("en", cp01.FeatureName!.Language);
        Assert.Equal("CP01", cp01.FeatureName.Name);
    }

    [Fact]
    public void From_TorresStraitFixture_ControlPointsOrderedByPassingTime()
    {
        // S-129 skill checklist #2: preserve the temporal ordering of UKC
        // values. The fixture lists CPs in id order, which also happens
        // to be passing-time order — verify the projection holds the
        // invariant regardless.
        var typed = Project("12900MCTDS130TS.gml", out _);

        var times = typed.ControlPoints
            .Select(cp => cp.ExpectedPassingTime)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        Assert.NotEmpty(times);
        for (var i = 1; i < times.Count; i++)
            Assert.True(times[i] >= times[i - 1],
                $"Control point {i} expectedPassingTime regressed: {times[i - 1]} → {times[i]}");
    }

    [Fact]
    public void From_TorresStraitFixture_NonNavigableAreasParsed()
    {
        var typed = Project("12900MCTDS130TS.gml", out _);
        Assert.NotEmpty(typed.NonNavigableAreas);

        foreach (var area in typed.NonNavigableAreas)
        {
            Assert.Equal(S129GeometryKind.Surface, area.GeometryKind);
            Assert.NotEmpty(area.Coordinates);
            Assert.Equal(1, area.ScaleMinimum);
        }
    }

    [Fact]
    public void From_PreservesRawDatasetOnSource()
    {
        var dataset = LoadFixture("12900MCTDS130TS.gml");
        var typed = S129UnderKeelClearancePlan.From(dataset, out _);
        Assert.Same(dataset, typed.Source);
    }

    // ── Synthetic-fixture edge cases ────────────────────────────────

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        var gml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dataset xmlns:gml="http://www.opengis.net/gml/3.2"
                 xmlns:S100="http://www.iho.int/s100gml/5.0"
                 xmlns="http://www.iho.int/S129/2.0"
                 gml:id="EMPTY">
          <members />
        </Dataset>
        """;
        var dataset = OpenGml(gml);
        Assert.Throws<InvalidOperationException>(
            () => S129UnderKeelClearancePlan.From(dataset, out _));
    }

    [Fact]
    public void From_DuplicatePlan_ReportsDiagnostic()
    {
        var gml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dataset xmlns:gml="http://www.opengis.net/gml/3.2"
                 xmlns:S100="http://www.iho.int/s100gml/5.0"
                 xmlns="http://www.iho.int/S129/2.0"
                 gml:id="DUPE">
          <members>
            <UnderKeelClearancePlan gml:id="PLAN_A">
              <vesselID>1</vesselID>
            </UnderKeelClearancePlan>
            <UnderKeelClearancePlan gml:id="PLAN_B">
              <vesselID>2</vesselID>
            </UnderKeelClearancePlan>
          </members>
        </Dataset>
        """;
        var typed = S129UnderKeelClearancePlan.From(OpenGml(gml), out var diagnostics);

        Assert.Equal("PLAN_A", typed.Plan!.Id);
        Assert.Contains(
            diagnostics,
            d => d.Code == "feature.duplicate" && d.RelatedId == "PLAN_B");
    }

    [Fact]
    public void From_UnresolvedXlink_ReportsDiagnostic()
    {
        // A child element with an xlink:href to a non-existent target is
        // captured by the reader as an S129Reference and surfaced by the
        // typed projection through XlinkResolver.
        var gml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dataset xmlns:gml="http://www.opengis.net/gml/3.2"
                 xmlns:xlink="http://www.w3.org/1999/xlink"
                 xmlns:S100="http://www.iho.int/s100gml/5.0"
                 xmlns="http://www.iho.int/S129/2.0"
                 gml:id="XLINK">
          <members>
            <UnderKeelClearancePlan gml:id="PLAN">
              <vesselID>1</vesselID>
              <sourceRoute xlink:href="#MISSING" />
            </UnderKeelClearancePlan>
          </members>
        </Dataset>
        """;
        var typed = S129UnderKeelClearancePlan.From(OpenGml(gml), out var diagnostics);

        Assert.NotNull(typed.Plan);
        Assert.Contains(
            diagnostics,
            d => d.Code == "xlink.unresolved" && d.RelatedId == "PLAN");
    }

    [Fact]
    public void From_AttributeParseFailure_ReportsDiagnostic()
    {
        var gml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dataset xmlns:gml="http://www.opengis.net/gml/3.2"
                 xmlns:S100="http://www.iho.int/s100gml/5.0"
                 xmlns="http://www.iho.int/S129/2.0"
                 gml:id="BAD">
          <members>
            <UnderKeelClearancePlan gml:id="PLAN">
              <maximumDraught>not-a-number</maximumDraught>
            </UnderKeelClearancePlan>
          </members>
        </Dataset>
        """;
        var typed = S129UnderKeelClearancePlan.From(OpenGml(gml), out var diagnostics);

        Assert.Null(typed.Plan!.MaximumDraught);
        Assert.Contains(
            diagnostics,
            d => d.Code == "attribute.parse.double"
                && d.RelatedAttribute == "maximumDraught");
    }

    [Fact]
    public void From_ExtraAttributes_PreservesUnmodelledKeys()
    {
        // A producer extension attribute not consumed by the typed model
        // round-trips through ExtraAttributes for forward compatibility.
        var gml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dataset xmlns:gml="http://www.opengis.net/gml/3.2"
                 xmlns:S100="http://www.iho.int/s100gml/5.0"
                 xmlns="http://www.iho.int/S129/2.0"
                 gml:id="EXTRA">
          <members>
            <UnderKeelClearancePlan gml:id="PLAN">
              <vesselID>1234</vesselID>
              <producerExtensionFooBar>baz</producerExtensionFooBar>
            </UnderKeelClearancePlan>
          </members>
        </Dataset>
        """;
        var typed = S129UnderKeelClearancePlan.From(OpenGml(gml), out _);

        Assert.True(
            typed.Plan!.ExtraAttributes.TryGetValue("producerExtensionFooBar", out var v));
        Assert.Equal("baz", v);
        // VesselId is modelled; it should not also appear in ExtraAttributes.
        Assert.False(typed.Plan.ExtraAttributes.ContainsKey("vesselID"));
    }
}
