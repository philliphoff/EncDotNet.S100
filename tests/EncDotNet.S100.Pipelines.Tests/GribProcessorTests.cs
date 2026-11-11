using System.Globalization;
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
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2", PrecipitationStyle.Key));
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2", ThunderstormRiskStyle.Key));
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2", CloudCoverStyle.Key));
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
    public async Task A_GFS_file_with_air_and_skin_temperature_draws_freezing_spray_over_water_and_picks_it()
    {
        // GFS fields at 60°N: column 0 is water, column 1 land. A 15 m/s
        // wind, −10 °C air and +2 °C water: Overland's predictor is 59, heavy.
        var grid = new Grid(60, -170, 1, 1, 2);
        Message Field(byte discipline, byte category, byte number, byte surface, int value, double[] values) =>
            new() { Discipline = discipline, Category = category, Number = number, SurfaceType = surface, SurfaceValue = value, Grid = grid, Values = values, DecimalScale = 2 };
        var path = Write(PathFor("gfs-icing.grib2"), Concat(
            Encode(Field(0, 2, 2, 103, 10, [0, 0])),
            Encode(Field(0, 2, 3, 103, 10, [15, 15])),
            Encode(Field(0, 0, 0, 103, 2, [263.15, 263.15])),
            Encode(Field(0, 0, 0, 1, 0, [275.15, 255.15])),
            Encode(Field(2, 0, 0, 1, 0, [0, 1]))));
        using var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());

        Assert.Equal(
            [WeatherParameters.WindU, WeatherParameters.WindV, WeatherParameters.AirTemperature, WeatherParameters.SurfaceTemperature, WeatherParameters.LandCover],
            processor.GridSet.Parameters.Select(p => p.Id));
        var portrayal = await processor.BuildCoveragePortrayalAsync(new RenderContext(), Ct);
        var spray = Assert.IsType<GridCoverageSubLayer>(portrayal.SubLayers.Single(l => l.LayerKey == FreezingSprayStyle.Key));
        Assert.True(spray.ClipToWater);
        Assert.DoesNotContain(portrayal.SubLayers, l => l.LayerKey == AirTemperatureIsothermStyle.Key);
        var withAir = await processor.BuildCoveragePortrayalAsync(
            new RenderContext { EcdisDisplay = new EcdisDisplaySettings { LayerVisibility = new Dictionary<string, bool> { [AirTemperatureIsothermStyle.Key] = true } } }, Ct);
        Assert.Contains(withAir.SubLayers, l => l.LayerKey == AirTemperatureIsothermStyle.Key);

        var water = processor.GetCoverageInfo(60, -170, null)!.Attributes;
        Assert.Equal("Heavy (predictor 59)", water.Single(a => a.Code == WeatherParameters.FreezingSprayPredictor).DisplayValue);
        Assert.Equal("-10.0 °C", water.Single(a => a.Code == WeatherParameters.AirTemperature).DisplayValue);
        Assert.Equal("2.0 °C", water.Single(a => a.Code == WeatherParameters.SurfaceTemperature).DisplayValue);
        Assert.DoesNotContain(water, a => a.Code == WeatherParameters.LandCover);
        var land = processor.GetCoverageInfo(60, -169, null)!.Attributes;
        Assert.Equal("—", land.Single(a => a.Code == WeatherParameters.FreezingSprayPredictor).DisplayValue);

        var query = new CoverageSampleQuery(60, -170, new ProjNetCrsTransformFactory());
        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(processor.SampleAt(query, null, nearestWhenOutOfRange: true));
        var fields = Assert.IsType<CoverageFieldsSample>(outcome.Value).Fields.ToDictionary(f => f.Name);
        Assert.Equal((double)(int)FreezingSprayClass.Heavy, fields["spray.predictor.class"].Value);
        Assert.Equal(-10, fields["air.temperature.degc"].Value!.Value, 2);
    }

    [Fact]
    public async Task A_GFS_file_with_precipitation_CAPE_and_cloud_draws_them_only_when_switched_on_and_picks_them()
    {
        // GFS fields at 25°N, 6 h into the run. GFS writes the precipitation
        // rate and cloud cover at the valid time, then as an average over
        // the hours before it: the first is read. CAPE for the 180–0 hPa
        // layer and low cloud aren't the rows' levels, so they're left out.
        var grid = new Grid(25, -80, 1, 1, 2);
        Message Field(byte category, byte number, byte surface, int value, double[] values, int decimals) =>
            new() { Category = category, Number = number, SurfaceType = surface, SurfaceValue = value, ForecastHours = 6, Grid = grid, Values = values, DecimalScale = decimals };
        var path = Write(PathFor("gfs-convection.grib2"), Concat(
            Encode(Field(1, 7, 1, 0, [0.001, 0], 6)),
            Encode(Field(1, 7, 1, 0, [0.01, 0.01], 6) with { ForecastHours = 0, IntervalHours = 6 }),
            Encode(Field(7, 6, 1, 0, [1850, 400], 0)),
            Encode(Field(7, 6, 108, 18000, [5000, 5000], 0) with { SecondSurfaceType = 108, SecondSurfaceValue = 0 }),
            Encode(Field(6, 1, 10, 0, [80, 10], 0)),
            Encode(Field(6, 1, 214, 0, [100, 100], 0))));
        using var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());

        Assert.Equal(
            [WeatherParameters.PrecipitationRate, WeatherParameters.Cape, WeatherParameters.CloudCover],
            processor.GridSet.Parameters.Where(p => p.IsKnown).Select(p => p.Id));
        Assert.Equal(WeatherLevel.EntireAtmosphere, processor.GridSet.FindParameter(WeatherParameters.CloudCover)!.Level);
        string[] keys = [PrecipitationStyle.Key, ThunderstormRiskStyle.Key, CloudCoverStyle.Key];

        // Off by default: the chart isn't covered until the mariner asks.
        var portrayal = await processor.BuildCoveragePortrayalAsync(new RenderContext(), Ct);
        Assert.DoesNotContain(portrayal.SubLayers, l => keys.Contains(l.LayerKey));

        var on = new EcdisDisplaySettings { LayerVisibility = keys.ToDictionary(k => k, _ => true) };
        var withAll = await processor.BuildCoveragePortrayalAsync(new RenderContext { EcdisDisplay = on }, Ct);
        var fills = keys.Select(k => Assert.IsType<GridCoverageSubLayer>(withAll.SubLayers.Single(l => l.LayerKey == k))).ToArray();
        Assert.All(fills, f => Assert.True(f.ClipToWater, f.LayerKey));
        Assert.All(fills, f => Assert.Equal(S98DisplayPlane.OnDemandSurface, f.Plane));
        Assert.Equal([0.5, 0.5, 0.3], fills.Select(f => f.Opacity));

        IReadOnlyList<PickAttribute> At(double longitude) => processor.GetCoverageInfo(25, longitude, null)!.Attributes;
        string? Pick(double longitude, string code) => At(longitude).Single(a => a.Code == code).DisplayValue;
        Assert.Equal("3.6 mm/h (moderate)", Pick(-80, WeatherParameters.PrecipitationRate));
        Assert.Equal("0.0 mm/h", Pick(-79, WeatherParameters.PrecipitationRate));
        Assert.Equal("1850 J/kg (moderate thunderstorm risk)", Pick(-80, WeatherParameters.Cape));
        Assert.Equal("400 J/kg", Pick(-79, WeatherParameters.Cape));
        Assert.Equal("80 %", Pick(-80, WeatherParameters.CloudCover));
        Assert.Equal("10 %", Pick(-79, WeatherParameters.CloudCover));
        Assert.DoesNotContain(At(-80), a => a.Code == WeatherParameters.PrecipitationRate + ".mmh");

        var query = new CoverageSampleQuery(25, -80, new ProjNetCrsTransformFactory());
        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(processor.SampleAt(query, null, nearestWhenOutOfRange: true));
        var fields = Assert.IsType<CoverageFieldsSample>(outcome.Value).Fields.ToDictionary(f => f.Name);
        Assert.Equal(("precipitation_flux", "kg m-2 s-1"), (fields["precipitation.rate"].Quantity, fields["precipitation.rate"].Unit));
        Assert.Equal(("precipitation_flux", "mm/h"), (fields["precipitation.rate.mmh"].Quantity, fields["precipitation.rate.mmh"].Unit));
        Assert.Equal(3.6, fields["precipitation.rate.mmh"].Value!.Value, 3);
        Assert.Equal(("atmosphere_convective_available_potential_energy", "J/kg"), (fields["cape"].Quantity, fields["cape"].Unit));
        Assert.Equal(("cloud_area_fraction", "%"), (fields["cloud.cover"].Quantity, fields["cloud.cover"].Unit));
    }

    [Fact]
    public async Task Visibility_draws_a_sparse_fill_and_picks_in_nautical_miles()
    {
        // Rows run north to south: 10°N has 400 m and 2500 m, 9°N 8000 m and 24100 m (good).
        var grid = new Grid(10, 10, 1, 2, 2);
        var path = Write(PathFor("vis.grib2"), Encode(new Message { Category = 19, Number = 0, Grid = grid, DecimalScale = 0, Values = [400, 2500, 8000, 24100] }));
        using var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());

        var portrayal = await processor.BuildCoveragePortrayalAsync(new RenderContext(), Ct);
        var fill = Assert.IsType<GridCoverageSubLayer>(Assert.Single(portrayal.SubLayers));
        Assert.Equal(VisibilityStyle.Key, fill.LayerKey);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, fill.Plane);
        Assert.True(fill.ClipToWater);

        string? Pick(double latitude, double longitude) =>
            processor.GetCoverageInfo(latitude, longitude, null)!.Attributes.Single(a => a.Code == WeatherParameters.Visibility).DisplayValue;
        Assert.Equal("0.2 nm (400 m)", Pick(10, 10));
        Assert.Equal("1.3 nm", Pick(10, 11));
        Assert.Equal("4.3 nm", Pick(9, 10));
        Assert.Equal("13.0 nm", Pick(9, 11));
        Assert.DoesNotContain(processor.GetCoverageInfo(9, 11, null)!.Attributes, a => a.Code == WeatherParameters.Visibility + ".nmi");

        var query = new CoverageSampleQuery(9, 10, new ProjNetCrsTransformFactory());
        var outcome = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(processor.SampleAt(query, null, nearestWhenOutOfRange: true));
        var fields = Assert.IsType<CoverageFieldsSample>(outcome.Value).Fields.ToDictionary(f => f.Name);
        Assert.Equal(("visibility_in_air", "m"), (fields["visibility"].Quantity, fields["visibility"].Unit));
        Assert.Equal(("visibility_in_air", "nmi"), (fields["visibility.nmi"].Quantity, fields["visibility.nmi"].Unit));
        Assert.Equal(8000 / 1852.0, fields["visibility.nmi"].Value!.Value, 3);
    }

    [Fact]
    public void The_pick_reads_ecCodes_values_and_leads_with_the_notice()
    {
        using var processor = new GribDatasetProcessor(Fixture(WaveFilter), new ProjNetCrsTransformFactory());
        var (lat, lon) = Point566;

        var info = processor.GetCoverageInfo(lat + 0.01, lon - 0.01, Run.AddHours(4))!;

        Assert.Equal("notice", info.Attributes[0].Code);
        Assert.True(info.Attributes[0].IsNavigationNotice);
        Assert.Equal("Weather model data, not for navigation.", info.Attributes[0].DisplayValue);
        Assert.Equal("GFS-Wave, run 2026-10-25 00Z", info.Attributes.Single(a => a.Code == "model").DisplayValue);
        Assert.Equal(Run.AddHours(3), info.Attributes.Single(a => a.Code == "timePoint").DateTimeValue);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{FilterAt566.Height[1]:0.0#} m"), info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).DisplayValue);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{FilterAt566.Direction[1]:0}°"), info.Attributes.Single(a => a.Code == WeatherParameters.WaveDirection).DisplayValue);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{FilterAt566.Period[1]:0.#} s"), info.Attributes.Single(a => a.Code == WeatherParameters.WavePeriod).DisplayValue);
        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"{Speed.FromMetresPerSecond(FilterAt566.WindSpeed[1]).TotalKnots:0.#} kn"),
            info.Attributes.Single(a => a.Code == WeatherParameters.WindSpeed).DisplayValue);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{FilterAt566.WindSeaDirection[1]:0}°"), info.Attributes.Single(a => a.Code == WeatherParameters.WindSeaDirection).DisplayValue);
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
            Concat(File.ReadAllBytes(Fixture(WaveFilter)), CcsdsMessage()));
        using var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());

        var contents = processor.Contents;

        Assert.Equal(13, contents.Count);
        var height = contents.Single(c => c.Name == "wave.hs (HTSGW)");
        Assert.True(height.IsUsed);
        Assert.Equal(("surface", 3), (height.Level, height.Steps));
        var partitions = contents.Where(c => c.Name == "grib2:10,0,8 (SWELL)").ToArray();
        Assert.Equal(2, partitions.Length);
        Assert.All(partitions, p => Assert.Contains("the first partition the file has", p.Reason));
        var ccsds = contents.Single(c => c.Steps == 0);
        Assert.Equal("HTSGW (10,0,3), mean sea level, valid 2026-10-25 03:00Z", ccsds.Name);
        Assert.Contains("5.42 (CCSDS", ccsds.Reason);
    }

    [Fact]
    public void A_file_with_nothing_decodable_fails_with_the_file_name()
    {
        var path = Write(PathFor("20261025000000-3h-wave-fc.grib2"), CcsdsMessage());

        var error = Assert.Throws<UnsupportedGribException>(() => CreateFactory().CreateProcessor(path));

        Assert.StartsWith("20261025000000-3h-wave-fc.grib2: The file's only field can't be decoded: Data representation template 5.42 (CCSDS", error.Message);
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
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{FilterAt566.Height[2]:0.0#} m"), pick.Info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).DisplayValue);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            TestLuaEngine.Create(),
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
