using EncDotNet.S100.PerfReport;

namespace EncDotNet.S100.PerfReport.Tests;

public class StatisticsTests
{
    [Fact]
    public void Percentile_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, Statistics.Percentile(Array.Empty<double>(), 0.5));
    }

    [Fact]
    public void Percentile_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(42.0, Statistics.Percentile(new[] { 42.0 }, 0.5));
        Assert.Equal(42.0, Statistics.Percentile(new[] { 42.0 }, 0.0));
        Assert.Equal(42.0, Statistics.Percentile(new[] { 42.0 }, 1.0));
    }

    [Fact]
    public void Percentile_FivePoints_LinearlyInterpolatesP50()
    {
        var sorted = new[] { 10.0, 20, 30, 40, 50 };
        Assert.Equal(30, Statistics.Percentile(sorted, 0.5));
    }

    [Fact]
    public void Median_EvenCount_AveragesMiddleTwo()
    {
        var values = new[] { 1.0, 3, 5, 7 };
        Assert.Equal(4, Statistics.Median(values));
    }

    [Fact]
    public void Median_OddCount_ReturnsMiddle()
    {
        var values = new[] { 5.0, 1, 3 };
        Assert.Equal(3, Statistics.Median(values));
    }

    [Fact]
    public void MedianAbsoluteDeviation_ConstantValues_ReturnsZero()
    {
        var values = new[] { 100.0, 100, 100, 100, 100 };
        var med = Statistics.Median(values);
        Assert.Equal(0, Statistics.MedianAbsoluteDeviation(values, med));
    }

    [Fact]
    public void MedianAbsoluteDeviation_SymmetricSpread_ReturnsExpected()
    {
        // values: median=10, deviations={2,1,0,1,2}, MAD=1
        var values = new[] { 8.0, 9, 10, 11, 12 };
        var med = Statistics.Median(values);
        Assert.Equal(10, med);
        Assert.Equal(1, Statistics.MedianAbsoluteDeviation(values, med));
    }

    [Fact]
    public void MedianAbsoluteDeviation_IsRobustToOutliers()
    {
        // Single huge outlier should NOT swing MAD the way it would
        // swing standard deviation. Median is 10, 4 of 5 deviations
        // are 0/1/1/0, the outlier produces 990, but median deviation
        // is still 1.
        var values = new[] { 9.0, 10, 10, 11, 1000 };
        var med = Statistics.Median(values);
        Assert.Equal(10, med);
        Assert.Equal(1, Statistics.MedianAbsoluteDeviation(values, med));
    }

    [Fact]
    public void MedianAbsoluteDeviation_ScaleToSigma_AppliesConstant()
    {
        var values = new[] { 8.0, 9, 10, 11, 12 };
        var med = Statistics.Median(values);
        var sigma = Statistics.MedianAbsoluteDeviation(values, med, scaleToSigma: true);
        Assert.Equal(1.4826, sigma, precision: 4);
    }
}
