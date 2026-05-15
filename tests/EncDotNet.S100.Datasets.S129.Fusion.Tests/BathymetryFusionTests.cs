using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S129.Fusion.Tests.Helpers;

namespace EncDotNet.S100.Datasets.S129.Fusion.Tests;

public class BathymetryFusionTests
{
    [Fact]
    public void Sample_AtSentinelCell_ReturnsDistinctValue()
    {
        var bathy = SyntheticDatasets.MakeBathymetry();
        var coverage = new S102CoverageSource(bathy);

        // Cell (5,5) — origin (50.0, 5.0) + 5 * 0.001 in each axis.
        var sample = S129BathymetryFusion.Sample(coverage, new GeoPosition(50.005, 5.005));

        Assert.NotNull(sample);
        Assert.Equal(20f, sample!.Depth);
        Assert.Equal(0.5f, sample.Uncertainty);
        Assert.Equal(5, sample.Row);
        Assert.Equal(5, sample.Column);
    }

    [Fact]
    public void Sample_AtNonSentinelCell_ReturnsBaseValue()
    {
        var bathy = SyntheticDatasets.MakeBathymetry();
        var coverage = new S102CoverageSource(bathy);

        var sample = S129BathymetryFusion.Sample(coverage, new GeoPosition(50.001, 5.001));

        Assert.NotNull(sample);
        Assert.Equal(10f, sample!.Depth);
    }

    [Fact]
    public void Sample_OutOfExtent_ReturnsNull()
    {
        var bathy = SyntheticDatasets.MakeBathymetry();
        var coverage = new S102CoverageSource(bathy);

        Assert.Null(S129BathymetryFusion.Sample(coverage, new GeoPosition(51.0, 6.0)));
        Assert.Null(S129BathymetryFusion.Sample(coverage, new GeoPosition(49.99, 4.99)));
    }
}
