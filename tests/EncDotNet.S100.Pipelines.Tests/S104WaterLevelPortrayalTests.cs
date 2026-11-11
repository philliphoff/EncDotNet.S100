using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The SoundCharts S-104 portrayal built by <see cref="S104DatasetProcessor"/>
/// (#937, #483): station gauges with a trend mark and value label, and surface
/// isolines at an interval fixed per dataset.
/// </summary>
public sealed class S104WaterLevelPortrayalTests
{
    private static readonly ProjNetCrsTransformFactory Transforms = new();

    [Fact]
    public async Task Stations_are_S100_symbols_with_a_trend_mark_and_signed_value_label()
    {
        var path = WriteStations();
        try
        {
            var layer = await StationsAt(new S104DatasetProcessor(path, Transforms), new RenderContext());
            Assert.Equal(S104DatasetProcessor.StationLayerKey, layer.LayerKey);
            Assert.Equal(S98DisplayPlane.OtherChartOverlays, layer.Plane);

            AssertStation(layer, "Rising", S104CoverageStyle.RisingSymbol, "+1.42");
            AssertStation(layer, "Falling", S104CoverageStyle.FallingSymbol, "+0.87");
            AssertStation(layer, "Steady", S104CoverageStyle.SteadySymbol, "\u22120.12");
            AssertStation(layer, "Unknown", S104CoverageStyle.UnknownSymbol, "+0.31");
            AssertStation(layer, "Missing", S104CoverageStyle.NoDataSymbol, null);

            // Labels follow the S-101 label rules: SCAMIN-gated at 1:100 000,
            // drawn with the S-104 ink and halo tokens in a monospaced face.
            Assert.All(layer.Instructions.OfType<TextInstruction>(), text =>
            {
                Assert.Equal(S104CoverageStyle.StationLabelMaxScaleDenominator, text.ScaleMinimum);
                Assert.Equal(S104CoverageStyle.InkToken, text.FontColor);
                Assert.Equal(S104CoverageStyle.LabelHaloToken, text.HaloColor);
                Assert.Equal(TextFontProportion.MonoSpaced, text.FontProportion);
                Assert.Equal(TextHorizontalAlignment.Start, text.HorizontalAlignment);
            });
            Assert.All(layer.Instructions, instruction =>
                Assert.NotNull(layer.GeometryProvider.GetGeometry(instruction.FeatureReference)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Stations_carry_the_mariners_symbol_and_text_scale()
    {
        var path = WriteStations();
        try
        {
            var layer = await StationsAt(
                new S104DatasetProcessor(path, Transforms),
                new RenderContext { SymbolScale = 1.5, TextScale = 2.0 });

            Assert.Equal(1.5, layer.SymbolScale);
            Assert.Equal(2.0, layer.TextScale);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Station_symbols_resolve_to_one_ink_per_palette(PaletteType palette)
    {
        var path = WriteStations();
        try
        {
            var layer = await StationsAt(new S104DatasetProcessor(path, Transforms), new RenderContext { Palette = palette });
            Assert.True(layer.Palette.TryResolve(S104CoverageStyle.InkToken, out var inkHex));
            Assert.True(layer.Palette.TryResolve(S104CoverageStyle.SymbolFillToken, out var fillHex));
            Assert.Equal(S104CoverageStyle.Palette.Resolve(S104CoverageStyle.InkToken, palette), RgbaColor.FromHex(inkHex));
            Assert.Equal(S104CoverageStyle.Palette.Resolve(S104CoverageStyle.SymbolFillToken, palette), RgbaColor.FromHex(fillHex));

            foreach (var symbol in layer.Instructions.OfType<PointInstruction>().Select(p => p.SymbolReference!).Distinct())
            {
                var processed = Portrayals.SvgProcessor.Process(layer.SymbolProvider(symbol)!, layer.Palette);
                Assert.Contains(inkHex, processed, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("S104INK", processed, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Headless_station_labels_scale_with_the_text_scale()
    {
        var path = WriteStations();
        try
        {
            var processor = new S104DatasetProcessor(path, Transforms);
            int Ink(SKBitmap bitmap) => Enumerable.Range(0, bitmap.Width)
                .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)))
                .Count(p => p != SKColors.White);

            // A small frame keeps the render inside the 1:100 000 label limit.
            using var normal = await processor.RenderHeadlessAsync(
                400, 300, new RenderContext(), cancellationToken: TestContext.Current.CancellationToken);
            using var large = await processor.RenderHeadlessAsync(
                400, 300, new RenderContext { TextScale = 2.0 }, cancellationToken: TestContext.Current.CancellationToken);
            using var noText = await processor.RenderHeadlessAsync(
                400, 300, new RenderContext { HiddenInstructionCategories = DrawingInstructionCategory.Text },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(Ink(large) > Ink(normal), "Doubling the text scale must enlarge the labels.");
            Assert.True(Ink(noText) < Ink(normal), "Hiding text must drop the labels and keep the symbols.");
            Assert.True(Ink(noText) > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Surface_is_isolines_at_one_interval_across_every_step()
    {
        // Range over all steps is 0.1 … 0.9 m (under 1 m), so 0.25 m lines.
        var path = WriteGrid(stepLevels: [0.1, 0.5]);
        try
        {
            var processor = new S104DatasetProcessor(path, Transforms);
            var steps = processor.AvailableTimes;

            foreach (var step in steps)
            {
                var result = await processor.BuildCoveragePortrayalAsync(
                    new RenderContext { TimeStep = step }, TestContext.Current.CancellationToken);

                var isolines = Assert.IsType<IsolineCoverageSubLayer>(Assert.Single(result.SubLayers));
                Assert.Equal(S104DatasetProcessor.IsolineLayerKey, isolines.LayerKey);
                Assert.NotEmpty(isolines.Isolines);
                Assert.All(isolines.Isolines, line =>
                {
                    Assert.Equal(0, Math.IEEERemainder(line.Level, 0.25), precision: 9);
                    Assert.Equal(S104CoverageStyle.FormatIsolineLabel(line.Level, 0.25), line.Label);
                });
            }

            // The first step (0.1 … 0.5 m) has no 0.75 m line; the second does.
            var first = await IsolinesAt(processor, steps[0]);
            var second = await IsolinesAt(processor, steps[1]);
            Assert.DoesNotContain(first.Isolines, l => l.Level == 0.75);
            Assert.Contains(second.Isolines, l => l.Level == 0.75);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Isoline_labels_follow_the_text_scale()
    {
        var path = WriteGrid(stepLevels: [0.0]);
        try
        {
            var result = await new S104DatasetProcessor(path, Transforms).BuildCoveragePortrayalAsync(
                new RenderContext { TextScale = 2.0 }, TestContext.Current.CancellationToken);
            var isolines = Assert.IsType<IsolineCoverageSubLayer>(Assert.Single(result.SubLayers));

            Assert.Equal(20, isolines.LabelFontSize);
            Assert.Equal(6, isolines.LabelHaloWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Surface_isolines_lie_inside_the_grid_extent()
    {
        var path = WriteGrid(stepLevels: [0.0]);
        try
        {
            var isolines = await IsolinesAt(new S104DatasetProcessor(path, Transforms), null);
            var extent = Assert.NotNull(isolines.Extent);

            Assert.All(isolines.Isolines, line =>
            {
                for (int i = 0; i + 1 < line.MercatorCoordinates.Length; i += 2)
                {
                    Assert.InRange(line.MercatorCoordinates[i], extent.MinX, extent.MaxX);
                    Assert.InRange(line.MercatorCoordinates[i + 1], extent.MinY, extent.MaxY);
                }
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Headless_surface_draws_lines_without_fill()
    {
        var path = WriteGrid(stepLevels: [0.0]);
        try
        {
            using var bitmap = await new S104DatasetProcessor(path, Transforms)
                .RenderHeadlessAsync(200, 200, cancellationToken: TestContext.Current.CancellationToken);

            var pixels = Enumerable.Range(0, bitmap.Width)
                .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)))
                .ToArray();
            int drawn = pixels.Count(p => p != SKColors.White);
            Assert.InRange(drawn, 50, pixels.Length / 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Isolines_are_clipped_to_water()
    {
        // A vertical line down the middle of a 100 × 100 px frame, with land over
        // the south half: only the north half of the line is drawn.
        var (minX, minY) = WebMercator.FromLonLat(0, 0);
        var (maxX, maxY) = WebMercator.FromLonLat(1, 1);
        var midX = (minX + maxX) / 2;
        var land = new FeatureGeometry
        {
            Type = GeometryType.Surface,
            Coordinates =
            [
                new GeoPosition(-1, -1), new GeoPosition(0.5, -1), new GeoPosition(0.5, 2),
                new GeoPosition(-1, 2), new GeoPosition(-1, -1),
            ],
        };
        using var scene = new IsolineScene(
            [new IsolinePolyline("0", [midX, minY, midX, maxY])],
            new IsolineAppearance { Ink = SKColors.Black, LabelHalo = SKColors.White, DashPattern = [] },
            [land]);

        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            scene.Draw(
                canvas,
                (x, y) => new SKPoint((float)((x - minX) / (maxX - minX) * 100), (float)((maxY - y) / (maxY - minY) * 100)),
                drawLabels: false);
        }

        // Land covers latitude 0 … 0.5, the south half of the frame.
        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 90));
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(50, 10));
    }

    private static async Task<IsolineCoverageSubLayer> IsolinesAt(S104DatasetProcessor processor, DateTime? step)
    {
        var result = await processor.BuildCoveragePortrayalAsync(
            new RenderContext { TimeStep = step }, TestContext.Current.CancellationToken);
        return Assert.IsType<IsolineCoverageSubLayer>(Assert.Single(result.SubLayers));
    }

    private static async Task<VectorCoverageSubLayer> StationsAt(S104DatasetProcessor processor, RenderContext context)
    {
        var result = await processor.BuildCoveragePortrayalAsync(context, TestContext.Current.CancellationToken);
        return Assert.IsType<VectorCoverageSubLayer>(Assert.Single(result.SubLayers));
    }

    private static void AssertStation(VectorCoverageSubLayer layer, string station, string symbol, string? label)
    {
        var featureRef = "station:" + station;
        var point = Assert.Single(layer.Instructions.OfType<PointInstruction>(), p => p.FeatureReference == featureRef);
        Assert.Equal(symbol, point.SymbolReference);
        Assert.NotNull(layer.SymbolProvider(symbol));

        var text = layer.Instructions.OfType<TextInstruction>().SingleOrDefault(t => t.FeatureReference == featureRef);
        Assert.Equal(label, text?.Text);
    }

    private static string WriteStations()
    {
        var path = Path.GetTempFileName() + ".h5";
        S104Dcf8FixtureBuilder.WriteFile(
            path,
            new[]
            {
                Station("Rising", 51.90f, 4.10f, 1.42f, 2),
                Station("Falling", 51.92f, 4.14f, 0.87f, 1),
                Station("Steady", 51.88f, 4.16f, -0.12f, 3),
                Station("Unknown", 51.93f, 4.20f, 0.31f, 0),
                Station("Missing", 51.87f, 4.12f, -9999f, 2),
            },
            waterLevelTrendThreshold: 0.05);
        return path;
    }

    private static S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow> Station(
        string id, float lat, float lon, float height, byte trend) => new()
        {
            Identifier = id,
            Latitude = lat,
            Longitude = lon,
            StartDateTime = "20261014T100000Z",
            EndDateTime = "20261014T100000Z",
            TimeRecordInterval = 3600,
            Values = [new() { WaterLevelHeight = height, WaterLevelTrend = trend }],
        };

    /// <summary>
    /// A 4 × 5 grid rising 0.1 m per column from each step's base level.
    /// </summary>
    private static string WriteGrid(double[] stepLevels)
    {
        const int rows = 4, columns = 5;
        var path = Path.GetTempFileName() + ".h5";
        var steps = stepLevels.Select((level, i) =>
        {
            var values = new S104FixtureBuilder.SpecRow[rows * columns];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    values[r * columns + c] = new S104FixtureBuilder.SpecRow
                    {
                        WaterLevelHeight = (float)(level + 0.1 * c),
                        WaterLevelTrend = 2,
                    };
            return ($"20261014T{10 + i:00}0000Z", values);
        }).ToArray();

        S104FixtureBuilder.WriteTimeSeries(
            path,
            new S104FixtureBuilder.GridGeoreference(51.86, 4.30, 0.02, 0.03, rows, columns),
            steps,
            new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-104.2.0",
                ["horizontalCRS"] = 4326,
                ["issueDate"] = "20261014",
            });
        return path;
    }
}
