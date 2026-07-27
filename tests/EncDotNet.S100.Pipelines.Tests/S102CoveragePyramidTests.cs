using System.Reflection;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines.Coverage;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Integration tests for the coverage overview pyramid on an
/// <see cref="S102CoverageSource"/> (issue #486). Builds a small
/// synthetic S-102 HDF5 in temp storage and exercises the additive
/// <see cref="ICoverageSource"/> pyramid API + level-aware
/// <see cref="ICoverageSource.Sample"/> / <see cref="ICoverageSource.Metadata"/>.
/// </summary>
public class S102CoveragePyramidTests
{
    private struct BathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    /// <summary>
    /// Writes an 8×8 synthetic S-102 grid whose depths increase
    /// left-to-right and top-to-bottom (so pyramid pooling has
    /// predictable outputs). Uncertainties are constant.
    /// </summary>
    private static string WriteSyntheticGrid(int rows = 8, int cols = 8)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        var values = new BathyRow[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                // Row 0 is southernmost per S-102 convention; use a
                // simple linear ramp so predictions are trivial.
                values[r * cols + c] = new BathyRow
                {
                    Depth = 10.0f + r * 1.0f + c * 0.1f,
                    Uncertainty = 0.1f,
                };
            }
        }

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = rows,
                ["numPointsLongitudinal"] = cols,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };
        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-102.3.0.0",
                ["horizontalCRS"] = 4326,
                ["verticalDatum"] = 10,
            },
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };
        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
        return path;
    }

    private static S102CoverageSource OpenSource(string path)
    {
        using var hdf = PureHdfFile.Open(path);
        var dataset = S102DatasetReader.Read(hdf);
        return new S102CoverageSource(dataset);
    }

    [Fact]
    public void AvailableOverviewLevels_ReflectsPyramidGeometry()
    {
        var path = WriteSyntheticGrid(rows: 8, cols: 8);
        try
        {
            var source = OpenSource(path);
            var levels = source.AvailableOverviewLevels;

            // 8 → 4 → 2 → 1 = four levels (0..3)
            Assert.Equal(4, levels.Count);
            Assert.Equal(8, levels[0].Rows);
            Assert.Equal(4, levels[1].Rows);
            Assert.Equal(2, levels[2].Rows);
            Assert.Equal(1, levels[3].Rows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DefaultBehaviour_Level0_UnchangedFromBaseGrid()
    {
        // Regression guard: existing callers that never touch the
        // pyramid API must see identical Metadata + Sample output.
        var path = WriteSyntheticGrid();
        try
        {
            var source = OpenSource(path);
            Assert.Equal(0, source.SelectedOverviewLevel);
            Assert.Equal(8, source.Metadata.GridMetadata.NumRows);
            Assert.Equal(8, source.Metadata.GridMetadata.NumColumns);
            Assert.Equal(0.01, source.Metadata.GridMetadata.SpacingLatitudinal, precision: 6);

            var sampled = source.Sample(GridRegion.Full);
            Assert.Equal(8 * 8, sampled.Values["depth"].Length);
            // Cell (r=0,c=0) = shoalest = 10.0
            Assert.Equal(10.0f, sampled.Values["depth"][0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectOverviewLevel_LevelOne_ReturnsShoalBiasedDepth()
    {
        var path = WriteSyntheticGrid();
        try
        {
            var source = OpenSource(path);
            source.SelectOverviewLevel(1);
            Assert.Equal(1, source.SelectedOverviewLevel);

            var meta = source.Metadata;
            Assert.Equal(4, meta.GridMetadata.NumRows);
            Assert.Equal(4, meta.GridMetadata.NumColumns);
            Assert.Equal(0.02, meta.GridMetadata.SpacingLatitudinal, precision: 6);

            var sampled = source.Sample(GridRegion.Full);
            Assert.Equal(16, sampled.Values["depth"].Length);

            // Depth safety: pooled cell must never look safer (deeper)
            // than the shoalest of its 4 base cells. NW pool covers
            // rows 0-1, cols 0-1 whose depths are (10.0, 10.1, 11.0,
            // 11.1); shoalest = 10.0.
            Assert.Equal(10.0f, sampled.Values["depth"][0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectOverviewLevel_UncertaintyIsMaxOfPool()
    {
        var path = WriteSyntheticGrid();
        try
        {
            var source = OpenSource(path);
            source.SelectOverviewLevel(1);
            var sampled = source.Sample(GridRegion.Full);
            // All base uncertainties = 0.1; max = 0.1 for every pool.
            foreach (var u in sampled.Values["uncertainty"])
                Assert.Equal(0.1f, u, precision: 5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectOverviewLevel_OutOfRange_Throws()
    {
        var path = WriteSyntheticGrid();
        try
        {
            var source = OpenSource(path);
            Assert.Throws<ArgumentOutOfRangeException>(() => source.SelectOverviewLevel(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.SelectOverviewLevel(99));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectOverviewLevel_RoundTripsBackToZero()
    {
        var path = WriteSyntheticGrid();
        try
        {
            var source = OpenSource(path);
            source.SelectOverviewLevel(2);
            Assert.Equal(2, source.SelectedOverviewLevel);
            Assert.Equal(2, source.Metadata.GridMetadata.NumRows);

            source.SelectOverviewLevel(0);
            Assert.Equal(0, source.SelectedOverviewLevel);
            Assert.Equal(8, source.Metadata.GridMetadata.NumRows);

            var sampled = source.Sample(GridRegion.Full);
            Assert.Equal(8 * 8, sampled.Values["depth"].Length);
            Assert.Equal(10.0f, sampled.Values["depth"][0]);
        }
        finally { File.Delete(path); }
    }
}
