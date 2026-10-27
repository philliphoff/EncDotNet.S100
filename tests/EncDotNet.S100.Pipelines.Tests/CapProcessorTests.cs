using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Cap;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.TestSupport;
using Mapsui;
using static EncDotNet.S100.TestSupport.CapFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1069: CAP files through the registry and the host paths —
/// detection (any extension, never clashing with S-124 or other XML and
/// GeoJSON), the processor's drawing at the view time, picks, hit tests,
/// headless rendering, metadata, the catalog payload, and the Mapsui session
/// the viewer drives.
/// </summary>
public sealed class CapProcessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cap-pipeline-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 2026-08-28 15:30Z: the Delaware Bay Special Marine Warning is in force,
    // the Santa Barbara Channel Small Craft Advisory not yet.
    private static readonly DateTime DuringSmw = new(2026, 8, 28, 15, 30, 0, DateTimeKind.Utc);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    // -----------------------------------------------------------------
    // Detection
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(GaleWarning, "alert.xml")]
    [InlineData(GaleWarning, "alert.cap")]
    [InlineData(GaleWarning, "urn-oid-2.49.0.1.840.0.1b8c16")]
    [InlineData(MarineWeatherStatement, "mws.XML")]
    [InlineData(GeoJson, "active.geojson")]
    [InlineData(GeoJson, "active.json")]
    [InlineData(GeoJson, "active")]
    [InlineData(Atom, "alerts.atom")]
    [InlineData(Atom, "alerts.xml")]
    [InlineData(Circle, "circle.xml")]
    [InlineData(Circle, "circle.gml")]
    public void A_CAP_file_is_detected_whatever_its_extension(string fixture, string name)
    {
        CopyFixture(fixture, PathFor(name));

        Assert.Equal("CAP", DatasetPipelineFactory.DetectProductSpec(PathFor(name)));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Equal("CAP", DatasetPipelineFactory.DetectProductSpecFromSource(source, name));
    }

    [Fact]
    public void The_CAP_matchers_claim_no_S100_or_weather_file()
    {
        string[] others =
        [
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_point.gml"),
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_ccg_ed2.gml"),
            Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000"),
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "102*.h5")[0],
            GribFixtures.Fixture(GribFixtures.Wind10m),
        ];

        foreach (var path in others)
        {
            var bytes = File.ReadAllBytes(path);
            var header = new DatasetHeader { LeadingBytes = bytes.AsMemory(0, Math.Min(bytes.Length, DatasetHeader.MaxLength)), Extension = Path.GetExtension(path) };
            Assert.False(WeatherProducts.Cap.MatchBytes!(header), path);
            Assert.NotEqual("CAP", DatasetPipelineFactory.DetectProductSpec(path));
        }

        Assert.Equal("S-124", DatasetPipelineFactory.DetectProductSpec(others[0]));
        Assert.Equal("S-124", DatasetPipelineFactory.DetectProductSpec(others[1]));
        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(others[4]));
    }

    [Fact]
    public void An_S124_dataset_named_xml_keeps_its_product_through_an_exchange_set()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_point.gml"), PathFor("warning.xml"));
        using var source = FileSystemAssetSource.Create(_dir);

        Assert.Equal("S-124", DatasetPipelineFactory.DetectProductSpecFromSource(source, "warning.xml"));
    }

    [Theory]
    [InlineData("other.geojson", """{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"event":"Gale Warning"},"geometry":null}]}""")]
    [InlineData("zone.json", """{"@context":{"@vocab":"https://api.weather.gov/ontology#"},"id":"https://api.weather.gov/zones/forecast/ANZ535","type":"Feature"}""")]
    [InlineData("news.atom", """<?xml version="1.0"?><feed xmlns="http://www.w3.org/2005/Atom"><title>News</title></feed>""")]
    [InlineData("other.xml", """<?xml version="1.0"?><alert><identifier>x</identifier></alert>""")]
    public void Other_XML_and_GeoJSON_files_are_not_CAP(string name, string text)
    {
        File.WriteAllText(PathFor(name), text);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(PathFor(name)));
    }

    [Fact]
    public void A_registry_without_the_weather_products_does_not_detect_CAP()
    {
        var path = CopyFixture(GaleWarning, PathFor("alert.xml"));

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, new ProductRegistry().AddAllS100Products()));
    }

    [Fact]
    public void CAP_is_registered_as_a_non_S100_product_drawn_in_code()
    {
        var registry = ProductRegistry.Default;

        Assert.True(registry.IsRegistered("CAP"));
        Assert.False(registry.IsS100("CAP"));
        Assert.Equal("SoundCharts portrayal: CAP 1.2 defines no symbology", registry.PortrayalOf("CAP").AsApplication?.Note);
        Assert.Equal(S98DisplayPlane.CautionsAndWarnings, registry.DisplayPlanes.GetDefaultPlane("CAP"));
        Assert.True(registry.TryResolve("cap", out var registration));
        Assert.Equal(CapDatasetProcessor.NotForNavigationNotice, registration.PickReportNotice);
    }

    // -----------------------------------------------------------------
    // The processor
    // -----------------------------------------------------------------

    [Fact]
    public void The_factory_builds_a_CAP_processor_that_reports_CAP_1_2()
    {
        var processor = Assert.IsType<CapDatasetProcessor>(CreateFactory().CreateProcessor(CopyFixture(GeoJson, PathFor("active.json"))));

        Assert.Equal("CAP/1.2.0", processor.Spec.ToString());
        Assert.Equal("active.json", processor.FileName);
        Assert.Empty(((IDatasetProcessor)processor).GetNavigationalWarnings());
        Assert.Equal(2, processor.EnumerateFeatures().Count());
        Assert.All(processor.EnumerateFeatures(), f => Assert.Equal(CapDatasetProcessor.FeatureType, f.FeatureType));
    }

    [Fact]
    public void Its_time_steps_are_when_warnings_start_and_end_held_until_the_next()
    {
        var processor = new CapDatasetProcessor(Fixture(GeoJson));

        Assert.Equal(processor.Warnings.Changes, processor.AvailableTimes);
        Assert.Equal(TimeStepSelection.AtOrBefore, processor.TimeSemantics.Selection);
        // The longest gap, 22:00 to just after 10:00 the next day, so the
        // dataset never hides between steps.
        Assert.Equal(TimeSpan.FromHours(12) + TimeSpan.FromTicks(1), processor.TimeSemantics.Validity);
    }

    [Fact]
    public async Task Only_the_warnings_in_force_at_the_time_step_are_drawn()
    {
        var processor = new CapDatasetProcessor(Fixture(GeoJson));

        async Task<string[]> Drawn(DateTime? time)
        {
            var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = time }, Ct);
            return result.SubLayers.Single().Instructions.Select(i => i.FeatureReference).Distinct().ToArray();
        }

        Assert.Empty(await Drawn(new DateTime(2026, 8, 28, 15, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(["alert1.info1.area1"], await Drawn(DuringSmw));
        Assert.Empty(await Drawn(new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(["alert2.info1.area1"], await Drawn(new DateTime(2026, 8, 29, 2, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(["alert1.info1.area1", "alert2.info1.area1"], await Drawn(null));
    }

    [Fact]
    public async Task The_sub_layer_sits_on_the_cautions_and_warnings_plane_after_S124()
    {
        var processor = new CapDatasetProcessor(Fixture(GeoJson));

        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = DuringSmw }, Ct);

        var layer = Assert.Single(result.SubLayers);
        Assert.Equal(S98DisplayPlane.CautionsAndWarnings, layer.Plane);
        Assert.Equal(CapDatasetProcessor.WithinPlanePriority, layer.WithinPlanePriority);
        Assert.True(layer.WithinPlanePriority > 0);
        Assert.Equal(CapDatasetProcessor.LayerKey, layer.LayerKey);
        Assert.Equal("CAP", result.Product);
        Assert.StartsWith("ds:", result.ContentScope);
        Assert.Contains("app:CAP@", result.ContentScope);
        Assert.NotNull(result.GeometryProvider.GetGeometry("alert1.info1.area1"));
        Assert.NotNull(result.SymbolProvider!("CAPHATCHSEV"));
        Assert.Equal("CAPHATCHSEV", result.AreaFillProvider!("CAPHATCHSEV")!.PatternSymbol);
        Assert.Contains("1 of 2 warning areas in force", result.Info);
        Assert.True(result.Palette.Colors.ContainsKey("CAPSEV"));
    }

    [Fact]
    public async Task A_zone_coded_area_draws_the_zones_outline()
    {
        var processor = new CapDatasetProcessor(Fixture(GaleWarning));

        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = CapFixtures.GaleOnset.UtcDateTime }, Ct);

        var geometry = result.GeometryProvider.GetGeometry("alert1.info1.area1");
        Assert.NotNull(geometry);
        Assert.Equal(GeometryType.Surface, geometry.Type);
        Assert.Equal(MarineZoneLayer.Default.Find("PZZ560")!.Surfaces[0].ExteriorRing, geometry.Coordinates);
    }

    [Fact]
    public void A_pick_reports_the_warning()
    {
        var processor = new CapDatasetProcessor(Fixture(GeoJson));

        var info = processor.GetFeatureInfoAt(0);

        Assert.NotNull(info);
        Assert.Equal("alert1.info1.area1", info.FeatureRef);
        Assert.Equal("Special Marine Warning", info.FeatureTypeName);
        string Value(string code) => info.Attributes.Single(a => a.Code == code).DisplayText;
        Assert.Equal("notice", info.Attributes[0].Code);
        Assert.Equal(CapDatasetProcessor.NotForNavigationNotice, Value("notice"));
        Assert.StartsWith("Special Marine Warning issued August 28", Value("headline"));
        Assert.Equal("Special Marine Warning", Value("event"));
        Assert.Equal("Severe", Value("severity"));
        Assert.Equal("Immediate", Value("urgency"));
        Assert.Equal("Likely", Value("certainty"));
        Assert.Equal("2026-08-28 15:12 UTC", Value("onset"));
        Assert.Equal(new DateTime(2026, 8, 28, 16, 15, 0, DateTimeKind.Utc), info.Attributes.Single(a => a.Code == "expires").DateTimeValue);
        Assert.Equal("NWS Mount Holly NJ (w-nws.webmaster@noaa.gov)", Value("sender"));
        Assert.StartsWith("SMWPHI", Value("description"));
        Assert.Equal("Move to safe harbor until hazardous weather passes.", Value("instruction"));
        Assert.Equal("urn:oid:2.49.0.1.840.0.a8794110aadd6082e6ee53e622a4841194229935.001.1", Value("identifier"));
        Assert.Equal(info.FeatureRef, processor.GetFeatureInfo("alert1.info1.area1")!.FeatureRef);
    }

    [Fact]
    public void A_pick_lists_the_zones_and_the_codes_not_found()
    {
        var processor = new CapDatasetProcessor(Fixture(Circle));

        var zoned = processor.GetFeatureInfoAt(1)!;

        Assert.Equal("ANZ450", zoned.Attributes.Single(a => a.Code == "zones").DisplayText);
        Assert.Equal("ANZ999", zoned.Attributes.Single(a => a.Code == "unmatchedZones").DisplayText);
        Assert.DoesNotContain(processor.GetFeatureInfoAt(0)!.Attributes, a => a.Code is "zones" or "unmatchedZones");
        Assert.Equal(["ANZ999"], processor.Warnings.UnmatchedZoneCodes);
    }

    [Fact]
    public async Task A_hit_test_finds_only_what_the_last_render_drew()
    {
        var processor = new CapDatasetProcessor(Fixture(GeoJson));

        // Inside the Delaware Bay polygon.
        Assert.Single(processor.HitTestFeatures(39.1, -75.1, 0));

        await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc) }, Ct);
        Assert.Empty(processor.HitTestFeatures(39.1, -75.1, 0));

        await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = DuringSmw }, Ct);
        Assert.Equal("alert1.info1.area1", Assert.Single(processor.HitTestFeatures(39.1, -75.1, 0)).FeatureRef);
        Assert.Empty(processor.HitTestFeatures(30, -60, 0));
    }

    [Fact]
    public async Task It_renders_headless_and_draws_nothing_when_nothing_is_in_force()
    {
        var processor = new CapDatasetProcessor(Fixture(Circle));

        using var drawn = await processor.RenderHeadlessAsync(320, 320, new RenderContext { TimeStep = new DateTime(2026, 8, 28, 16, 0, 0, DateTimeKind.Utc) }, cancellationToken: Ct);
        using var empty = await processor.RenderHeadlessAsync(320, 320, new RenderContext { TimeStep = new DateTime(2026, 8, 28, 19, 0, 0, DateTimeKind.Utc) }, cancellationToken: Ct);

        Assert.True(NonWhite(drawn) > 2000, $"{NonWhite(drawn)} pixels drawn");
        Assert.Equal(0, NonWhite(empty));
    }

    [Fact]
    public void Its_metadata_is_the_areas_extent_and_the_warnings_time()
    {
        var processor = new CapDatasetProcessor(Fixture(GeoJson));

        var metadata = processor.Metadata;

        Assert.Equal("CAP", metadata.Spec.Name);
        Assert.Equal(4326, metadata.HorizontalCrsEpsg);
        Assert.Equal(new DateTime(2026, 8, 28, 15, 12, 0, DateTimeKind.Utc), metadata.TimeCoverage!.Value.Start);
        Assert.Equal(new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc), metadata.TimeCoverage.Value.End);
        // Delaware Bay to the Santa Barbara Channel.
        Assert.InRange(metadata.Extent!.NorthLatitude, 39.2, 39.3);
        Assert.InRange(metadata.Extent.WestLongitude, -121, -119);
        Assert.Equal(metadata, WeatherProducts.Cap.ReadMetadata!(Fixture(GeoJson)) with { });
    }

    [Fact]
    public void The_catalog_payload_describes_itself()
    {
        var processor = new CapDatasetProcessor(Fixture(Atom));

        var fromProcessor = LoadedDatasetProjector.Project(new DatasetId("atom"), processor);
        LoadedDataset? fromStream;
        using (var stream = File.OpenRead(Fixture(Atom)))
            fromStream = LoadedDatasetProjector.Project(new DatasetId("atom"), "CAP", stream);

        Assert.NotNull(fromProcessor);
        Assert.NotNull(fromStream);
        Assert.Equal(processor.Metadata.Spec, fromProcessor.Spec);
        Assert.Equal(fromProcessor.Spec, fromStream.Spec);
        var data = Assert.IsType<CapDatasetData>(fromStream.Data);
        Assert.Equal(3, data.Warnings.Areas.Count);
    }

    [Fact]
    public void A_file_that_is_not_CAP_fails_naming_the_file()
    {
        File.WriteAllText(PathFor("broken.xml"), "<alert xmlns=\"urn:oasis:names:tc:emergency:cap:1.2\"><identifier>x");

        var error = Assert.Throws<CapFormatException>(() => new CapDatasetProcessor(PathFor("broken.xml")));

        Assert.StartsWith("broken.xml: ", error.Message);
        // The file was closed.
        File.Delete(PathFor("broken.xml"));
    }

    // -----------------------------------------------------------------
    // The Mapsui session (the viewer's load path)
    // -----------------------------------------------------------------

    [Fact]
    public async Task The_session_loads_steps_and_picks_a_CAP_file()
    {
        var path = CopyFixture(GeoJson, PathFor("active.json"));
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });

        var processor = Assert.IsType<CapDatasetProcessor>(CreateFactory().CreateProcessorWithFilesystemUpdates(path));
        var id = new MapDatasetId("active.json");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "active.json", processor.Metadata), processor, cancellationToken: Ct));

        var entry = Assert.Single(s100.Session.GetLayerStackEntries());
        Assert.Equal(S98DisplayPlane.CautionsAndWarnings, entry.Item.Plane);

        var timed = Assert.Single(s100.GetTimeSnapshot().Datasets);
        Assert.Equal(MapsuiTimeSelectionKind.AtOrBefore, timed.Selection);

        await s100.SetTimeAsync(DuringSmw, Ct);
        var pick = Assert.Single(await s100.Query.PickAsync(new GeographicPickQuery { Latitude = 39.1, Longitude = -75.1 }, Ct));
        Assert.Equal("Special Marine Warning", pick.Info.Attributes.Single(a => a.Code == "event").DisplayValue);

        // Past the warning's end: nothing to pick.
        await s100.SetTimeAsync(new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc), Ct);
        Assert.Empty(await s100.Query.PickAsync(new GeographicPickQuery { Latitude = 39.1, Longitude = -75.1 }, Ct));
    }

    private static int NonWhite(SkiaSharp.SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SkiaSharp.SKColors.White)
                    count++;
            }
        }

        return count;
    }

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
}
