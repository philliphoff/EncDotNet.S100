using System.Reflection;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-102 safety contour traced from the grid at the mariner's value
/// (S-164 UserSelectedSafetyContour) and the coverage-limit border (S-98
/// Part A §A-6.9.1), with no water-level adjustment (#1132 phase 2).
/// </summary>
public sealed class S102DepthLinesTests
{
    private const float Fill = S102CoverageSource.FillValue;

    [Fact]
    public void Safety_contour_runs_between_the_nodes_either_side_of_the_value()
    {
        // Depth 5, 10, 15, 20 m by column: 12 m lies 40 % of the way from column 1 to 2.
        var layer = Layer(3, 4, (_, c) => 5f + (5f * c));

        var lines = S102DepthLines.SafetyContour(layer, 12);

        var line = Assert.Single(lines);
        Assert.Equal(12, line.Level);
        Assert.Equal(string.Empty, line.Label);
        var x = WebMercator.FromLonLat(20 + 1.4, 0).X;
        Assert.All(Enumerable.Range(0, line.MercatorCoordinates.Length / 2), i => Assert.Equal(x, line.MercatorCoordinates[2 * i], 6));
    }

    [Fact]
    public void Safety_contour_is_absent_when_the_grid_does_not_reach_it()
    {
        var layer = Layer(3, 4, (_, c) => 5f + (5f * c));

        Assert.Empty(S102DepthLines.SafetyContour(layer, 30));
    }

    [Fact]
    public void Safety_contour_ends_at_no_data()
    {
        // The two eastern columns are no-data: the line can't cross into them.
        var layer = Layer(3, 4, (_, c) => c >= 2 ? Fill : 5f + (5f * c));

        Assert.Empty(S102DepthLines.SafetyContour(layer, 12));
        Assert.NotEmpty(S102DepthLines.SafetyContour(layer, 7));
    }

    [Fact]
    public void Coverage_limit_runs_half_way_to_the_no_data_nodes()
    {
        var layer = Layer(3, 4, (_, c) => c >= 2 ? Fill : 10f);

        var line = Assert.Single(S102DepthLines.CoverageLimit(layer));

        var x = WebMercator.FromLonLat(20 + 1.5, 0).X;
        Assert.All(Enumerable.Range(0, line.MercatorCoordinates.Length / 2), i => Assert.Equal(x, line.MercatorCoordinates[2 * i], 6));
    }

    [Fact]
    public void Coverage_limit_is_not_drawn_along_the_grid_edges()
    {
        Assert.Empty(S102DepthLines.CoverageLimit(Layer(3, 4, (_, _) => 10f)));
        Assert.Empty(S102DepthLines.CoverageLimit(Layer(3, 4, (_, _) => Fill)));
    }

    [Fact]
    public void A_native_transform_is_applied()
    {
        var layer = Layer(3, 4, (_, c) => c >= 2 ? Fill : 10f);

        var line = Assert.Single(S102DepthLines.CoverageLimit(layer, (x, y) => (x + 100, y)));

        Assert.Equal(WebMercator.FromLonLat(121.5, 0).X, line.MercatorCoordinates[0], 6);
    }

    [Fact]
    public void Surface_extent_is_the_corner_nodes_padded_by_half_a_cell()
    {
        // Nodes from 20°E to 23°E and 10°S to 8°S, 1° apart.
        var extent = S102DepthLines.SurfaceExtent(Layer(3, 4, (_, _) => 10f));

        var (minX, minY) = WebMercator.FromLonLat(20, -10);
        var (maxX, maxY) = WebMercator.FromLonLat(23, -8);
        Assert.Equal(minX - ((maxX - minX) / 3 / 2), extent.MinX, 6);
        Assert.Equal(maxY + ((maxY - minY) / 2 / 2), extent.MaxY, 6);
    }

    [Fact]
    public async Task Processor_draws_the_safety_contour_and_limit_with_the_s101_line_work()
    {
        // Depth rises 1 m per row from 10 m; the north-east corner is no-data.
        var path = WriteGrid(8, 8, (r, c) => r >= 6 && c >= 6 ? Fill : 10f + r);
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(path, manager, new MoonSharpLuaEngine(), new ProjNetCrsTransformFactory());
        try
        {
            var context = new RenderContext
            {
                Mariner = MarinerSettings.Default with
                {
                    ShallowContour = Depth.FromMetres(2),
                    SafetyContour = Depth.FromMetres(13.5),
                    DeepContour = Depth.FromMetres(30),
                },
            };

            var result = await processor.BuildCoveragePortrayalAsync(context, TestContext.Current.CancellationToken);

            var safety = Assert.Single(result.SubLayers.OfType<IsolineCoverageSubLayer>(), l => l.LayerKey == S102DatasetProcessor.SafetyContourLayerKey);
            var limit = Assert.Single(result.SubLayers.OfType<IsolineCoverageSubLayer>(), l => l.LayerKey == S102DatasetProcessor.CoverageLimitLayerKey);

            Assert.Equal(S98DisplayPlane.BaseChartOver, safety.Plane);
            Assert.Equal(2.0, safety.LineWidth); // 0.64 mm
            Assert.Empty(safety.DashPattern);
            Assert.NotEmpty(safety.Isolines);
            Assert.All(safety.Isolines, l => Assert.Equal(13.5, l.Level));

            // The row at 13.5 m is half way between rows 3 and 4: 50.035° N.
            var y = WebMercator.FromLonLat(0, 50.035).Y;
            Assert.All(
                Enumerable.Range(0, safety.Isolines[0].MercatorCoordinates.Length / 2),
                i => Assert.Equal(y, safety.Isolines[0].MercatorCoordinates[(2 * i) + 1], 3));

            Assert.Equal(S98DisplayPlane.BaseChartOver, limit.Plane);
            Assert.NotEmpty(limit.DashPattern);
            Assert.NotEmpty(limit.Isolines);
            Assert.True(limit.LineWidth < safety.LineWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static StyledCoverageLayer Layer(int rows, int columns, Func<int, int, float> depth)
    {
        var values = new float[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
                values[(r * columns) + c] = depth(r, c);
        }

        var metadata = new GridMetadata
        {
            NumRows = rows,
            NumColumns = columns,
            OriginLatitude = -10,
            OriginLongitude = 20,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]> { [S102DepthLines.DepthField] = values },
            },
            NoDataValue = Fill,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
    }

    private struct BathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    private static string WriteGrid(int rows, int cols, Func<int, int, float> depth)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        var values = new BathyRow[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
                values[(r * cols) + c] = new BathyRow { Depth = depth(r, c), Uncertainty = 0.1f };
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
                ["verticalDatum"] = 12,
            },
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };
        file.Write(path, new H5WriteOptions(FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return manager;
    }
}
