using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S129.Fusion.Tests.Helpers;

namespace EncDotNet.S100.Datasets.S129.Fusion.Tests;

public class WaterLevelFusionTests
{
    [Fact]
    public void Sample_AtT0AndSentinelCell_PicksT0Slice()
    {
        var wl = SyntheticDatasets.MakeWaterLevel();
        var coverage = new S104CoverageSource(wl);

        var sample = S129WaterLevelFusion.Sample(
            coverage,
            new GeoPosition(50.005, 5.005),
            SyntheticDatasets.T0);

        Assert.NotNull(sample);
        Assert.Equal(1.5f, sample!.Height);
        Assert.Equal(SyntheticDatasets.T0.UtcDateTime, sample.TimeSelected);
    }

    [Fact]
    public void Sample_AtT0PlusOneHour_PicksLaterSlice()
    {
        var wl = SyntheticDatasets.MakeWaterLevel();
        var coverage = new S104CoverageSource(wl);

        var sample = S129WaterLevelFusion.Sample(
            coverage,
            new GeoPosition(50.005, 5.005),
            SyntheticDatasets.T0.AddHours(1));

        Assert.NotNull(sample);
        Assert.Equal(2.5f, sample!.Height);
    }

    [Fact]
    public void Sample_AtBetweenSlicesTime_PicksNearestTimeSlice()
    {
        var wl = SyntheticDatasets.MakeWaterLevel();
        var coverage = new S104CoverageSource(wl);

        // 50min in: nearest is T0+1h
        var sample = S129WaterLevelFusion.Sample(
            coverage,
            new GeoPosition(50.005, 5.005),
            SyntheticDatasets.T0.AddMinutes(50));

        Assert.NotNull(sample);
        Assert.Equal(SyntheticDatasets.T0.AddHours(1).UtcDateTime, sample!.TimeSelected);
    }

    [Fact]
    public void Sample_OutOfExtent_ReturnsNull()
    {
        var wl = SyntheticDatasets.MakeWaterLevel();
        var coverage = new S104CoverageSource(wl);

        var sample = S129WaterLevelFusion.Sample(
            coverage,
            new GeoPosition(51.0, 6.0),
            SyntheticDatasets.T0);

        Assert.Null(sample);
    }
}
