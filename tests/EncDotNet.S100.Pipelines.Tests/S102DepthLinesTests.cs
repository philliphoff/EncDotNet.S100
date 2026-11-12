using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

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

    [Theory]
    [InlineData(21.4, -9, true)]  // nearest node column 1: a depth
    [InlineData(21.6, -9, false)] // nearest node column 2: no-data
    [InlineData(19.4, -9, false)] // west of the grid
    [InlineData(21.0, -7.4, false)] // north of the grid
    public void Data_area_reads_the_nearest_node(double longitude, double latitude, bool expected)
    {
        var area = S102DepthLines.DataArea(Layer(3, 4, (_, c) => c >= 2 ? Fill : 10f));
        var (x, y) = WebMercator.FromLonLat(longitude, latitude);

        Assert.Equal(expected, area.HasData(x, y));
    }

    [Fact]
    public void A_cell_limit_is_hidden_where_the_next_cell_carries_on()
    {
        // West cell: nodes 20–25° E, depths in the western four columns, so
        // its limit runs north–south at 23.5° E. East cell: nodes 24–29° E.
        var west = Layer(5, 6, (_, c) => c >= 4 ? Fill : 10f);
        var east = Layer(5, 6, (_, _) => 12f, originLongitude: 24);

        var limit = LimitOf(Join(west, east), "west.h5");

        Assert.Empty(limit.Isolines);
    }

    [Fact]
    public void A_cell_limit_stays_where_the_next_cell_begins_further_on()
    {
        // The east cell starts at 26° E: a gap of S-101 between the two.
        var west = Layer(5, 6, (_, c) => c >= 4 ? Fill : 10f);
        var east = Layer(5, 6, (_, _) => 12f, originLongitude: 26);
        var alone = S102DepthLines.CoverageLimit(west);

        var limit = LimitOf(Join(west, east), "west.h5");

        Assert.Equal(alone.Select(l => l.MercatorCoordinates), limit.Isolines.Select(l => l.MercatorCoordinates));
    }

    [Fact]
    public void A_cell_limit_is_hidden_only_along_the_part_the_next_cell_carries_on()
    {
        // The east cell has depths in its southern three rows only (10°S to
        // 8°S), so the west cell's limit stays north of them.
        var west = Layer(9, 6, (_, c) => c >= 4 ? Fill : 10f);
        var east = Layer(9, 6, (r, _) => r <= 2 ? 12f : Fill, originLongitude: 24);

        var limit = LimitOf(Join(west, east), "west.h5");

        var line = Assert.Single(limit.Isolines);
        var latitudes = Enumerable.Range(0, line.MercatorCoordinates.Length / 2)
            .Select(i => WebMercator.ToLonLat(line.MercatorCoordinates[2 * i], line.MercatorCoordinates[(2 * i) + 1]).Latitude)
            .ToList();
        Assert.InRange(latitudes.Min(), -8.01, -6.99);
        Assert.Equal(-2, latitudes.Max(), 6);
    }

    [Fact]
    public void Cells_that_share_a_coastline_keep_their_limits()
    {
        // Both cells end at the same coast, 23.5° E, and overlap west of it.
        var west = Layer(5, 6, (_, c) => c >= 4 ? Fill : 10f);
        var other = Layer(5, 6, (_, c) => c >= 2 ? Fill : 11f, originLongitude: 22);
        var alone = S102DepthLines.CoverageLimit(west);

        var limit = LimitOf(Join(west, other), "west.h5");

        Assert.Equal(alone.Select(l => l.MercatorCoordinates), limit.Isolines.Select(l => l.MercatorCoordinates));
    }

    [Fact]
    public void An_inactive_cell_hides_no_limit()
    {
        var west = Layer(5, 6, (_, c) => c >= 4 ? Fill : 10f);
        var east = Layer(5, 6, (_, _) => 12f, originLongitude: 24);

        var limit = LimitOf(Join(west, east, eastActive: false), "west.h5");

        Assert.NotEmpty(limit.Isolines);
    }

    [Fact]
    public void Joining_again_with_the_same_cells_returns_the_same_lines()
    {
        var west = Layer(9, 6, (_, c) => c >= 4 ? Fill : 10f);
        var east = Layer(9, 6, (r, _) => r <= 2 ? 12f : Fill, originLongitude: 24);
        var stack = Stack(west, east);
        var loaded = Loaded(eastActive: true);
        var authority = new InteroperabilityAuthority();

        var first = LimitOf(authority.ApplyRules(stack, loaded), "west.h5");
        var second = LimitOf(authority.ApplyRules(stack, loaded), "west.h5");

        Assert.Same(first, second);
    }

    [Fact]
    public void A_cell_surface_is_handed_the_data_area_of_the_next_cell()
    {
        var west = Layer(5, 6, (_, c) => c >= 4 ? Fill : 10f);
        var east = Layer(5, 6, (_, _) => 12f, originLongitude: 24);
        var stack = Stack(west, east);
        var eastArea = ((CoveragePortrayalResult)((CoverageStackPayload)stack[^1].Payload).Result).DataArea;

        var surface = SurfaceOf(new InteroperabilityAuthority().ApplyRules(stack, Loaded(eastActive: true)), "west.h5");

        Assert.Same(eastArea, Assert.Single(surface.NeighbourDataAreas!));
    }

    [Fact]
    public void Cells_whose_extents_do_not_meet_are_not_joined()
    {
        var west = Layer(5, 6, (_, c) => c >= 4 ? Fill : 10f);
        var far = Layer(5, 6, (_, _) => 12f, originLongitude: 24);
        SubLayerStackItem[] stack =
        [
            .. Items(west, "west.h5", new GeographicBounds(20, -10, 25, -6)),
            .. Items(far, "east.h5", new GeographicBounds(40, -10, 45, -6)),
        ];

        var ruled = new InteroperabilityAuthority().ApplyRules(stack, Loaded(eastActive: true));

        Assert.Null(SurfaceOf(ruled, "west.h5").NeighbourDataAreas);
        Assert.NotEmpty(LimitOf(ruled, "west.h5").Isolines);
    }

    private static GridCoverageSubLayer SurfaceOf(IReadOnlyList<SubLayerStackItem> ruled, string datasetId) =>
        ruled
            .Where(i => i.SourceDatasetId == datasetId)
            .Select(i => ((CoverageStackPayload)i.Payload).SubLayer)
            .OfType<GridCoverageSubLayer>()
            .Single();

    private static IReadOnlyList<SubLayerStackItem> Join(
        StyledCoverageLayer west, StyledCoverageLayer east, bool eastActive = true) =>
        new InteroperabilityAuthority()
            .ApplyRules(Stack(west, east), Loaded(eastActive));

    private static LoadedDatasetInfo[] Loaded(bool eastActive) =>
    [
        new("west.h5", "S-102", Active: true),
        new("east.h5", "S-102", Active: eastActive),
    ];

    private static SubLayerStackItem[] Stack(
        StyledCoverageLayer west, StyledCoverageLayer east) =>
        [.. Items(west, "west.h5"), .. Items(east, "east.h5")];

    /// <summary>A cell's surface and limit sub-layers, as the S-102 processor lays them out.</summary>
    private static IEnumerable<SubLayerStackItem> Items(
        StyledCoverageLayer layer, string datasetId, GeographicBounds? extent = null)
    {
        var surface = new GridCoverageSubLayer
        {
            LayerKey = "s102.surface",
            LayerName = datasetId,
            Plane = S98DisplayPlane.Bathymetry,
            Coverage = layer,
            Viewport = new Viewport
            {
                MinLatitude = -10,
                MaxLatitude = 0,
                MinLongitude = 20,
                MaxLongitude = 30,
                WidthPixels = 10,
                HeightPixels = 10,
                ScaleDenominator = 1_000_000,
            },
        };
        var limit = new IsolineCoverageSubLayer
        {
            LayerKey = S102DatasetProcessor.CoverageLimitLayerKey,
            LayerName = datasetId,
            Plane = S98DisplayPlane.BaseChartOver,
            Isolines = S102DepthLines.CoverageLimit(layer),
            Ink = new RgbaColor(0, 0, 0),
            LabelHalo = new RgbaColor(255, 255, 255),
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = [surface, limit],
            Spec = new EncDotNet.S100.Core.SpecRef("S-102", default),
            SourceDatasetId = datasetId,
            Info = datasetId,
            DataArea = S102DepthLines.DataArea(layer),
            CoverageExtent = extent,
        };
        yield return new(new CoverageStackPayload(result, surface), S98DisplayPlane.Bathymetry, 0, datasetId);
        yield return new(new CoverageStackPayload(result, limit), S98DisplayPlane.BaseChartOver, 0, datasetId);
    }

    private static IsolineCoverageSubLayer LimitOf(
        IReadOnlyList<SubLayerStackItem> ruled, string datasetId) =>
        ruled
            .Where(i => i.SourceDatasetId == datasetId)
            .Select(i => ((CoverageStackPayload)i.Payload).SubLayer)
            .OfType<IsolineCoverageSubLayer>()
            .Single();

    [Fact]
    public async Task Processor_draws_the_safety_contour_and_limit_with_the_s101_line_work()
    {
        // Depth rises 1 m per row from 10 m; the north-east corner is no-data.
        var path = S102Grids.Write(8, 8, (r, c) => r >= 6 && c >= 6 ? Fill : 10f + r);
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(path, manager, TestLuaEngine.Create(), new ProjNetCrsTransformFactory());
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
            Assert.NotNull(result.DataArea);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Processor_reuses_the_traced_lines_until_the_safety_contour_or_region_changes()
    {
        var path = S102Grids.Write(8, 8, (r, c) => r >= 6 && c >= 6 ? Fill : 10f + r);
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(path, manager, TestLuaEngine.Create(), new ProjNetCrsTransformFactory());
        try
        {
            RenderContext At(double safety, PaletteType palette = PaletteType.Day) => new()
            {
                Palette = palette,
                Mariner = MarinerSettings.Default with
                {
                    ShallowContour = Depth.FromMetres(2),
                    SafetyContour = Depth.FromMetres(safety),
                    DeepContour = Depth.FromMetres(30),
                },
            };
            async Task<(IReadOnlyList<Isoline> Safety, IReadOnlyList<Isoline> Limit)> Lines(RenderContext context)
            {
                var result = await processor.BuildCoveragePortrayalAsync(context, TestContext.Current.CancellationToken);
                var lines = result.SubLayers.OfType<IsolineCoverageSubLayer>().ToList();
                return (lines.Single(l => l.LayerKey == S102DatasetProcessor.SafetyContourLayerKey).Isolines,
                        lines.Single(l => l.LayerKey == S102DatasetProcessor.CoverageLimitLayerKey).Isolines);
            }

            var first = await Lines(At(13.5));
            var repeat = await Lines(At(13.5, PaletteType.Night));
            var deeper = await Lines(At(14.5));

            Assert.Same(first.Safety, repeat.Safety);
            Assert.Same(first.Limit, repeat.Limit);
            Assert.NotSame(first.Safety, deeper.Safety);
            Assert.All(deeper.Safety, l => Assert.Equal(14.5, l.Level));
            Assert.Same(first.Limit, deeper.Limit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static StyledCoverageLayer Layer(int rows, int columns, Func<int, int, float> depth, double originLongitude = 20)
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
            OriginLongitude = originLongitude,
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
