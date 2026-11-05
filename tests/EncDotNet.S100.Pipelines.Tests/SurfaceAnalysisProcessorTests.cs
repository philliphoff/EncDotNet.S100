using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.TestSupport;
using static EncDotNet.S100.TestSupport.CodsusFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1295: WPC coded surface bulletins through the registry — detection
/// whatever the extension (never claiming an S-100 or other weather file),
/// the <c>CODSUS</c> registration, and the processor's features and metadata.
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
    public void A_pick_reports_the_notice_valid_time_and_the_bulletins_points()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(HighResolution));

        var front = processor.GetFeatureInfo("a0.front0")!;
        Assert.Equal("Cold front", front.FeatureTypeName);
        Assert.Equal(
            ["notice", "validTime", "code", "points"],
            front.Attributes.Select(a => a.Code));
        Assert.Equal(SurfaceAnalysisDatasetProcessor.NotForNavigationNotice, front.Attributes[0].RawValue);
        Assert.Equal(new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), front.Attributes[1].DateTimeValue);
        Assert.StartsWith("41.7°N 93.1°W, 40.6°N 92.2°W", front.Attributes[3].RawValue, StringComparison.Ordinal);

        var centre = processor.EnumerateFeatures().First(f => f.FeatureType == SurfaceAnalysisDatasetProcessor.PressureCentreFeatureType);
        var info = processor.GetFeatureInfoAt(centre.Ordinal)!;
        Assert.Equal("High 1026 hPa", info.FeatureTypeName);
        Assert.Equal("50.1°N 116.2°W", info.Attributes.Single(a => a.Code == "position").RawValue);
        Assert.Null(processor.GetFeatureInfoAt(-1));
        Assert.Null(processor.GetFeatureInfo("nope"));
    }

    [Fact]
    public void A_forecast_pick_reports_its_lead_time_and_a_front_its_intensity()
    {
        var forecast = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));
        var day = forecast.EnumerateFeatures().First(f => f.FeatureRef.StartsWith("a1.", StringComparison.Ordinal));
        Assert.Equal("24 h", forecast.GetFeatureInfoAt(day.Ordinal)!.Attributes.Single(a => a.Code == "forecastHours").RawValue);

        var synthetic = new SurfaceAnalysisDatasetProcessor(Fixture(Antimeridian));
        Assert.Equal("Strong", synthetic.GetFeatureInfo("a0.front0")!.Attributes.Single(a => a.Code == "strength").RawValue);
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
