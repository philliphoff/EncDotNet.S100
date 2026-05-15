using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S129.Tests;

/// <summary>
/// Smoke tests for <see cref="S129Dataset"/> over the two bundled
/// real-world S-129 fixtures.
/// </summary>
public class S129DatasetReaderTests
{
    private const string TestDataDir = "TestData";

    [Theory]
    [InlineData("12900MCTDS130TS.gml")]
    [InlineData("12900MCTDS200TS.gml")]
    public void Open_RealFixture_ParsesProductAndFeatures(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        var dataset = S129Dataset.Open(path);

        Assert.Equal("S-129", dataset.ProductIdentifier);
        Assert.False(dataset.Features.IsDefaultOrEmpty);

        // The two bundled fixtures both carry exactly one plan and one
        // plan-area feature plus many areas and control points.
        Assert.Single(dataset.Features.Where(f =>
            string.Equals(f.FeatureType, "UnderKeelClearancePlan", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(dataset.Features.Where(f =>
            string.Equals(f.FeatureType, "UnderKeelClearancePlanArea", StringComparison.OrdinalIgnoreCase)));
        Assert.NotEmpty(dataset.Features.Where(f =>
            string.Equals(f.FeatureType, "UnderKeelClearanceNonNavigableArea", StringComparison.OrdinalIgnoreCase)));
        Assert.NotEmpty(dataset.Features.Where(f =>
            string.Equals(f.FeatureType, "UnderKeelClearanceControlPoint", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Open_RealFixture_ControlPointGeometryParsesLatFirst()
    {
        // S-100 Part 10b §6.2: GML coordinate ordering for EPSG:4326 is
        // (lat, lon). The Torres Strait fixture sits around lat ≈ -10.5,
        // lon ≈ 142.x — verify the reader does not swap them.
        var dataset = S129Dataset.Open(Path.Combine(TestDataDir, "12900MCTDS130TS.gml"));
        var firstCp = dataset.Features
            .First(f => string.Equals(
                f.FeatureType, "UnderKeelClearanceControlPoint", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(GmlGeometryType.Point, firstCp.GeometryType);
        Assert.Single(firstCp.Points);
        var (lat, lon) = firstCp.Points[0];
        Assert.InRange(lat, -11.0, -10.0);
        Assert.InRange(lon, 142.0, 143.0);
    }

    [Fact]
    public void Open_RealFixture_PlanAreaSurfaceRingClosed()
    {
        var dataset = S129Dataset.Open(Path.Combine(TestDataDir, "12900MCTDS130TS.gml"));
        var planArea = dataset.Features.Single(f =>
            string.Equals(f.FeatureType, "UnderKeelClearancePlanArea", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(GmlGeometryType.Surface, planArea.GeometryType);
        Assert.False(planArea.ExteriorRing.IsDefaultOrEmpty);
        Assert.True(planArea.ExteriorRing.Length >= 4);
        // Ring closure: first ≡ last coordinate.
        Assert.Equal(planArea.ExteriorRing[0], planArea.ExteriorRing[^1]);
    }
}
