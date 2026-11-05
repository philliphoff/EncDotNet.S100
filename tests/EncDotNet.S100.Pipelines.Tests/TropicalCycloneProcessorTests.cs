using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1294: NHC tropical cyclone advisories through the registry — the
/// <c>TC</c> format name, detection of a forecast zip or a storm's folder
/// (and not of the zips that go with it), the processor's metadata, the
/// source route with its sibling zips, and the catalog payload.
/// </summary>
public sealed class TropicalCycloneProcessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tc-pipeline-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void TC_is_registered_as_a_non_S100_product_drawn_in_code()
    {
        var registry = ProductRegistry.Default;

        Assert.Equal("TC", SpecName.Normalize("tc"));
        Assert.Contains("TC", SpecName.NonS100Formats);
        Assert.True(registry.IsRegistered("TC"));
        Assert.False(registry.IsS100("TC"));
        Assert.Contains(WeatherProducts.TropicalCyclone, WeatherProducts.All);
        Assert.Equal("SoundCharts portrayal: NHC tropical cyclone GIS data defines no symbology", registry.PortrayalOf("TC").AsApplication?.Note);
        Assert.True(registry.TryResolve("tc", out var registration));
        Assert.Equal(TropicalCycloneDatasetProcessor.NotForNavigationNotice, registration.PickReportNotice);
    }

    [Theory]
    [InlineData("al092022_5day_020.zip")]
    [InlineData("download.zip")]
    [InlineData("advisory")]
    public void A_forecast_zip_is_detected_whatever_its_name(string name)
    {
        File.Copy(TropicalCycloneFixtures.IanForecast, Path.Combine(_dir, name));

        Assert.Equal("TC", DatasetPipelineFactory.DetectProductSpec(Path.Combine(_dir, name)));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Equal("TC", DatasetPipelineFactory.DetectProductSpecFromSource(source, name));
    }

    [Fact]
    public void The_radii_and_best_track_zips_are_not_datasets_on_their_own()
    {
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(TropicalCycloneFixtures.IanWindRadii));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(TropicalCycloneFixtures.IanBestTrack));
    }

    [Fact]
    public void A_storm_folder_is_detected_and_reads_its_latest_advisory()
    {
        var folder = Path.Combine(_dir, "ian");
        TropicalCycloneFixtures.CopyIan(folder);

        Assert.Equal("TC", DatasetPipelineFactory.DetectProductSpec(folder));
        var processor = Assert.IsType<TropicalCycloneDatasetProcessor>(CreateFactory().CreateProcessor(folder));
        Assert.Equal("ian", processor.FileName);
        Assert.Equal("20", processor.Advisory.Advisory);
        Assert.NotEmpty(processor.Advisory.PastTrack);
    }

    [Fact]
    public void A_registry_without_the_weather_products_does_not_detect_TC()
    {
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(TropicalCycloneFixtures.IanForecast, new ProductRegistry().AddAllS100Products()));
    }

    [Fact]
    public void The_factory_builds_a_processor_that_reports_TC_and_the_advisory()
    {
        var forecast = TropicalCycloneFixtures.CopyIan(_dir);

        var processor = Assert.IsType<TropicalCycloneDatasetProcessor>(CreateFactory().CreateProcessor(forecast));

        Assert.Equal("TC/1.0.0", processor.Spec.ToString());
        Assert.Equal("al092022_5day_020.zip", processor.FileName);
        Assert.Equal("AL092022", processor.Advisory.Storm.AtcfId);
        Assert.Equal(3, processor.Advisory.Forecast[0].Radii.Count);
        Assert.NotEmpty(processor.Advisory.PastTrack);
        Assert.Null(processor.GetFeatureInfo("anything"));
    }

    [Fact]
    public void Its_metadata_is_the_advisory_extent_and_the_track_to_the_last_forecast_point()
    {
        var processor = new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast);

        var metadata = processor.Metadata;

        Assert.Equal("TC", metadata.Spec.Name);
        Assert.Equal(4326, metadata.HorizontalCrsEpsg);
        Assert.Equal(new DateTime(2022, 9, 22, 18, 0, 0, DateTimeKind.Utc), metadata.TimeCoverage!.Value.Start);
        Assert.Equal(new DateTime(2022, 10, 2, 0, 0, 0, DateTimeKind.Utc), metadata.TimeCoverage.Value.End);
        // The best track starts in the Caribbean; the cone reaches the Carolinas.
        Assert.InRange(metadata.Extent!.SouthLatitude, 12, 13);
        Assert.InRange(metadata.Extent.NorthLatitude, 38, 39);
        Assert.Equal(metadata, WeatherProducts.TropicalCyclone.ReadMetadata!(TropicalCycloneFixtures.IanForecast) with { });
    }

    [Fact]
    public void The_source_route_reads_the_zips_beside_the_forecast()
    {
        TropicalCycloneFixtures.CopyIan(Path.Combine(_dir, "storms"));
        using var source = FileSystemAssetSource.Create(_dir);

        var processor = new TropicalCycloneDatasetProcessor(source, "storms/al092022_5day_020.zip");

        Assert.Equal("al092022_5day_020.zip", processor.FileName);
        Assert.Equal(3, processor.Advisory.Forecast[0].Radii.Count);
        Assert.NotEmpty(processor.Advisory.PastTrack);
    }

    [Fact]
    public void The_source_route_reads_a_forecast_zip_alone()
    {
        File.Copy(TropicalCycloneFixtures.IanForecast, Path.Combine(_dir, "al092022_5day_020.zip"));
        using var source = FileSystemAssetSource.Create(_dir);

        var processor = new TropicalCycloneDatasetProcessor(source, "al092022_5day_020.zip");

        Assert.Equal(8, processor.Advisory.Forecast.Count);
        Assert.All(processor.Advisory.Forecast, p => Assert.Empty(p.Radii));
        Assert.Empty(processor.Advisory.PastTrack);
    }

    [Fact]
    public void The_catalog_payload_describes_itself()
    {
        var processor = new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast);

        var fromProcessor = LoadedDatasetProjector.Project(new DatasetId("ian"), processor);
        LoadedDataset? fromStream;
        using (var stream = File.OpenRead(TropicalCycloneFixtures.IanForecast))
            fromStream = LoadedDatasetProjector.Project(new DatasetId("ian"), "TC", stream);

        Assert.NotNull(fromProcessor);
        Assert.NotNull(fromStream);
        Assert.Equal(processor.Metadata.Spec, fromProcessor.Spec);
        Assert.Equal(fromProcessor.Spec, fromStream.Spec);
        var data = Assert.IsType<TropicalCycloneDatasetData>(fromStream.Data);
        Assert.Equal("Ian", data.Advisory.Storm.Name);
        // A file stream's sibling zips are read too.
        Assert.NotEmpty(data.Advisory.PastTrack);
    }

    [Fact]
    public void A_file_that_is_not_an_advisory_fails_naming_the_file()
    {
        var path = Path.Combine(_dir, "al092022_5day_021.zip");
        File.WriteAllText(path, "not a zip");

        var error = Assert.Throws<NhcFormatException>(() => new TropicalCycloneDatasetProcessor(path));

        Assert.StartsWith("al092022_5day_021.zip: ", error.Message);
        File.Delete(path);
    }

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            TestLuaEngine.Create(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
}
