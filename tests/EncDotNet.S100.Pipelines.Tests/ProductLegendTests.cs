using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The legend rows the S-100 product processors put on their sub-layers
/// (#1406, design note legends.md A10): S-102 depth shades from the Mariner
/// settings, S-104 isoline interval and gauges, S-111 speed bands from
/// <c>select_arrow.xsl</c>, the S-411 WMO ice colours and the S-124 key. Each is
/// built in the palette drawn with.
/// </summary>
public sealed class ProductLegendTests
{
    private static readonly ProjNetCrsTransformFactory Transforms = new();

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

    // ---- S-102 ----------------------------------------------------------------

    private static async Task<CoveragePortrayalResult> S102Async(MarinerSettings mariner, PaletteType palette)
    {
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor("TestData/102US004MI1CI262227.h5", manager, TestLuaEngine.Create(), Transforms);
        return await processor.BuildCoveragePortrayalAsync(
            new RenderContext { Mariner = mariner, Palette = palette }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task S102_depth_shades_follow_the_mariner_contours_shallowest_first()
    {
        var mariner = MarinerSettings.Default with
        {
            FourShades = true,
            ShallowContour = Depth.FromMetres(2),
            SafetyContour = Depth.FromMetres(10),
            DeepContour = Depth.FromMetres(30),
        };

        var result = await S102Async(mariner, PaletteType.Day);

        var surface = result.SubLayers.OfType<GridCoverageSubLayer>().Single();
        Assert.Equal(
            [
                "Intertidal, < 0 m",
                "Shallow Water, 0–2 m",
                "Medium-Shallow Water, 2–10 m",
                "Medium-Deep Water, 10–30 m",
                "Deep Water, ≥ 30 m",
            ],
            surface.Legend!.Select(e => e.Label));
        Assert.All(surface.Legend!, e => Assert.IsType<SwatchLegendEntry>(e));
        Assert.Equal("Safety contour 10 m", surface.LegendCaption);

        // The swatches are the colours the bands are drawn with.
        Assert.Equal(
            surface.Coverage.ColorScheme!.Bands.Select(b => b.Color),
            surface.Legend!.Cast<SwatchLegendEntry>().Select(e => e.Color));
    }

    [Fact]
    public async Task S102_two_shades_and_the_depth_lines_have_rows()
    {
        var mariner = MarinerSettings.Default with { FourShades = false, SafetyContour = Depth.FromMetres(5) };

        var result = await S102Async(mariner, PaletteType.Day);

        var surface = result.SubLayers.OfType<GridCoverageSubLayer>().Single();
        Assert.Equal(["Intertidal, < 0 m", "Shallow Water, 0–5 m", "Deep Water, ≥ 5 m"], surface.Legend!.Select(e => e.Label));

        var safety = result.SubLayers.Single(l => l.LayerKey == S102DatasetProcessor.SafetyContourLayerKey);
        var safetyRow = Assert.IsType<LineLegendEntry>(Assert.Single(safety.Legend!));
        Assert.Equal("Safety contour", safetyRow.Label);
        Assert.Equal(0.64, safetyRow.Line.Width, 6);
        Assert.Empty(safetyRow.Line.Dashes);

        var limit = result.SubLayers.Single(l => l.LayerKey == S102DatasetProcessor.CoverageLimitLayerKey);
        var limitRow = Assert.IsType<LineLegendEntry>(Assert.Single(limit.Legend!));
        Assert.Equal("Limit of S-102 data", limitRow.Label);
        Assert.NotEmpty(limitRow.Line.Dashes);
    }

    [Fact]
    public async Task S102_rows_are_rebuilt_for_the_palette()
    {
        var day = await S102Async(MarinerSettings.Default, PaletteType.Day);
        var night = await S102Async(MarinerSettings.Default, PaletteType.Night);

        static IEnumerable<string> Colours(CoveragePortrayalResult r) =>
            r.SubLayers.OfType<GridCoverageSubLayer>().Single().Legend!.Cast<SwatchLegendEntry>().Select(e => e.Color);

        Assert.Equal(
            day.SubLayers.OfType<GridCoverageSubLayer>().Single().Legend!.Select(e => e.Label),
            night.SubLayers.OfType<GridCoverageSubLayer>().Single().Legend!.Select(e => e.Label));
        Assert.NotEqual(Colours(day), Colours(night));
    }

    // ---- S-104 ----------------------------------------------------------------

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task S104_isolines_have_a_row_with_the_dataset_interval(PaletteType palette)
    {
        // A 0.4 m range selects the fine 0.25 m interval.
        var path = WriteS104Grid();
        try
        {
            var result = await new S104DatasetProcessor(path, Transforms).BuildCoveragePortrayalAsync(
                new RenderContext { Palette = palette }, TestContext.Current.CancellationToken);

            var isolines = result.SubLayers.OfType<IsolineCoverageSubLayer>().Single();
            var row = Assert.IsType<LineLegendEntry>(Assert.Single(isolines.Legend!));
            Assert.Equal("Water level, every 0.25 m", row.Label);
            Assert.Equal(isolines.Ink.ToHex(), row.Line.Color);
            Assert.Equal(S104CoverageStyle.Palette.Resolve(S104CoverageStyle.InkToken, palette).ToHex(), row.Line.Color);
            Assert.NotEmpty(row.Line.Dashes);
            Assert.Equal(S104CoverageStyle.LegendCaption, isolines.LegendCaption);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void S104_station_rows_are_the_gauge_symbols_in_the_palette()
    {
        var day = S104CoverageStyle.BuildStationLegend(PaletteType.Day);
        var night = S104CoverageStyle.BuildStationLegend(PaletteType.Night);

        Assert.Equal(["Rising", "Falling", "Steady", "Trend unknown", "No value at this time"], day.Select(e => e.Label));
        Assert.All(day, e => Assert.IsType<SymbolLegendEntry>(e));
        Assert.NotEqual(((SymbolLegendEntry)day[0]).Svg, ((SymbolLegendEntry)night[0]).Svg);
    }

    // ---- S-111 ----------------------------------------------------------------

    private static async Task<ArrowCoverageSubLayer> S111ArrowsAsync(PaletteType palette)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        var values = Enumerable.Range(0, 16)
            .Select(i => new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 0.5f * i, SurfaceCurrentDirection = 45f })
            .ToArray();
        S111FixtureBuilder.WriteFile(path, values, 4, 4, useF64GridAttrs: true, useUnsignedCounts: false);
        try
        {
            using var manager = S111TestCatalogues.Create();
            using var processor = new S111DatasetProcessor(path, manager, Transforms);
            var result = await processor.BuildCoveragePortrayalAsync(
                new RenderContext { Palette = palette }, TestContext.Current.CancellationToken);
            return result.SubLayers.OfType<ArrowCoverageSubLayer>().Single();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task S111_speed_bands_come_from_select_arrow_slowest_first()
    {
        var arrows = await S111ArrowsAsync(PaletteType.Day);

        Assert.Equal(9, arrows.Legend!.Count);
        Assert.Equal("0–0.5 kn", arrows.Legend![0].Label);
        Assert.Equal("> 13 kn", arrows.Legend![^1].Label);
        Assert.All(arrows.Legend!, e => Assert.IsType<SymbolLegendEntry>(e));
        Assert.Equal("Speed, kn · longer arrow = faster", arrows.LegendCaption);
    }

    [Fact]
    public async Task S111_arrows_are_drawn_in_the_palette()
    {
        var day = await S111ArrowsAsync(PaletteType.Day);
        var night = await S111ArrowsAsync(PaletteType.Night);

        Assert.Equal(day.Legend!.Select(e => e.Label), night.Legend!.Select(e => e.Label));
        Assert.NotEqual(
            day.Legend!.Cast<SymbolLegendEntry>().Select(e => e.Svg),
            night.Legend!.Cast<SymbolLegendEntry>().Select(e => e.Svg));
    }

    // ---- S-411 ----------------------------------------------------------------

    private static async Task<VectorSubLayer> S411Async(string? modeId, PaletteType palette = PaletteType.Day)
    {
        var processor = new S411DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes.gml"),
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext { DisplayModeId = modeId, Palette = palette }, TestContext.Current.CancellationToken);
        return Assert.Single(result.SubLayers);
    }

    [Fact]
    public async Task S411_concentration_key_is_the_WMO_colour_code_labelled_from_the_catalogue()
    {
        var layer = await S411Async(S411DisplayModes.ConcentrationModeId);

        Assert.Equal(
            [
                "Ice Free",
                "Open Water ( < 1/10 ice), Bergy Water",
                "1/10 to 3/10 ice",
                "2/10 to 6/10 ice",
                "5/10 to 8/10 ice",
                "7/10 to 10/10 ice",
                "10/10 ice",
            ],
            layer.Legend!.Select(e => e.Label));
        var swatches = layer.Legend!.Cast<SwatchLegendEntry>().ToList();
        Assert.Equal(S411IceColours.Concentration.Select(c => c.Color[1..]), swatches.Select(s => s.Color[3..]));
        Assert.All(swatches, s => Assert.Equal(9, s.Color.Length)); // #AARRGGBB: the fill's transparency
        Assert.Equal("Total concentration (WMO colour code)", layer.LegendCaption);
    }

    [Fact]
    public async Task S411_stage_of_development_key_follows_the_display_mode()
    {
        var layer = await S411Async(S411DisplayModes.StageOfDevelopmentModeId);

        Assert.Equal(S411IceColours.StageOfDevelopment.Count, layer.Legend!.Count);
        Assert.Equal("Ice Free, Brash Ice, No stage of development", layer.Legend![0].Label);
        Assert.Contains(layer.Legend!, e => e.Label == "Multi-Year Ice");
        Assert.Equal("Stage of development (WMO colour code)", layer.LegendCaption);
    }

    [Fact]
    public async Task S411_navigational_preview_has_no_key()
    {
        var layer = await S411Async(S411DisplayModes.NavigationalModeId);

        Assert.Null(layer.Legend);
        Assert.Null(layer.LegendCaption);
    }

    [Fact]
    public async Task S411_colours_are_the_same_in_every_palette()
    {
        // The S-411 catalogue's WMO colours are literal RGB, not palette tokens.
        var day = await S411Async(null, PaletteType.Day);
        var night = await S411Async(null, PaletteType.Night);

        Assert.Equal(
            day.Legend!.Cast<SwatchLegendEntry>().Select(e => (e.Label, e.Color)),
            night.Legend!.Cast<SwatchLegendEntry>().Select(e => (e.Label, e.Color)));
    }

    // ---- S-124 ----------------------------------------------------------------

    private static async Task<VectorSubLayer> S124Async(string fileName, PaletteType palette)
    {
        var processor = new S124DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", fileName),
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext { Palette = palette }, TestContext.Current.CancellationToken);
        return Assert.Single(result.SubLayers);
    }

    [Fact]
    public async Task S124_key_lists_the_marks_drawn_in_catalogue_order()
    {
        var layer = await S124Async("navwarn_drawing_parts.gml", PaletteType.Day);

        var labels = layer.Legend!.Select(e => e.Label).ToList();
        string[] order = ["Warning area", "Warning line or restricted area", "Warning position", "Warning area mark", "Approximate position"];
        Assert.Contains("Warning area", labels);
        Assert.Equal(order.Where(labels.Contains), labels);

        var area = Assert.IsType<SwatchLegendEntry>(layer.Legend![0]);
        Assert.NotNull(area.Outline);
        Assert.NotEmpty(area.Outline!.Dashes);
        Assert.Equal(0.64, area.Outline.Width, 6);
    }

    [Fact]
    public async Task S124_point_warnings_have_only_point_rows()
    {
        var layer = await S124Async("navwarn_point.gml", PaletteType.Day);

        Assert.Contains(layer.Legend!, e => e is SymbolLegendEntry { Label: "Warning position" });
        Assert.DoesNotContain(layer.Legend!, e => e.Label == "Warning area");
    }

    [Fact]
    public async Task S124_rows_are_rebuilt_for_the_palette()
    {
        var day = await S124Async("navwarn_drawing_parts.gml", PaletteType.Day);
        var night = await S124Async("navwarn_drawing_parts.gml", PaletteType.Night);

        var dayArea = (SwatchLegendEntry)day.Legend![0];
        var nightArea = (SwatchLegendEntry)night.Legend![0];
        Assert.NotEqual(dayArea.Outline!.Color, nightArea.Outline!.Color);
    }

    private static string WriteS104Grid()
    {
        const int rows = 4, columns = 5;
        var path = Path.GetTempFileName() + ".h5";
        var values = new S104FixtureBuilder.SpecRow[rows * columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                values[r * columns + c] = new S104FixtureBuilder.SpecRow { WaterLevelHeight = (float)(0.5 + 0.1 * c), WaterLevelTrend = 2 };

        S104FixtureBuilder.WriteTimeSeries(
            path,
            new S104FixtureBuilder.GridGeoreference(51.86, 4.30, 0.02, 0.03, rows, columns),
            [("20261014T100000Z", values)],
            new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-104.2.0",
                ["horizontalCRS"] = 4326,
                ["issueDate"] = "20261014",
            });
        return path;
    }
}
