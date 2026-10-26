using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Grib;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.TestSupport;
using Mapsui;
using PureHDF;
using SkiaSharp;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1074: GRIB2 files through the registry and the host paths —
/// detection (any extension, never clashing with S-100 products or NetCDF),
/// the processor's portrayal, time steps, picks, sampling and headless
/// rendering, what it lists and leaves out, the catalog payload, and the
/// Mapsui session the viewer drives. Values are ecCodes'.
/// </summary>
public sealed class GribProcessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grib-pipeline-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    // 35.75°N, 74.75°W: file point 566 of the US East Coast crop.
    private static (double Latitude, double Longitude) Point566 => CropPoint(566);

    // -----------------------------------------------------------------
    // Detection
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("gfswave.t00z.global.0p25.f003.grib2")]
    [InlineData("waves.grb")]
    [InlineData("waves.grb2")]
    [InlineData("waves.grib")]
    [InlineData("WAVES.GRIB2")]
    [InlineData("gfswave.t00z.global.0p25.f003")]
    [InlineData("filter_gfswave.pl")]
    [InlineData("download.bin")]
    [InlineData("misnamed.h5")]
    [InlineData("misnamed.000")]
    public void A_GRIB2_file_is_detected_by_its_indicator_whatever_its_extension(string name)
    {
        CopyFixture(WaveFilter, PathFor(name));

        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(PathFor(name)));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpecFromSource(source, name));
    }

    [Fact]
    public void A_GRIB_file_after_a_WMO_header_is_detected_by_its_extension()
    {
        var bulletin = Concat("\u0001\r\r\n123\r\r\nHTXA50 KWBC 250000\r\r\n"u8.ToArray(), File.ReadAllBytes(Fixture(Wind10m)));
        var named = Write(PathFor("bulletin.grib2"), bulletin);
        var unnamed = Write(PathFor("bulletin.txt"), bulletin);

        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(named));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(unnamed));
        // ...and the reader skips the header.
        using var processor = Assert.IsType<GribDatasetProcessor>(CreateFactory().CreateProcessor(named));
        Assert.Equal(3, processor.AvailableTimes.Count);
    }

    [Fact]
    public void A_GRIB1_file_named_as_GRIB_reaches_the_reader_which_rejects_it()
    {
        var bytes = new byte[64];
        "GRIB"u8.CopyTo(bytes);
        bytes[7] = 1;
        var named = Write(PathFor("old.grb"), bytes);
        var unnamed = Write(PathFor("old.bin"), bytes);

        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(named));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(unnamed));
        var error = Assert.Throws<UnsupportedGribException>(() => CreateFactory().CreateProcessor(named));
        Assert.StartsWith("old.grb: ", error.Message);
        Assert.Contains("GRIB edition 1, which isn't supported", error.Message);
    }

    [Fact]
    public void The_GRIB2_matcher_claims_no_S100_or_NetCDF_file()
    {
        string[] others =
        [
            Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000"),
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "102*.h5")[0],
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_point.gml"),
            NetCdfFixtures.WriteWaveNetCdf4(PathFor("waves.nc")),
            NetCdfFixtures.WriteWaveNetCdf3(PathFor("ww3.nc")),
            NetCdfFixtures.WriteWaveNetCdf3(PathFor("ww3.grib2")),
        ];

        foreach (var path in others)
        {
            var bytes = File.ReadAllBytes(path);
            var header = new DatasetHeader { LeadingBytes = bytes.AsMemory(0, Math.Min(bytes.Length, DatasetHeader.MaxLength)), Extension = Path.GetExtension(path) };
            Assert.False(WeatherProducts.Grib2.MatchBytes!(header), path);
            Assert.NotEqual("GRIB2", DatasetPipelineFactory.DetectProductSpec(path));
        }

        // The S-100 files keep their products.
        Assert.Equal("S-57", DatasetPipelineFactory.DetectProductSpec(others[0]));
        Assert.Equal("S-102", DatasetPipelineFactory.DetectProductSpec(others[1]));
        Assert.Equal("S-124", DatasetPipelineFactory.DetectProductSpec(others[2]));
        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(others[5]));
    }

    [Theory]
    [InlineData("INT.IHO.S-102.2.2", "S-102")]
    [InlineData("INT.IHO.S-104.2.0", "S-104")]
    [InlineData("INT.IHO.S-111.2.0", "S-111")]
    public void An_S100_HDF5_file_named_as_GRIB_keeps_its_product(string productSpecification, string expected)
    {
        var path = PathFor("product.grib2");
        new H5File { Attributes = new() { ["productSpecification"] = productSpecification } }.Write(path);

        Assert.Equal(expected, DatasetPipelineFactory.DetectProductSpec(path));
    }

    [Fact]
    public void A_GRIB2_file_is_never_claimed_by_an_S100_product_or_NetCDF()
    {
        var path = CopyFixture(WaveFilter, PathFor("waves.grib2"));
        var withoutGrib = new ProductRegistry().AddAllS100Products().AddProduct(WeatherProducts.NetCdf);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, withoutGrib));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(CopyFixture(WaveFilter, PathFor("waves.h5")), withoutGrib));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(CopyFixture(WaveFilter, PathFor("waves")), withoutGrib));
    }

    [Fact]
    public void The_registration_carries_the_weather_traits()
    {
        var registry = ProductRegistry.Default;

        Assert.Contains(WeatherProducts.Grib2, WeatherProducts.All);
        Assert.False(registry.IsS100("GRIB2"));
        Assert.Equal("GRIB2", registry.PortrayalOf("GRIB2").AsApplication?.Basis);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2", WaveHeightStyle.Key));
        Assert.Equal(S98DisplayPlane.DynamicArrows, registry.DisplayPlanes.GetDefaultPlane("GRIB2", WindBarbStyle.Key));
    }

    // -----------------------------------------------------------------
    // The processor
    // -----------------------------------------------------------------

    [Fact]
    public void The_factory_builds_a_GRIB2_processor_with_metadata()
    {
        var path = CopyFixture(WaveFilter, PathFor("filter_gfswave.pl"));

        using var processor = Assert.IsType<GribDatasetProcessor>(CreateFactory().CreateProcessor(path));

        Assert.Equal(SpecRef.Parse("GRIB2/2.0.0"), processor.Spec);
        Assert.Equal("filter_gfswave.pl", processor.FileName);
        var metadata = processor.Metadata;
        Assert.Equal(4326, metadata.HorizontalCrsEpsg);
        Assert.Equal(new TimeCoverage(Run, Run.AddHours(6)), metadata.TimeCoverage);
        Assert.Equal(CropSouth - 0.125, metadata.Extent!.SouthLatitude, 6);
        Assert.Equal(CropWest - 0.125, metadata.Extent.WestLongitude, 6);
        Assert.Equal(CropEast + 0.125, metadata.Extent.EastLongitude, 6);
        Assert.Equal(metadata, WeatherProducts.Grib2.ReadMetadata!(path));
    }

    [Fact]
    public void Time_traits_come_from_the_file()
    {
        using var waves = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());

        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], waves.AvailableTimes);
        Assert.Equal(new DatasetForecastRun("GFS-Wave", Run), waves.ForecastRun);
        Assert.Equal(TimeStepKind.Forecast, waves.TimeSemantics.Kind);
        Assert.Equal(TimeStepSelection.Nearest, waves.TimeSemantics.Selection);

        // Analyses of several runs appended: analysis steps, no run time.
        var grid = new Grid(10, 10, 1, 2, 2);
        byte[] Analysis(DateTime run) => Encode(new Message { Discipline = 10, Number = 3, Process = 11, ReferenceTime = run, Grid = grid, Values = [1, 2, 3, 4] });
        using var analyses = new GribDatasetProcessor(
            Write(PathFor("analyses.grib2"), Concat(Analysis(Run), Analysis(Run.AddHours(6)))), new ProjNetCrsTransformFactory());
        Assert.Equal(new DatasetForecastRun("GFS-Wave", null), analyses.ForecastRun);
        Assert.Equal(TimeStepKind.Analysis, analyses.TimeSemantics.Kind);
    }

    [Fact]
    public async Task The_portrayal_draws_the_wave_fill_and_the_wind_barbs_on_their_planes()
    {
        using var processor = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());

        var portrayal = await processor.BuildCoveragePortrayalAsync(new RenderContext(), Ct);

        Assert.Equal([WindBarbStyle.Key, WaveHeightStyle.Key], portrayal.SubLayers.Select(l => l.LayerKey));
        var barbs = Assert.IsType<ArrowCoverageSubLayer>(portrayal.SubLayers[0]);
        Assert.Equal(S98DisplayPlane.DynamicArrows, barbs.Plane);
        var fill = Assert.IsType<GridCoverageSubLayer>(portrayal.SubLayers[1]);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, fill.Plane);
        Assert.Equal(SpecRef.Parse("GRIB2/2.0.0"), portrayal.Spec);
        Assert.Equal(WeatherProducts.Grib2.Spec, portrayal.Spec.Name);
        Assert.Contains("Not for navigation", portrayal.Info);
        Assert.StartsWith("ds:", portrayal.ContentScope);
    }

    [Fact]
    public async Task The_context_time_step_selects_the_step_drawn()
    {
        using var processor = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());

        await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run.AddHours(4) }, Ct);
        Assert.All(Drawn(processor), s => Assert.Equal(Run.AddHours(3), s.SelectedTime));

        var result = await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run.AddHours(6) }, Ct);
        Assert.All(Drawn(processor), s => Assert.Equal(Run.AddHours(6), s.SelectedTime));
        Assert.Contains("2026-10-25 06:00:00Z", result.Info);
    }

    [Fact]
    public void The_pick_reads_ecCodes_values_and_leads_with_the_notice()
    {
        using var processor = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());
        var (lat, lon) = Point566;

        var info = processor.GetCoverageInfo(lat + 0.01, lon - 0.01, Run.AddHours(4))!;

        Assert.Equal("notice", info.Attributes[0].Code);
        Assert.Equal("Weather model data, not for navigation.", info.Attributes[0].DisplayValue);
        Assert.Equal("GFS-Wave, run 2026-10-25 00Z", info.Attributes.Single(a => a.Code == "model").DisplayValue);
        Assert.Equal(Run.AddHours(3), info.Attributes.Single(a => a.Code == "timePoint").DateTimeValue);
        Assert.Equal($"{FilterAt566.Height[1]:0.0#} m", info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).DisplayValue);
        Assert.Equal($"{FilterAt566.Direction[1]:0}°", info.Attributes.Single(a => a.Code == WeatherParameters.WaveDirection).DisplayValue);
        Assert.Equal($"{FilterAt566.Period[1]:0.#} s", info.Attributes.Single(a => a.Code == WeatherParameters.WavePeriod).DisplayValue);
        Assert.Equal(
            $"{Speed.FromMetresPerSecond(FilterAt566.WindSpeed[1]).TotalKnots:0.#} kn",
            info.Attributes.Single(a => a.Code == WeatherParameters.WindSpeed).DisplayValue);
        Assert.Equal($"{FilterAt566.WindSeaDirection[1]:0}°", info.Attributes.Single(a => a.Code == WeatherParameters.WindSeaDirection).DisplayValue);
        Assert.Equal("(15,5)", info.FeatureRef);

        // Outside the grid, and on land (the bitmap).
        Assert.Null(processor.GetCoverageInfo(0, 0, null));
        var (landLat, landLon) = CropPoint(0);
        Assert.Null(processor.GetCoverageInfo(landLat, landLon, null));
    }

    [Fact]
    public void The_processor_samples_with_quantities_and_units()
    {
        using var processor = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());
        var (lat, lon) = Point566;
        var query = new CoverageSampleQuery(lat, lon, new ProjNetCrsTransformFactory());

        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(
            processor.SampleAt(query, new DateTimeOffset(Run.AddHours(6)), nearestWhenOutOfRange: false));

        var sample = Assert.IsType<CoverageFieldsSample>(outcome.Value);
        Assert.Equal(Run.AddHours(6), sample.SampleTime);
        var fields = sample.Fields.ToDictionary(f => f.Name);
        Assert.Equal(("significant_wave_height", "m"), (fields["wave.hs"].Quantity, fields["wave.hs"].Unit));
        Assert.Equal(FilterAt566.Height[2], fields["wave.hs"].Value!.Value, 1e-4);
        Assert.Equal(("wind_speed", "kn"), (fields["wind.speed.kn"].Quantity, fields["wind.speed.kn"].Unit));
        Assert.Equal(Speed.FromMetresPerSecond(FilterAt566.WindSpeed[2]).TotalKnots, fields["wind.speed.kn"].Value!.Value, 1e-3);
        Assert.Equal(("wind_wave_from_direction", "deg"), (fields["wave.wind_sea.dir"].Quantity, fields["wave.wind_sea.dir"].Unit));
        Assert.Equal(FilterAt566.WindSeaDirection[2], fields["wave.wind_sea.dir"].Value!.Value, 1e-3);
        // Only the first swell partition is a field.
        Assert.Single(sample.Fields, f => f.Name == WeatherParameters.SwellHeight);
        Assert.DoesNotContain(sample.Fields, f => f.Name.StartsWith("grib2:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_headless_render_draws_the_waves()
    {
        using var processor = new GribDatasetProcessor(Fixture(Waves), new ProjNetCrsTransformFactory());

        using var bitmap = await processor.RenderHeadlessAsync(200, 200, new RenderContext(), cancellationToken: Ct);

        int ink = 0;
        for (int y = 0; y < bitmap.Height; y += 4)
        {
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                    ink++;
            }
        }

        Assert.True(ink > 1500, $"Only {ink} of 2500 sampled pixels are drawn.");
    }

    [Fact]
    public void The_contents_list_what_isnt_used_with_why()
    {
        var path = Write(
            PathFor("gfswave.grib2"),
            Concat(File.ReadAllBytes(Fixture(WaveFilter)), File.ReadAllBytes(Fixture(Jpeg2000))));
        using var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());

        var contents = processor.Contents;

        Assert.Equal(13, contents.Count);
        var height = contents.Single(c => c.Name == "wave.hs (HTSGW)");
        Assert.True(height.IsUsed);
        Assert.Equal(("surface", 3), (height.Level, height.Steps));
        var partitions = contents.Where(c => c.Name == "grib2:10,0,8 (SWELL)").ToArray();
        Assert.Equal(2, partitions.Length);
        Assert.All(partitions, p => Assert.Contains("the first partition the file has", p.Reason));
        var jpeg = contents.Single(c => c.Steps == 0);
        Assert.Equal("HTSGW (10,0,3), surface, valid 2026-10-25 03:00Z", jpeg.Name);
        Assert.Contains("5.40 (JPEG 2000)", jpeg.Reason);
    }

    [Fact]
    public void A_file_with_nothing_decodable_fails_with_the_file_name()
    {
        var error = Assert.Throws<UnsupportedGribException>(() => CreateFactory().CreateProcessor(Fixture(Jpeg2000)));

        Assert.StartsWith($"{Jpeg2000}: The file's only field can't be decoded: Data representation template 5.40 (JPEG 2000)", error.Message);
    }

    [Fact]
    public void A_global_0_to_360_grid_picks_either_side_of_the_prime_meridian()
    {
        using var processor = new GribDatasetProcessor(Write(PathFor("global.grib2"), GlobalFile()), new ProjNetCrsTransformFactory());

        Assert.Equal(-181, processor.Metadata.Extent!.WestLongitude, 6);
        Assert.Equal(179, processor.Metadata.Extent.EastLongitude, 6);
        string Height(double longitude) =>
            processor.GetCoverageInfo(40, longitude, Run)!.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).RawValue;

        Assert.Equal(GlobalValue(40, 358), double.Parse(Height(-2), System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(GlobalValue(40, 2), double.Parse(Height(2), System.Globalization.CultureInfo.InvariantCulture), 3);
        // A longitude given in the 0 to 360 convention finds the same node.
        Assert.Equal(Height(-2), Height(358));
    }

    [Fact]
    public void Disposing_the_processor_closes_the_file()
    {
        Assert.SkipUnless(Directory.Exists("/proc/self/fd"), "Counts open files through /proc.");
        var path = CopyFixture(WaveFilter, PathFor("count.grib2"));

        var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());
        processor.GetCoverageInfo(Point566.Latitude, Point566.Longitude, null);
        Assert.Equal(1, OpenHandles(path));

        processor.Dispose();
        Assert.Equal(0, OpenHandles(path));
        _ = WeatherProducts.Grib2.ReadMetadata!(path);
        Assert.Equal(0, OpenHandles(path));
    }

    // -----------------------------------------------------------------
    // Catalog payload
    // -----------------------------------------------------------------

    [Fact]
    public void The_catalog_projects_a_payload_that_samples()
    {
        LoadedDataset? loaded;
        using (var stream = File.OpenRead(Fixture(WaveFilter)))
            loaded = LoadedDatasetProjector.Project(new DatasetId("gfswave"), "GRIB2", stream);

        Assert.NotNull(loaded);
        Assert.Equal(SpecRef.Parse("GRIB2/2.0.0"), loaded.Spec);
        Assert.NotNull(loaded.TimeRange);
        var data = Assert.IsType<WeatherGridDatasetData>(loaded.Data);
        Assert.Equal("GFS-Wave", data.GridSet.Origin.Model);
        var (lat, lon) = Point566;
        var query = new CoverageSampleQuery(lat, lon, new ProjNetCrsTransformFactory());

        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(
            data.SampleAt(query, new DateTimeOffset(Run.AddHours(3)), nearestWhenOutOfRange: false));

        var height = ((CoverageFieldsSample)outcome.Value).Fields.Single(f => f.Name == WeatherParameters.WaveHeight);
        Assert.Equal(FilterAt566.Height[1], height.Value!.Value, 1e-4);
    }

    [Fact]
    public void A_processor_projects_the_same_payload()
    {
        using var processor = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());

        var loaded = LoadedDatasetProjector.Project(new DatasetId("gfswave"), processor);

        Assert.NotNull(loaded);
        Assert.Equal(processor.Metadata.Spec, loaded.Spec);
        Assert.IsType<WeatherGridDatasetData>(loaded.Data);
    }

    // -----------------------------------------------------------------
    // The Mapsui session (the viewer's load path)
    // -----------------------------------------------------------------

    [Fact]
    public async Task The_session_loads_draws_steps_and_picks_a_GRIB2_file()
    {
        var path = CopyFixture(WaveFilter, PathFor("filter_gfswave.pl"));
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });

        var processor = Assert.IsType<GribDatasetProcessor>(CreateFactory().CreateProcessorWithFilesystemUpdates(path));
        var id = new MapDatasetId("filter_gfswave.pl");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "filter_gfswave.pl", processor.Metadata), processor, cancellationToken: Ct));

        // The layer stack: the wave height fill and the wind barbs.
        var entries = s100.Session.GetLayerStackEntries();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Item.SourceFeatureType == WaveHeightStyle.Key && e.Item.Plane == S98DisplayPlane.OnDemandSurface);
        Assert.Contains(entries, e => e.Item.SourceFeatureType == WindBarbStyle.Key && e.Item.Plane == S98DisplayPlane.DynamicArrows);

        // The timeline: three forecast steps of a GFS-Wave run, nearest step.
        var timed = Assert.Single(s100.GetTimeSnapshot().Datasets);
        Assert.Equal(MapsuiTimeSelectionKind.Nearest, timed.Selection);
        Assert.Equal(new DatasetForecastRun("GFS-Wave", Run), timed.ForecastRun);
        Assert.Equal(TimeSpan.FromHours(3), timed.Tolerance);

        await s100.SetTimeAsync(Run.AddHours(6), Ct);
        Assert.All(Drawn(processor), s => Assert.Equal(Run.AddHours(6), s.SelectedTime));

        var (lat, lon) = Point566;
        var pick = Assert.Single(await s100.Query.PickAsync(new GeographicPickQuery { Latitude = lat, Longitude = lon }, Ct));
        Assert.True(pick.IsCoverage);
        Assert.Equal("notice", pick.Info.Attributes[0].Code);
        Assert.Equal(Run.AddHours(6), pick.Info.Attributes.Single(a => a.Code == "timePoint").DateTimeValue);
        Assert.Equal($"{FilterAt566.Height[2]:0.0#} m", pick.Info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).DisplayValue);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());

    // The sources drawn by default: the wind (barbs) and the sea state (fill).
    // The wind-sea and swell sources have no default style, so drawing leaves them be.
    private static IEnumerable<WeatherCoverageSource> Drawn(GribDatasetProcessor processor) =>
        processor.Sources.Where(s => s.Name is WeatherParameters.WindGroup or WeatherParameters.WaveGroup);

    // The number of this process's open file descriptors on the file.
    private static int OpenHandles(string path)
    {
        var full = Path.GetFullPath(path);
        int count = 0;
        foreach (var fd in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            try
            {
                if (new FileInfo(fd).LinkTarget == full)
                    count++;
            }
            catch (IOException)
            {
                // Closed while enumerating.
            }
        }

        return count;
    }
}
