using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.TestSupport;
using static EncDotNet.S100.TestSupport.CodsusFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issues #1295 and #1300: WPC coded surface bulletins through the registry — detection
/// whatever the extension (never claiming an S-100 or other weather file),
/// the <c>CODSUS</c> registration, and the processor's features, metadata,
/// portrayal, picks and hit tests.
/// </summary>
public sealed class SurfaceAnalysisProcessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("codsus-pipeline-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Theory]
    [InlineData(HighResolution, "codsus_hr_2026110400.txt")]
    [InlineData(HighResolution, "asus02.kwbc.cod.sus")]
    [InlineData(Standard, "CODSUS")]
    [InlineData(Forecast, "fsus02.kwbc.cod.srp.txt")]
    [InlineData(Antimeridian, "bulletin.TXT")]
    public void A_bulletin_is_detected_whatever_its_extension(string fixture, string name)
    {
        CopyFixture(fixture, PathFor(name));

        Assert.Equal("CODSUS", DatasetPipelineFactory.DetectProductSpec(PathFor(name)));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Equal("CODSUS", DatasetPipelineFactory.DetectProductSpecFromSource(source, name));
    }

    [Fact]
    public void The_matcher_claims_no_S100_or_other_weather_file()
    {
        string[] others =
        [
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_point.gml"),
            Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000"),
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "102*.h5")[0],
            GribFixtures.Fixture(GribFixtures.Wind10m),
            CapFixtures.Fixture(CapFixtures.GaleWarning),
            CapFixtures.Fixture(CapFixtures.GeoJson),
        ];

        foreach (var path in others)
        {
            var bytes = File.ReadAllBytes(path);
            var header = new DatasetHeader { LeadingBytes = bytes.AsMemory(0, Math.Min(bytes.Length, DatasetHeader.MaxLength)), Extension = Path.GetExtension(path) };
            Assert.False(WeatherProducts.Codsus.MatchBytes!(header), path);
            Assert.NotEqual("CODSUS", DatasetPipelineFactory.DetectProductSpec(path));
        }
    }

    [Fact]
    public void Other_text_files_are_not_bulletins()
    {
        File.WriteAllText(PathFor("notes.txt"), "VALID FROM 0400Z\nThe forecast is fair.\n");

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(PathFor("notes.txt")));
    }

    [Fact]
    public void A_registry_without_the_weather_products_does_not_detect_a_bulletin()
    {
        var path = CopyFixture(HighResolution, PathFor("codsus.txt"));

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, new ProductRegistry().AddAllS100Products()));
    }

    [Fact]
    public void CODSUS_is_registered_as_a_non_S100_product_drawn_in_code()
    {
        var registry = ProductRegistry.Default;

        Assert.True(registry.IsRegistered("CODSUS"));
        Assert.False(registry.IsS100("CODSUS"));
        Assert.Equal("SoundCharts portrayal: WPC's coded surface bulletin defines no symbology", registry.PortrayalOf("CODSUS").AsApplication?.Note);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, registry.DisplayPlanes.GetDefaultPlane("CODSUS"));
        Assert.True(registry.TryResolve("codsus", out var registration));
        Assert.Equal(SurfaceAnalysisDatasetProcessor.NotForNavigationNotice, registration.PickReportNotice);
        Assert.Equal("CODSUS", SpecName.Normalize("codsus"));
    }

    [Fact]
    public void The_factory_builds_a_processor_whose_features_are_the_fronts_and_centres()
    {
        var processor = Assert.IsType<SurfaceAnalysisDatasetProcessor>(CreateFactory().CreateProcessor(CopyFixture(HighResolution, PathFor("hr.txt"))));

        Assert.Equal("CODSUS", processor.Spec.Name);
        Assert.Equal("hr.txt", processor.FileName);
        var features = processor.EnumerateFeatures().ToArray();
        Assert.Equal(74 + 44, features.Length);
        Assert.Equal(74, features.Count(f => f.FeatureType == SurfaceAnalysisDatasetProcessor.FrontFeatureType));
        Assert.Equal(44, features.Count(f => f.FeatureType == SurfaceAnalysisDatasetProcessor.PressureCentreFeatureType));
        Assert.Equal("Cold front", features[0].FeatureTypeName);
        Assert.Equal(Enumerable.Range(0, features.Length), features.Select(f => f.Ordinal));
    }

    [Fact]
    public void Features_are_named_by_code_and_number_and_forecasts_by_lead_time()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(HighResolution));
        var refs = processor.EnumerateFeatures().Select(f => f.FeatureRef).ToArray();

        Assert.Equal("COLD 1", refs[0]);
        Assert.Equal("STNRY 1", refs[1]);
        Assert.Contains("HIGH 15", refs);
        Assert.Contains("LOW 29", refs);
        Assert.Equal(refs.Length, refs.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var forecast = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));
        var forecastRefs = forecast.EnumerateFeatures().Select(f => f.FeatureRef).ToArray();
        Assert.Contains("COLD 1", forecastRefs);
        Assert.Contains("+24h COLD 1", forecastRefs);
    }

    [Fact]
    public void A_pick_reports_the_notice_the_analysis_and_next_times_and_the_bulletins_points()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(HighResolution));

        var front = processor.GetFeatureInfo("COLD 1")!;
        Assert.Equal("Cold front", front.FeatureTypeName);
        Assert.Equal(
            ["notice", "analysis", "nextAnalysis", "points"],
            front.Attributes.Select(a => a.Code));
        Assert.True(front.Attributes[0].IsNavigationNotice);
        Assert.Equal("Weather analysis, not for navigation.", front.Attributes[0].RawValue);
        Assert.Equal(new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), front.Attributes[1].DateTimeValue);
        Assert.Equal(new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc), front.Attributes[2].DateTimeValue);
        var points = front.Attributes[3];
        Assert.StartsWith("41.7°N 93.1°W, 40.6°N 92.2°W", points.RawValue, StringComparison.Ordinal);
        Assert.Equal("9, from 41.7°N 93.1°W to 27.4°N 97.0°W", points.DisplayValue);

        var centre = processor.EnumerateFeatures().First(f => f.FeatureType == SurfaceAnalysisDatasetProcessor.PressureCentreFeatureType);
        var info = processor.GetFeatureInfoAt(centre.Ordinal)!;
        Assert.Equal("HIGH 1", info.FeatureRef);
        Assert.Equal("High 1026 hPa", info.FeatureTypeName);
        Assert.Equal("50.1°N 116.2°W", info.Attributes.Single(a => a.Code == "position").RawValue);
        Assert.Null(processor.GetFeatureInfoAt(-1));
        Assert.Null(processor.GetFeatureInfo("nope"));
    }

    [Fact]
    public void A_forecast_pick_reports_its_lead_time_and_a_front_its_intensity()
    {
        var forecast = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));
        var day = forecast.EnumerateFeatures().First(f => f.FeatureRef.StartsWith("+24h ", StringComparison.Ordinal));
        var attributes = forecast.GetFeatureInfoAt(day.Ordinal)!.Attributes;
        Assert.Equal("24 h", attributes.Single(a => a.Code == "forecastHours").RawValue);
        Assert.DoesNotContain(attributes, a => a.Code == "nextAnalysis");

        var synthetic = new SurfaceAnalysisDatasetProcessor(Fixture(Antimeridian));
        Assert.Equal("Strong", synthetic.GetFeatureInfo("WARM 1")!.Attributes.Single(a => a.Code == "strength").RawValue);
    }

    [Fact]
    public async Task The_portrayal_is_one_sub_layer_above_the_isobars_with_its_legend()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(AtlanticLow));

        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { Palette = PaletteType.Dusk }, TestContext.Current.CancellationToken);

        var sub = Assert.Single(result.SubLayers);
        Assert.Equal("weather.fronts", sub.LayerKey);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, sub.Plane);
        Assert.Equal(92, sub.WithinPlanePriority);
        Assert.True(sub.WithinPlanePriority > WeatherCoverageStyle.IsolinePriority);
        Assert.Equal(6, sub.Legend!.Count);
        Assert.Equal("CODSUS", result.Product);
        Assert.Equal(processor.FileName, result.SourceDatasetId);
        Assert.Contains("not for navigation", result.Info, StringComparison.Ordinal);
        Assert.StartsWith("ds:", result.ContentScope, StringComparison.Ordinal);
        Assert.NotNull(result.LineStyleProvider!(SurfaceAnalysisStyle.Stationary.Name));
        Assert.NotNull(result.SymbolProvider!(SurfaceAnalysisStyle.CentreSymbol));

        // Each front's line in its own style; each centre its letter and pressure.
        var lines = sub.Instructions.OfType<LineInstruction>().Where(l => l.LineStyleReference != LineInstruction.SimpleLineStyleReference).ToArray();
        Assert.Equal(
            [("OCFNT 1", "WXFRONT_OCCLUDED"), ("COLD 1", "WXFRONT_COLD"), ("WARM 1", "WXFRONT_WARM"), ("STNRY 1", "WXFRONT_STATIONARY"), ("TROF 1", "WXFRONT_TROUGH")],
            lines.Select(l => (l.FeatureReference, l.LineStyleReference)));
        Assert.Equal(["H", "1032", "L", "996"], sub.Instructions.OfType<TextInstruction>().Select(t => t.Text));

        // Fronts are drawn on the smoothed line, which runs the bulletin's way.
        var geometry = result.GeometryProvider.GetGeometry("COLD 1")!;
        Assert.Equal(GeometryType.Curve, geometry.Type);
        Assert.Equal(new GeoPosition(42.5, -45.5), geometry.Coordinates[0]);
        Assert.Equal(new GeoPosition(33.5, -61.0), geometry.Coordinates[^1]);
        Assert.True(geometry.Coordinates.Count > 6);
        Assert.Equal(GeometryType.Point, result.GeometryProvider.GetGeometry("LOW 1")!.Type);
    }

    [Fact]
    public async Task Only_the_first_analysis_is_drawn_and_hit()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));

        var result = await processor.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken);

        Assert.Same(processor.Bulletin.Analyses[0], processor.DrawnAnalysis);
        Assert.All(result.SubLayers[0].Instructions, i => Assert.DoesNotContain("+24h", i.FeatureReference, StringComparison.Ordinal));
        var later = processor.EnumerateFeatures().First(f => f.FeatureRef == "+24h COLD 1");
        var line = processor.GetFeatureGeometryAt(later.Ordinal)!.Curves[0];
        var middle = line[line.Count / 2];
        Assert.DoesNotContain(processor.HitTestFeatures(middle.Latitude, middle.Longitude, 2_000), h => h.FeatureRef == "+24h COLD 1");
    }

    [Fact]
    public void A_pick_on_the_drawn_line_or_centre_finds_it()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(AtlanticLow));
        var cold = processor.EnumerateFeatures().Single(f => f.FeatureRef == "COLD 1");
        var line = processor.GetFeatureGeometryAt(cold.Ordinal)!;
        Assert.Equal(S100GeometryType.Curve, line.Primitive);
        var onLine = line.Curves[0][line.Curves[0].Count / 2];

        Assert.Contains(processor.HitTestFeatures(onLine.Latitude, onLine.Longitude, 2_000), h => h.FeatureRef == "COLD 1");
        Assert.Contains(processor.HitTestFeatures(45.0, -50.0, 2_000), h => h.FeatureRef == "LOW 1");
        Assert.Empty(processor.HitTestFeatures(20.0, -20.0, 2_000));
    }

    [Fact]
    public async Task It_renders_headless()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(AtlanticLow));

        using var bitmap = await processor.RenderHeadlessAsync(400, 300, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((400, 300), (bitmap.Width, bitmap.Height));
        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                    ink++;
            }
        }

        Assert.True(ink > 1_000, $"Only {ink} dark pixels.");
    }

    [Fact]
    public void The_metadata_records_WPCs_domain_and_the_valid_times()
    {
        var metadata = SurfaceAnalysisDatasetProcessor.ReadMetadata(Fixture(Forecast));

        Assert.Equal("CODSUS", metadata.Spec.Name);
        Assert.Equal(CodedSurfaceBulletin.WpcDomain, metadata.Extent);
        Assert.Equal(4326, metadata.HorizontalCrsEpsg);
        Assert.Equal(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc), metadata.TimeCoverage!.Value.Start);
        Assert.Equal(new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), metadata.TimeCoverage!.Value.End);
        Assert.Same(ProductRegistry.Default.Resolve("CODSUS").ReadMetadata, WeatherProducts.Codsus.ReadMetadata);
    }

    [Fact]
    public void A_file_that_cant_be_decoded_is_named_in_the_error()
    {
        File.WriteAllText(PathFor("broken.txt"), "CODED SURFACE FRONTAL POSITIONS\n\nVALID 110400Z\nHIGHS 1026 501116\n");

        var ex = Assert.Throws<CodedSurfaceBulletinFormatException>(() => new SurfaceAnalysisDatasetProcessor(PathFor("broken.txt")));

        Assert.StartsWith("broken.txt: Line 4:", ex.Message, StringComparison.Ordinal);
    }

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            TestLuaEngine.Create(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
}
