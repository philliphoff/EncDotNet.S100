using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.NetCdf;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using Mapsui;
using PureHDF;
using SkiaSharp;
using static EncDotNet.S100.TestSupport.NetCdfFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1071: NetCDF-CF files through the registry and the host paths —
/// detection (never clashing with S-102, S-104 or S-111), the processor's
/// portrayal, time steps, picks, sampling and headless rendering, the catalog
/// payload, and the Mapsui session the viewer drives.
/// </summary>
public sealed class NetCdfProcessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf-pipeline-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    // -----------------------------------------------------------------
    // Detection
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("waves.nc")]
    [InlineData("waves.nc4")]
    [InlineData("waves.h5")]
    [InlineData("waves")]
    public void A_CF_NetCDF4_file_is_detected_whatever_its_extension(string name)
    {
        var path = WriteWaveNetCdf4(PathFor(name));

        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(path));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpecFromSource(source, name));
    }

    [Theory]
    [InlineData("ww3.nc", false)]
    [InlineData("ww3.nc", true)]
    [InlineData("griddap-download", false)]
    public void A_NetCDF3_file_is_detected_by_its_signature(string name, bool offset64)
    {
        var path = WriteWaveNetCdf3(PathFor(name), offset64: offset64);

        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(path));
    }

    [Fact]
    public void An_HDF5_nc_file_without_Conventions_is_offered_to_the_reader()
    {
        var path = PathFor("plain.nc");
        new H5File { ["x"] = new float[] { 1, 2 } }.Write(path);

        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(path));
        // ...which then says what the file lacks.
        var error = Assert.Throws<CfConventionsException>(() => CreateFactory().CreateProcessor(path));
        Assert.StartsWith("plain.nc: The file has no latitude coordinate", error.Message);
        Assert.Contains("Variables found: x(phony_dim_0)", error.Message);
    }

    [Theory]
    [InlineData("INT.IHO.S-102.2.2", "S-102")]
    [InlineData("INT.IHO.S-104.2.0", "S-104")]
    [InlineData("INT.IHO.S-111.2.0", "S-111")]
    public void An_S100_HDF5_file_is_never_claimed_by_NetCDF_even_with_CF_Conventions(string productSpecification, string expected)
    {
        foreach (var name in (string[])["product.h5", "product.nc"])
        {
            var path = PathFor(name);
            new H5File
            {
                Attributes = new() { ["productSpecification"] = productSpecification, ["Conventions"] = "CF-1.6" },
            }.Write(path);

            Assert.Equal(expected, DatasetPipelineFactory.DetectProductSpec(path));
            Assert.False(WeatherProducts.NetCdf.MatchHdf5!(Hdf5Root(path)));
        }
    }

    [Fact]
    public void A_CF_NetCDF4_file_is_never_claimed_by_S102_S104_or_S111()
    {
        var path = WriteWaveNetCdf4(PathFor("waves.h5"));
        var coverageOnly = new ProductRegistry()
            .AddProduct(S100Products.S102)
            .AddProduct(S100Products.S104)
            .AddProduct(S100Products.S111);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, coverageOnly));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(WriteWaveNetCdf3(PathFor("ww3.h5")), coverageOnly));
    }

    [Fact]
    public void Without_the_weather_registrations_NetCDF_is_unknown()
    {
        var registry = new ProductRegistry().AddAllS100Products();

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(WriteWaveNetCdf4(PathFor("waves.nc")), registry));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(WriteWaveNetCdf3(PathFor("ww3.nc")), registry));
    }

    [Fact]
    public void The_registration_carries_the_weather_traits()
    {
        var registry = ProductRegistry.Default;

        Assert.False(registry.IsS100("NetCDF"));
        Assert.Equal("NetCDF-CF", registry.PortrayalOf("NetCDF").AsApplication?.Basis);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("NetCDF", WaveHeightStyle.Key));
        Assert.Equal(S98DisplayPlane.DynamicArrows, registry.DisplayPlanes.GetDefaultPlane("NetCDF", WindBarbStyle.Key));
    }

    // -----------------------------------------------------------------
    // The processor
    // -----------------------------------------------------------------

    [Fact]
    public void The_factory_builds_a_NetCDF4_processor_with_metadata()
    {
        using var processor = Assert.IsType<NetCdfDatasetProcessor>(CreateFactory().CreateProcessor(WriteWaveNetCdf4(PathFor("waves.nc"))));

        Assert.Equal(SpecRef.Parse("NetCDF/4.0.0"), processor.Spec);
        Assert.Equal("waves.nc", processor.FileName);
        var metadata = processor.Metadata;
        Assert.Equal(4326, metadata.HorizontalCrsEpsg);
        Assert.Equal(new TimeCoverage(Run, Run.AddHours(6)), metadata.TimeCoverage);
        Assert.Equal(-32.315, metadata.Extent!.SouthLatitude, 4);
        Assert.Equal(62.185, metadata.Extent.EastLongitude, 4);
        Assert.Equal(metadata.Spec, WeatherProducts.NetCdf.ReadMetadata!(PathFor("waves.nc")).Spec);
    }

    [Fact]
    public void A_NetCDF3_file_reports_NetCDF_3()
    {
        using var processor = new NetCdfDatasetProcessor(WriteWaveNetCdf3(PathFor("ww3.nc")), new ProjNetCrsTransformFactory());

        Assert.Equal(SpecRef.Parse("NetCDF/3.0.0"), processor.Spec);
        Assert.Equal("WW3", processor.ForecastRun.Model);
    }

    [Fact]
    public void Time_traits_come_from_the_file()
    {
        using var waves = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());
        using var wind = new NetCdfDatasetProcessor(WriteWindNetCdf4(PathFor("wind.nc")), new ProjNetCrsTransformFactory());

        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], waves.AvailableTimes);
        Assert.Equal(new DatasetForecastRun("MFWAM", Run), waves.ForecastRun);
        Assert.Equal(TimeStepKind.Forecast, waves.TimeSemantics.Kind);
        Assert.Equal(TimeStepSelection.Nearest, waves.TimeSemantics.Selection);
        // No reference time in the wind file: analysis steps, no run time.
        Assert.Equal(new DatasetForecastRun("Synthetic wind model", null), wind.ForecastRun);
        Assert.Equal(TimeStepKind.Analysis, wind.TimeSemantics.Kind);
    }

    [Fact]
    public async Task The_portrayal_draws_the_default_styles_on_their_planes()
    {
        using var waves = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());
        using var wind = new NetCdfDatasetProcessor(WriteWindNetCdf4(PathFor("wind.nc")), new ProjNetCrsTransformFactory());

        var wavePortrayal = await waves.BuildCoveragePortrayalAsync(new RenderContext(), Ct);
        var windPortrayal = await wind.BuildCoveragePortrayalAsync(new RenderContext(), Ct);

        var fill = Assert.IsType<GridCoverageSubLayer>(Assert.Single(wavePortrayal.SubLayers));
        Assert.Equal(WaveHeightStyle.Key, fill.LayerKey);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, fill.Plane);
        Assert.Equal(0.5, fill.Opacity);
        Assert.Equal(SpecRef.Parse("NetCDF/4.0.0"), wavePortrayal.Spec);
        Assert.Equal("waves.nc", wavePortrayal.SourceDatasetId);
        Assert.Contains("Not for navigation", wavePortrayal.Info);
        Assert.StartsWith("ds:", wavePortrayal.ContentScope);

        var barbs = Assert.IsType<ArrowCoverageSubLayer>(Assert.Single(windPortrayal.SubLayers));
        Assert.Equal(WindBarbStyle.Key, barbs.LayerKey);
        Assert.Equal(S98DisplayPlane.DynamicArrows, barbs.Plane);
        Assert.Equal(20, barbs.WithinPlanePriority);
    }

    [Fact]
    public async Task The_context_time_step_selects_the_step_drawn()
    {
        using var processor = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());

        await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run.AddHours(4) }, Ct);
        Assert.Equal(Run.AddHours(3), processor.Sources[0].SelectedTime);

        var result = await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run.AddHours(6) }, Ct);
        Assert.Equal(Run.AddHours(6), processor.Sources[0].SelectedTime);
        Assert.Contains("2026-10-25 06:00:00Z", result.Info);
    }

    [Fact]
    public async Task A_zoomed_out_viewport_draws_an_overview_level()
    {
        using var processor = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());
        var zoomedOut = new Viewport
        {
            MinLatitude = -35,
            MaxLatitude = -29,
            MinLongitude = 59,
            MaxLongitude = 65,
            WidthPixels = 40,
            HeightPixels = 40,
            ScaleDenominator = 5_000_000,
        };

        await processor.BuildCoveragePortrayalAsync(new RenderContext { Viewport = zoomedOut }, Ct);
        Assert.Equal(2, processor.Sources[0].SelectedOverviewLevel);

        await processor.BuildCoveragePortrayalAsync(new RenderContext(), Ct);
        Assert.Equal(0, processor.Sources[0].SelectedOverviewLevel);
    }

    [Fact]
    public void The_pick_leads_with_the_not_for_navigation_notice_and_gives_units()
    {
        using var waves = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());
        var grid = ChartGrid;
        double lat = grid.South + 4 * grid.Spacing, lon = grid.West + 7 * grid.Spacing;

        var info = waves.GetCoverageInfo(lat + 0.002, lon - 0.002, Run.AddHours(5))!;

        Assert.Equal("notice", info.Attributes[0].Code);
        Assert.Equal("Weather model data, not for navigation.", info.Attributes[0].DisplayValue);
        Assert.Equal("MFWAM, run 2026-10-25 00Z", info.Attributes.Single(a => a.Code == "model").DisplayValue);
        Assert.Equal(Run.AddHours(6), info.Attributes.Single(a => a.Code == "timePoint").DateTimeValue);
        var height = info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{WaveHeight(4, 7, 2):0.0#} m"), height.DisplayValue);
        Assert.Equal("270°", info.Attributes.Single(a => a.Code == WeatherParameters.WaveDirection).DisplayValue);
        Assert.Equal("8.7 s", info.Attributes.Single(a => a.Code == WeatherParameters.WavePeriod).DisplayValue);
        Assert.Equal("(4,7)", info.FeatureRef);

        // Outside the grid, and at the node with no data.
        Assert.Null(waves.GetCoverageInfo(0, 0, null));
        Assert.Null(waves.GetCoverageInfo(grid.South + 19 * grid.Spacing, grid.West + 19 * grid.Spacing, null));
    }

    [Fact]
    public void The_wind_pick_derives_speed_in_knots_and_direction()
    {
        using var wind = new NetCdfDatasetProcessor(WriteWindNetCdf4(PathFor("wind.nc")), new ProjNetCrsTransformFactory());
        var grid = ChartWindGrid;

        var info = wind.GetCoverageInfo(grid.South + 2 * grid.Spacing, grid.West + 3 * grid.Spacing, Run)!;

        var (knots, from) = Wind(2, 3, 0, grid.Columns);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{knots:0.#} kn"), info.Attributes.Single(a => a.Code == WeatherParameters.WindSpeed).DisplayValue);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{from:0}°"), info.Attributes.Single(a => a.Code == WeatherParameters.WindFromDirection).DisplayValue);
    }

    [Fact]
    public void The_processor_samples_with_quantities_and_units()
    {
        using var wind = new NetCdfDatasetProcessor(WriteWindNetCdf4(PathFor("wind.nc")), new ProjNetCrsTransformFactory());
        var grid = ChartWindGrid;
        var query = new CoverageSampleQuery(grid.South + grid.Spacing, grid.West + grid.Spacing, new ProjNetCrsTransformFactory());

        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(
            wind.SampleAt(query, new DateTimeOffset(Run.AddHours(3)), nearestWhenOutOfRange: false));

        var sample = Assert.IsType<CoverageFieldsSample>(outcome.Value);
        Assert.Equal(Run.AddHours(3), sample.SampleTime);
        Assert.Equal((1, 1), (sample.Row, sample.Column));
        var fields = sample.Fields.ToDictionary(f => f.Name);
        Assert.Equal(("eastward_wind", "m/s"), (fields["wind.u"].Quantity, fields["wind.u"].Unit));
        Assert.Equal(("wind_speed", "m/s"), (fields["wind.speed"].Quantity, fields["wind.speed"].Unit));
        Assert.Equal(("wind_speed", "kn"), (fields["wind.speed.kn"].Quantity, fields["wind.speed.kn"].Unit));
        Assert.Equal(Wind(1, 1, 1, grid.Columns).Knots, fields["wind.speed.kn"].Value!.Value, 3);
        Assert.Equal(("wind_from_direction", "deg"), (fields["wind.from_direction"].Quantity, fields["wind.from_direction"].Unit));
        Assert.Equal(Wind(1, 1, 1, grid.Columns).FromDirection, fields["wind.from_direction"].Value!.Value, 2);
    }

    [Fact]
    public async Task The_headless_render_draws_the_wave_fill()
    {
        using var processor = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());

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
    public void A_file_that_isnt_CF_fails_with_the_file_name()
    {
        var path = PathFor("lambert.nc");
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables.Add(new("crs", FixtureType.Int, [], [0], new() { ["grid_mapping_name"] = "lambert_conformal_conic" }));
        model.Variables[3].Attributes["grid_mapping"] = "crs";
        WriteNetCdf3(path, model);

        var error = Assert.Throws<UnsupportedWeatherGridException>(() => CreateFactory().CreateProcessor(path));

        Assert.StartsWith("lambert.nc: Variable 'VHM0' is on a projected grid", error.Message);
        Assert.Contains("lambert_conformal_conic", error.Message);
    }

    // -----------------------------------------------------------------
    // Catalog payload
    // -----------------------------------------------------------------

    [Fact]
    public void The_catalog_projects_a_payload_that_samples()
    {
        var path = WriteWaveNetCdf3(PathFor("ww3.nc"));
        LoadedDataset? loaded;
        using (var stream = File.OpenRead(path))
            loaded = LoadedDatasetProjector.Project(new DatasetId("ww3"), "NetCDF", stream);

        Assert.NotNull(loaded);
        Assert.Equal(SpecRef.Parse("NetCDF/3.0.0"), loaded.Spec);
        Assert.NotNull(loaded.TimeRange);
        var data = Assert.IsType<WeatherGridDatasetData>(loaded.Data);
        var grid = ChartGrid;
        var query = new CoverageSampleQuery(grid.South + 5 * grid.Spacing, grid.West + 6 * grid.Spacing, new ProjNetCrsTransformFactory());

        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(
            data.SampleAt(query, new DateTimeOffset(Run.AddHours(3)), nearestWhenOutOfRange: false));

        var height = ((CoverageFieldsSample)outcome.Value).Fields.Single(f => f.Name == WeatherParameters.WaveHeight);
        Assert.Equal(("significant_wave_height", "m"), (height.Quantity, height.Unit));
        Assert.Equal(WaveHeight(5, 6, 1), height.Value!.Value, 4);
    }

    [Fact]
    public void A_processor_projects_the_same_payload()
    {
        using var processor = new NetCdfDatasetProcessor(WriteWaveNetCdf4(PathFor("waves.nc")), new ProjNetCrsTransformFactory());

        var loaded = LoadedDatasetProjector.Project(new DatasetId("waves"), processor);

        Assert.NotNull(loaded);
        Assert.Equal(processor.Metadata.Spec, loaded.Spec);
        Assert.IsType<WeatherGridDatasetData>(loaded.Data);
    }

    // -----------------------------------------------------------------
    // The Mapsui session (the viewer's load path)
    // -----------------------------------------------------------------

    [Fact]
    public async Task The_session_loads_draws_steps_and_picks_a_NetCDF_file()
    {
        var path = WriteWaveNetCdf4(PathFor("waves.nc"));
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });

        // What S100DatasetLoader.LoadAsync (and the viewer's loader) does:
        // detect, build through the factory, register with the processor's metadata.
        var processor = Assert.IsType<NetCdfDatasetProcessor>(CreateFactory().CreateProcessorWithFilesystemUpdates(path));
        var id = new MapDatasetId("waves.nc");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "waves.nc", processor.Metadata), processor, cancellationToken: Ct));

        // The layer stack: the wave height fill on the on-demand surface plane.
        var entry = Assert.Single(s100.Session.GetLayerStackEntries());
        Assert.Equal(WaveHeightStyle.Key, entry.Item.SourceFeatureType);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, entry.Item.Plane);

        // The timeline: three forecast steps of an MFWAM run, nearest step.
        var timed = Assert.Single(s100.GetTimeSnapshot().Datasets);
        Assert.Equal(MapsuiTimeSelectionKind.Nearest, timed.Selection);
        Assert.Equal(new DatasetForecastRun("MFWAM", Run), timed.ForecastRun);
        Assert.Equal(TimeSpan.FromHours(3), timed.Tolerance);

        // Step to 06Z: the source draws the third step and the pick reads it.
        await s100.SetTimeAsync(Run.AddHours(6), Ct);
        Assert.Equal(Run.AddHours(6), processor.Sources[0].SelectedTime);

        var grid = ChartGrid;
        var pick = Assert.Single(await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = grid.South + 3 * grid.Spacing, Longitude = grid.West + 3 * grid.Spacing },
            Ct));
        Assert.True(pick.IsCoverage);
        Assert.Equal("notice", pick.Info.Attributes[0].Code);
        Assert.Equal(Run.AddHours(6), pick.Info.Attributes.Single(a => a.Code == "timePoint").DateTimeValue);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{WaveHeight(3, 3, 2):0.0#} m"), pick.Info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).DisplayValue);
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

    // Runs the NetCDF HDF5 matcher on a file's root, as detection does.
    private static Hdf5RootInfo Hdf5Root(string path)
    {
        // The factory reads productSpecification first; mirror that here.
        using var file = EncDotNet.S100.Hdf5.PureHdf.PureHdfFile.Open(path);
        var root = file.Root;
        return new Hdf5RootInfo
        {
            ProductSpecification = root.AttributeExists("productSpecification") ? root.ReadStringAttribute("productSpecification") : "",
        };
    }
}
