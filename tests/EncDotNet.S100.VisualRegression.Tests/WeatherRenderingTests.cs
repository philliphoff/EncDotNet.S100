using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for the code-defined weather styles (#1068): each
/// style in the Day, Dusk and Night palettes over in-memory grids, rendered
/// through the headless Skia compositor, and a composite of an S-101 cell, the
/// wave height fill, wind barbs and S-111 arrows in S-98 order. The drawing
/// polish (#1072) adds southern-hemisphere barbs, the wave fill clipped to the
/// water of an S-101 cell, and labelled pressure isobars.
/// </summary>
/// <remarks>
/// The grids are generated here, not read from files: no weather reader is
/// registered yet (#1071), and the styles only need a
/// <see cref="WeatherCoverageSource"/>.
/// </remarks>
public sealed class WeatherRenderingTests
{
    private static readonly DateTime Run = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------
    // One style per golden, in each palette.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Wave_height_fill(PaletteType palette)
    {
        // Heights from 0 m in the west to 15 m in the east, rippled north to
        // south, so every band shows.
        var source = WaveSource(17, 25, 0.25, 47, -6, (row, col, cols) =>
            ((float)Math.Max(0, 15.0 * col / (cols - 1) + Math.Sin(row * 0.6)), 270f));

        var bitmap = await RenderAsync(source, new WaveHeightStyle(), palette, 480, 320);

        await TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.01).UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Wind_speed_fill(PaletteType palette)
    {
        // A westerly from calm in the west to 70 kn in the east: every Beaufort force.
        var source = WindSource(17, 25, 0.25, 47, -6, (row, col, cols) =>
            (Math.Max(0, 70.0 * col / (cols - 1) + 3 * Math.Sin(row * 0.6)), 270.0));

        var bitmap = await RenderAsync(source, new WindSpeedStyle(), palette, 480, 320);

        await TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.01).UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Wind_barbs(PaletteType palette)
    {
        // 42 cells at 1°: calm, then 5 kn more in each cell up to 205 kn (drawn
        // as 200), turning 45° from cell to cell.
        var source = WindSource(6, 7, 1.0, 45, -6, (row, col, cols) =>
        {
            int i = row * cols + col;
            return (5.0 * i, i * 45.0 % 360);
        });

        var bitmap = await RenderAsync(source, new WindBarbStyle(), palette, 640, 560);

        var background = new SKColor(Background(palette));
        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 2500, maxDifferentInkFraction: 0.25, background)
            .UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Wave_direction_arrows(PaletteType palette)
    {
        // 35 cells at 1°: heights from 0.05 m (no arrow) to 12 m, waves coming
        // from a direction 30° further round in each cell.
        var source = WaveSource(5, 7, 1.0, 45, -6, (row, col, cols) =>
        {
            int i = row * cols + col;
            return (i == 0 ? 0.05f : 0.35f * i, i * 30f % 360);
        });

        var bitmap = await RenderAsync(source, new WaveDirectionStyle(), palette, 640, 480);

        var background = new SKColor(Background(palette));
        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 2000, maxDifferentInkFraction: 0.25, background)
            .UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task Wind_barbs_in_the_southern_hemisphere(PaletteType palette)
    {
        // The Wind_barbs grid moved to 45°S–50°S: the same speeds and
        // directions, drawn with the mirrored southern barbs.
        var source = WindSource(6, 7, 1.0, -50, -6, (row, col, cols) =>
        {
            int i = row * cols + col;
            return (5.0 * i, i * 45.0 % 360);
        });

        var bitmap = await RenderAsync(source, new WindBarbStyle(), palette, 640, 560);

        var background = new SKColor(Background(palette));
        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 2500, maxDifferentInkFraction: 0.25, background)
            .UseParameters(palette);
    }

    [Theory]
    [InlineData(1.0, "east")]
    [InlineData(-1.0, "west")]
    public async Task Barb_feathers_change_side_at_the_equator(double latitude, string side)
    {
        // One 25 kn northerly: the staff points up (north) from the station, so
        // its feathers are east of the staff in the north and west of it in
        // the south.
        var source = WindSource(3, 3, 1.0, latitude - 1, -6, (row, col, _) =>
            row == 1 && col == 1 ? (25.0, 0.0) : (double.NaN, double.NaN));

        using var bitmap = await RenderAsync(source, new WindBarbStyle(), PaletteType.Day, 200, 200, CentredViewport(-5, latitude, 2, 200));

        var (west, east, _, _) = InkByHalf(bitmap, new SKColor(Background(PaletteType.Day)));
        Assert.True(side == "east" ? east > 3 * west : west > 3 * east, $"Ink west {west}, east {east}: the feathers aren't {side} of the staff.");
    }

    [Fact]
    public async Task Wave_height_fill_clipped_to_the_water_of_an_S101_cell()
    {
        // IHO test cell 101AA00DS0020 (32°S): land either side of a channel.
        var cellPath = Path.Combine(TestHelpers.DatasetsRoot, "S101", "S-101", "DATASET_FILES", "101AA00DS0020.000");
        Assert.SkipUnless(File.Exists(cellPath), $"S-101 test dataset not present: {cellPath}");

        using var manager = CreateCatalogueManager();
        var s101 = await new S101DatasetProcessor(
                cellPath, manager, new MoonSharpLuaEngine(), new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue))
            .BuildVectorPortrayalAsync(new RenderContext(), Ct);

        // Wave height rising from 0.5 m in the south-west to 6 m in the
        // north-east, over the whole view, land included.
        var waves = await WeatherPortrayal.BuildAsync(
            WaveSource(20, 20, 0.012, -32.33, 61.97, (row, col, _) => (0.5f + 0.15f * (row + col), 250f)),
            [new WaveHeightStyle()],
            "waves.grb2",
            cancellationToken: Ct);

        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());
        var options = new HeadlessCompositeOptions { Viewport = CentredViewport(62.0833, -32.2167, 0.2, 640) };
        using var chart = compositor.Render([HeadlessCompositeInput.ForVector(s101)], options);
        var bitmap = compositor.Render([HeadlessCompositeInput.ForVector(s101), HeadlessCompositeInput.ForCoverage(waves)], options);

        // The fill covers the whole view, so only pixels it left alone — the
        // land it was clipped from — match the chart drawn on its own.
        int same = 0, total = 0;
        for (int y = 0; y < chart.Height; y += 2)
        {
            for (int x = 0; x < chart.Width; x += 2)
            {
                total++;
                if (chart.GetPixel(x, y) == bitmap.GetPixel(x, y))
                    same++;
            }
        }

        Assert.True(same > total / 10, $"Only {same} of {total} sampled pixels are untouched by the wave fill: it isn't clipped to water.");
        await TestHelpers.VerifyBitmap(bitmap);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task Pressure_isobars(PaletteType palette)
    {
        // A 976 hPa low west of a 1030 hPa high over the North Atlantic, on a
        // 1° grid: isobars every 4 hPa, labelled in hectopascals.
        var source = PressureSource(21, 31, 1.0, 40, -40, (lat, lon) =>
        {
            double low = 1012 - 36 * Math.Exp(-(Square(lat - 50) + Square(lon + 30)) / 40.0);
            double high = 18 * Math.Exp(-(Square(lat - 45) + Square(lon + 15)) / 60.0);
            return (float)((low + high) * 100);
        });

        var bitmap = await RenderAsync(source, new PressureIsobarStyle(), palette, 640, 480);

        var background = new SKColor(Background(palette));
        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 3000, maxDifferentInkFraction: 0.25, background)
            .UseParameters(palette);

        static double Square(double x) => x * x;
    }

    // ---------------------------------------------------------------
    // Direction: pixel checks on a single symbol.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(270.0, "west")]
    [InlineData(90.0, "east")]
    [InlineData(0.0, "north")]
    [InlineData(180.0, "south")]
    public async Task A_barb_staff_points_into_the_wind(double fromDirection, string side)
    {
        // One 25 kn barb in the middle of a 3 × 3 grid; the other cells have no data.
        var source = WindSource(3, 3, 1.0, 45, -6, (row, col, _) =>
            row == 1 && col == 1 ? (25.0, fromDirection) : (double.NaN, double.NaN));

        using var bitmap = await RenderAsync(source, new WindBarbStyle(), PaletteType.Day, 200, 200, CentredViewport(-5, 46, 2, 200));

        var (west, east, north, south) = InkByHalf(bitmap, new SKColor(Background(PaletteType.Day)));
        var expected = side switch
        {
            "west" => west > 5 * east,
            "east" => east > 5 * west,
            "north" => north > 5 * south,
            _ => south > 5 * north,
        };
        Assert.True(expected, $"Ink west {west}, east {east}, north {north}, south {south}: the staff doesn't point {side}.");
    }

    [Fact]
    public async Task A_wave_arrow_points_the_way_the_waves_travel()
    {
        // Waves from the west (270°) travel east: the arrow head is east of the cell.
        var source = WaveSource(3, 3, 1.0, 45, -6, (row, col, _) =>
            row == 1 && col == 1 ? (3f, 270f) : (float.NaN, float.NaN));

        using var bitmap = await RenderAsync(source, new WaveDirectionStyle(), PaletteType.Day, 200, 200, CentredViewport(-5, 46, 2, 200));

        var (west, east, _, _) = InkByHalf(bitmap, new SKColor(Background(PaletteType.Day)));
        Assert.True(east > west * 1.3, $"Ink west {west}, east {east}: the arrow head isn't east.");
    }

    // ---------------------------------------------------------------
    // Composite: S-101 + wave fill + barbs + S-111 arrows.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Composite_of_S101_waves_barbs_and_S111_in_S98_order()
    {
        // IHO test cell 101AA00DS0020 (62.00°E to 62.17°E, 32.30°S to 32.13°S):
        // land, a buoyed channel and depth areas.
        var cellPath = Path.Combine(TestHelpers.DatasetsRoot, "S101", "S-101", "DATASET_FILES", "101AA00DS0020.000");
        Assert.SkipUnless(File.Exists(cellPath), $"S-101 test dataset not present: {cellPath}");

        using var manager = CreateCatalogueManager();
        var s101 = await new S101DatasetProcessor(
                cellPath, manager, new MoonSharpLuaEngine(), new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue))
            .BuildVectorPortrayalAsync(new RenderContext(), Ct);

        // Wave height rising from 0.5 m in the south-west to 6 m in the north-east.
        var waves = await WeatherPortrayal.BuildAsync(
            WaveSource(20, 20, 0.01, -32.31, 61.99, (row, col, _) => (0.5f + 0.15f * (row + col), 250f)),
            [new WaveHeightStyle()],
            "waves.grb2",
            cancellationToken: Ct);

        // A 15–40 kn wind veering across the cell.
        var wind = await WeatherPortrayal.BuildAsync(
            WindSource(5, 5, 0.04, -32.29, 62.005, (row, col, cols) =>
            {
                int i = row * cols + col;
                return (15 + i, 200 + 4.0 * i);
            }),
            [new WindBarbStyle()],
            "wind.grb2",
            cancellationToken: Ct);

        var s111Path = WriteS111(Path.Combine(Path.GetTempPath(), $"111XX00_WEATHER_{Guid.NewGuid():N}.h5"));
        try
        {
            using var s111Processor = new S111DatasetProcessor(s111Path, manager, new ProjNetCrsTransformFactory());
            var s111 = await s111Processor.BuildCoveragePortrayalAsync(new RenderContext(), Ct);

            HeadlessCompositeInput[] inputs =
            [
                HeadlessCompositeInput.ForVector(s101),
                HeadlessCompositeInput.ForCoverage(s111),
                HeadlessCompositeInput.ForCoverage(wind),
                HeadlessCompositeInput.ForCoverage(waves),
            ];

            // S-98 order, whatever the input order: chart fills, then the wave
            // fill on the on-demand surface, then the chart's line work, then
            // the S-111 arrows and the barbs above them on DynamicArrows.
            var order = PaintOrder(inputs);
            int areas = order.FindIndex(i => i.Plane == S98DisplayPlane.BaseChartUnder);
            int waveFill = order.FindIndex(i => i.SourceFeatureType == WaveHeightStyle.Key);
            int lines = order.FindIndex(i => i.Plane == S98DisplayPlane.BaseChartOver);
            int currents = order.FindIndex(i => i.SourceDatasetId == s111.SourceDatasetId);
            int barbs = order.FindIndex(i => i.SourceFeatureType == WindBarbStyle.Key);
            Assert.True(areas >= 0 && areas < waveFill && waveFill < lines && lines < currents && currents < barbs,
                $"Paint order: {string.Join(", ", order.Select(i => $"{i.SourceDatasetId}/{i.SourceFeatureType}@{i.Plane}:{i.WithinPlanePriority}"))}");

            var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory())
                .Render(inputs, new HeadlessCompositeOptions { Viewport = CentredViewport(62.0833, -32.2167, 0.2, 640) });

            await TestHelpers.VerifyBitmap(bitmap);
        }
        finally
        {
            File.Delete(s111Path);
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    // Renders one style over the grid, fitted to the grid or to a viewport.
    private static async Task<SKBitmap> RenderAsync(
        WeatherCoverageSource source, WeatherCoverageStyle style, PaletteType palette, int width, int height, Viewport? viewport = null)
    {
        var result = await WeatherPortrayal.BuildAsync(
            source, [style], "weather.grb2", new RenderContext { Palette = palette }, cancellationToken: Ct);
        Assert.Single(result.SubLayers);

        return new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(
            [HeadlessCompositeInput.ForCoverage(result)],
            new HeadlessCompositeOptions
            {
                Width = width,
                Height = height,
                Viewport = viewport,
                Background = RgbaColor.FromHex($"#{Background(palette) & 0xFFFFFF:X6}"),
                // Draw the style even when it's off by default (#1072).
                ShownLayerKeys = [style.LayerKey],
            });
    }

    // A square size × size viewport centred on (longitude, latitude), spanning
    // the same Web Mercator distance each way, so the point is the centre pixel.
    private static Viewport CentredViewport(double longitude, double latitude, double longitudeSpan, int size)
    {
        var (x, y) = WebMercator.FromLonLat(longitude, latitude);
        var (west, _) = WebMercator.FromLonLat(longitude - longitudeSpan / 2, latitude);
        double half = x - west;
        var (_, south) = WebMercator.ToLonLat(x, y - half);
        var (_, north) = WebMercator.ToLonLat(x, y + half);
        return new Viewport
        {
            MinLongitude = longitude - longitudeSpan / 2,
            MaxLongitude = longitude + longitudeSpan / 2,
            MinLatitude = south,
            MaxLatitude = north,
            WidthPixels = size,
            HeightPixels = size,
            ScaleDenominator = 1_000_000,
        };
    }

    // The order the compositor paints the inputs' sub-layers in, bottom first.
    private static List<SubLayerStackItem> PaintOrder(IReadOnlyList<HeadlessCompositeInput> inputs)
    {
        var items = new List<SubLayerStackItem>();
        foreach (var input in inputs)
        {
            if (input.Vector is { } vector)
            {
                items.AddRange(vector.SubLayers.Select(sub => new SubLayerStackItem(
                    new VectorStackPayload(vector, sub), sub.Plane, sub.WithinPlanePriority, vector.SourceDatasetId, sub.SourceFeatureType)));
            }
            else if (input.Coverage is { } coverage)
            {
                items.AddRange(coverage.SubLayers.Select(sub => new SubLayerStackItem(
                    new CoverageStackPayload(coverage, sub), sub.Plane, sub.WithinPlanePriority, coverage.SourceDatasetId, sub.SourceFeatureType)));
            }
        }

        return new InteroperabilityAuthority().Sort(items).ToList();
    }

    // A wave group of height and direction on a regular grid from (south, west).
    private static WeatherCoverageSource WaveSource(
        int rows, int cols, double spacing, double south, double west,
        Func<int, int, int, (float Height, float FromDirection)> cell)
    {
        var heights = new float[rows * cols];
        var directions = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
                (heights[r * cols + c], directions[r * cols + c]) = cell(r, c, cols);
        }

        var grid = new WeatherNativeGrid(rows, cols, south, west, spacing, spacing);
        WeatherParameter[] parameters = [Parameter(WeatherParameters.WaveHeight, heights), Parameter(WeatherParameters.WaveDirection, directions)];
        return new WeatherCoverageSource(new WeatherGridSet(new WeatherOrigin("TEST", Run), grid, parameters), WeatherParameters.WaveGroup, parameters);
    }

    // A wind group of U and V on a regular grid, from each cell's speed in
    // knots and the direction it blows from; the source derives speed and
    // direction back from them.
    private static WeatherCoverageSource WindSource(
        int rows, int cols, double spacing, double south, double west,
        Func<int, int, int, (double Knots, double FromDirection)> cell)
    {
        var u = new float[rows * cols];
        var v = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var (knots, from) = cell(r, c, cols);
                double speed = Speed.FromKnots(knots).TotalMetresPerSecond;
                double radians = from * Math.PI / 180;
                u[r * cols + c] = (float)(-speed * Math.Sin(radians));
                v[r * cols + c] = (float)(-speed * Math.Cos(radians));
            }
        }

        var grid = new WeatherNativeGrid(rows, cols, south, west, spacing, spacing);
        WeatherParameter[] parameters = [Parameter(WeatherParameters.WindU, u), Parameter(WeatherParameters.WindV, v)];
        return new WeatherCoverageSource(new WeatherGridSet(new WeatherOrigin("TEST", Run), grid, parameters), WeatherParameters.WindGroup, parameters);
    }

    // Mean sea level pressure, in pascals, from each node's latitude and longitude.
    private static WeatherCoverageSource PressureSource(
        int rows, int cols, double spacing, double south, double west, Func<double, double, float> pascals)
    {
        var values = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
                values[r * cols + c] = pascals(south + r * spacing, west + c * spacing);
        }

        var grid = new WeatherNativeGrid(rows, cols, south, west, spacing, spacing);
        WeatherParameter[] parameters = [Parameter(WeatherParameters.PressureMsl, values)];
        return new WeatherCoverageSource(new WeatherGridSet(new WeatherOrigin("TEST", Run), grid, parameters), WeatherParameters.PressureGroup, parameters);
    }

    private static WeatherParameter Parameter(string id, float[] values)
    {
        var definition = WeatherParameters.Find(id)!;
        return new WeatherParameter(definition, definition.Level ?? WeatherLevel.Surface, [WeatherTimeStep.FromValues(Run, values)]);
    }

    // A 5 × 5 S-111 grid over the cell, between the barbs: 0.5 to 2.9 kn.
    private static string WriteS111(string path)
    {
        const int rows = 5, cols = 5;
        var values = new S111FixtureBuilder.SpecRow[rows * cols];
        for (int i = 0; i < values.Length; i++)
            values[i] = new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 0.5f + 0.1f * i, SurfaceCurrentDirection = 40f + 3f * i };

        return S111FixtureBuilder.WriteTimeSeries(
            path,
            new S111FixtureBuilder.GridGeoreference(-32.27, 62.025, 0.04, 0.04, rows, cols),
            [("20261025T000000Z", values)],
            new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-111.2.0",
                ["issueDate"] = "20261025",
                ["horizontalCRS"] = 4326,
                ["geographicIdentifier"] = "Weather composite (synthetic test data)",
            },
            featureAttributes: new Dictionary<string, object> { ["typeOfCurrentData"] = (byte)6 });
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

    // Ink in each half of the image: west and east of the centre column, north
    // and south of the centre row.
    private static (int West, int East, int North, int South) InkByHalf(SKBitmap bitmap, SKColor background)
    {
        int west = 0, east = 0, north = 0, south = 0;
        int cx = bitmap.Width / 2, cy = bitmap.Height / 2;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.Red - background.Red) + Math.Abs(p.Green - background.Green) + Math.Abs(p.Blue - background.Blue) < 60)
                    continue;
                if (x < cx - 2) west++;
                if (x > cx + 2) east++;
                if (y < cy - 2) north++;
                if (y > cy + 2) south++;
            }
        }

        return (west, east, north, south);
    }

    // The grounds of the S-104 renders: the palettes' water colours.
    private static uint Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => 0xFF5B6B78u,
        PaletteType.Night => 0xFF0B0F14u,
        _ => 0xFFF6FBFEu,
    };
}
