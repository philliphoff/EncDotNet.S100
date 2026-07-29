using System.Diagnostics;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for shoal-safe S-102 viewport-stride sampling (issue
/// #496).
/// </summary>
public sealed class S102ShoalAwareSamplingTests
{
    [Fact]
    public void Sample_StridedBaseGrid_PreservesShoalBetweenNearestSamples()
    {
        var source = CreateSource(
            rows: 6,
            cols: 6,
            (row, col) => new BathymetryValue(
                row == 1 && col == 1 ? 1f : 50f,
                row == 2 && col == 2 ? 9f : 0.5f));
        var region = new GridRegion(0, 6, 0, 6, rowStride: 3, colStride: 3);

        var sampled = source.Sample(region);

        Assert.Equal(2, sampled.Metadata.NumRows);
        Assert.Equal(2, sampled.Metadata.NumColumns);
        Assert.Equal(50.01, sampled.Metadata.OriginLatitude, precision: 6);
        Assert.Equal(-0.99, sampled.Metadata.OriginLongitude, precision: 6);
        Assert.Equal(1f, sampled.GetField("depth")[0, 0]);
        Assert.Equal(9f, sampled.GetField("uncertainty")[0, 0]);

        var sourceValues = source.Coverage.Values;
        Assert.Equal(50f, sourceValues[0].Depth);
        Assert.Equal(50f, sourceValues[3].Depth);
        Assert.Equal(50f, sourceValues[18].Depth);
        Assert.Equal(50f, sourceValues[21].Depth);
    }

    [Fact]
    public void Sample_StridedBaseGrid_PoolsTrailingPartialWindow()
    {
        var source = CreateSource(
            rows: 5,
            cols: 5,
            (row, col) => new BathymetryValue(
                row == 4 && col == 4 ? 2f : 40f,
                0.5f));
        var region = new GridRegion(0, 5, 0, 5, rowStride: 3, colStride: 3);

        var sampled = source.Sample(region);

        Assert.Equal(2, sampled.Metadata.NumRows);
        Assert.Equal(2, sampled.Metadata.NumColumns);
        Assert.Equal(2f, sampled.GetField("depth")[1, 1]);
        Assert.Equal(50.0075, sampled.Metadata.OriginLatitude, precision: 6);
        Assert.Equal(0.025, sampled.Metadata.SpacingLatitudinal, precision: 6);
        Assert.Equal(-1.005, sampled.Metadata.OriginLongitude - sampled.Metadata.SpacingLongitudinal / 2, precision: 6);
        Assert.Equal(-0.955, sampled.Metadata.OriginLongitude + 1.5 * sampled.Metadata.SpacingLongitudinal, precision: 6);
    }

    [Fact]
    public void Sample_StridedOverview_PreservesShoalBetweenNearestSamples()
    {
        var source = CreateSource(
            rows: 8,
            cols: 8,
            (row, col) => new BathymetryValue(
                row == 2 && col == 2 ? 3f : 60f,
                0.5f));
        source.SelectOverviewLevel(1);
        var region = new GridRegion(0, 4, 0, 4, rowStride: 2, colStride: 2);

        var sampled = source.Sample(region);

        Assert.Equal(2, sampled.Metadata.NumRows);
        Assert.Equal(2, sampled.Metadata.NumColumns);
        Assert.Equal(3f, sampled.GetField("depth")[0, 0]);
    }

    [Fact]
    public void Sample_StridedBaseGrid_ExcludesNoData()
    {
        var source = CreateSource(
            rows: 2,
            cols: 4,
            (row, col) => col < 2
                ? new BathymetryValue(
                    row == 1 && col == 1 ? 7f : S102CoverageSource.FillValue,
                    row == 1 && col == 1 ? 2f : S102CoverageSource.FillValue)
                : new BathymetryValue(
                    S102CoverageSource.FillValue,
                    S102CoverageSource.FillValue));
        var region = new GridRegion(0, 2, 0, 4, rowStride: 2, colStride: 2);

        var sampled = source.Sample(region);

        Assert.Equal(7f, sampled.GetField("depth")[0, 0]);
        Assert.Equal(2f, sampled.GetField("uncertainty")[0, 0]);
        Assert.Equal(S102CoverageSource.FillValue, sampled.GetField("depth")[0, 1]);
        Assert.Equal(S102CoverageSource.FillValue, sampled.GetField("uncertainty")[0, 1]);
    }

    [Fact]
    public void Sample_TagsShoalBiasedReducer()
    {
        var source = CreateSource(
            rows: 2,
            cols: 2,
            (_, _) => new BathymetryValue(10f, 0.5f));
        using var activity = new Activity("test");
        activity.Start();

        source.Sample(new GridRegion(0, 2, 0, 2, rowStride: 2, colStride: 2));

        Assert.Equal("min", activity.GetTagItem(TelemetryTags.CoverageReducer));
    }

    [Fact]
    public void Sample_StrideLargerThanRegion_DoesNotOverAllocate()
    {
        var source = CreateSource(
            rows: 5,
            cols: 5,
            (row, col) => new BathymetryValue(
                row == 4 && col == 4 ? 2f : 40f,
                0.5f));
        var region = new GridRegion(
            rowStart: 0,
            rowEnd: 5,
            colStart: 0,
            colEnd: 5,
            rowStride: 50_000,
            colStride: 50_000);

        var sampled = source.Sample(region);

        Assert.Equal(1, sampled.Metadata.NumRows);
        Assert.Equal(1, sampled.Metadata.NumColumns);
        Assert.Equal(2f, sampled.GetField("depth")[0, 0]);
    }

    private static S102CoverageSource CreateSource(
        int rows,
        int cols,
        Func<int, int, BathymetryValue> valueFactory)
    {
        var values = new BathymetryValue[rows * cols];
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                values[row * cols + col] = valueFactory(row, col);
            }
        }

        var coverage = new BathymetryCoverage
        {
            OriginLatitude = 50,
            OriginLongitude = -1,
            SpacingLatitudinal = 0.01,
            SpacingLongitudinal = 0.01,
            NumPointsLatitudinal = rows,
            NumPointsLongitudinal = cols,
            Values = values,
        };
        var dataset = new S102Dataset
        {
            HorizontalCRS = 4326,
            Coverages = [coverage],
        };
        return new S102CoverageSource(dataset);
    }
}
