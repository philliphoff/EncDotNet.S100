using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Chart-wide water level adjustment (#1132 phase 3): the datum rule of
/// <see cref="WaterLevelDepthAdjustment"/> and the adjusted S-102 surface and
/// safety contour (<c>docs/design/chart-wide-water-level-adjustment.md</c>
/// §2.3, §3.1, §11 Q2).
/// </summary>
public sealed class WaterLevelDepthAdjustmentTests
{
    private const int Lat = 23;
    private const int Mllw = 12;
    private const float Fill = S102CoverageSource.FillValue;
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(Lat, Lat, false, true)]
    [InlineData(Mllw, Lat, false, false)]
    [InlineData(Mllw, Lat, true, false)]
    [InlineData(null, Lat, false, false)]
    [InlineData(null, Lat, true, true)]
    [InlineData(Lat, null, true, true)]
    public void Applies_water_level_only_on_a_permitted_datum(int? depthDatum, int? waterDatum, bool applyUndeclared, bool applies)
    {
        var adjustment = Adjustment(new Water(waterDatum, (_, _) => -0.4), applyUndeclared);

        var at = adjustment.At(0, 0, depthDatum);

        Assert.Equal(applies, at is not null);
        Assert.Equal(applies, adjustment.Accepts(depthDatum));
        if (applies)
        {
            Assert.Equal(-0.4, at!.Value.HeightMeters, 6);
        }
    }

    [Fact]
    public void A_refused_point_reports_a_datum_mismatch()
    {
        var adjustment = Adjustment(new Water(Lat, (_, _) => 1.0), applyUndeclared: false);

        Assert.Equal(WaterLevelStatus.DatumMismatch, adjustment.Resolve(new GeoPosition(0, 0), null).Status);
        Assert.Equal(WaterLevelStatus.DatumMismatch, adjustment.Resolve(new GeoPosition(0, 0), Mllw).Status);
        Assert.Equal(WaterLevelStatus.Applied, adjustment.Resolve(new GeoPosition(0, 0), Lat).Status);
    }

    [Fact]
    public void Nothing_applies_outside_the_series_time_range()
    {
        var water = new Water(Lat, (_, _) => 1.0);
        var adjustment = new WaterLevelDepthAdjustment(new WaterLevelSnapshot(water, T0.AddDays(2)), false, "test");

        Assert.Null(adjustment.At(0, 0, Lat));
    }

    [Fact]
    public void The_key_changes_with_the_time_and_the_override()
    {
        var water = new Water(Lat, (_, _) => 1.0);
        string Key(DateTime t, bool undeclared) =>
            new WaterLevelDepthAdjustment(new WaterLevelSnapshot(water, t), undeclared, "catalog-1").Key;

        Assert.Equal(Key(T0.AddHours(1), false), Key(T0.AddHours(1), false));
        Assert.NotEqual(Key(T0.AddHours(1), false), Key(T0.AddHours(2), false));
        Assert.NotEqual(Key(T0.AddHours(1), false), Key(T0.AddHours(1), true));
    }

    [Fact]
    public void Adjuster_adds_the_water_level_and_keeps_no_data_and_uncovered_nodes()
    {
        // Columns at 20°E…23°E; water level only east of 21.5°E.
        var layer = Layer(2, 4, (_, c) => c == 3 ? Fill : 10f);
        var adjustment = Adjustment(new Water(Lat, (_, lon) => lon > 21.5 ? -1.5 : null), applyUndeclared: false);

        var (adjusted, count) = S102DepthAdjuster.Apply(layer, adjustment, Lat, nativeToWgs84: null);

        var depths = adjusted.Coverage.GetValues(S102DepthAdjuster.DepthField);
        Assert.Equal(2, count);
        Assert.Equal([10f, 10f, 8.5f, Fill, 10f, 10f, 8.5f, Fill], depths);
        Assert.Equal([10f, 10f, 10f, Fill, 10f, 10f, 10f, Fill], layer.Coverage.GetValues(S102DepthAdjuster.DepthField));
    }

    [Fact]
    public void Adjuster_leaves_a_refused_surface_untouched()
    {
        var layer = Layer(2, 2, (_, _) => 10f);
        var adjustment = Adjustment(new Water(Lat, (_, _) => 1.0), applyUndeclared: true);

        var (adjusted, count) = S102DepthAdjuster.Apply(layer, adjustment, Mllw, nativeToWgs84: null);

        Assert.Same(layer, adjusted);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Processor_traces_the_safety_contour_on_the_adjusted_depths()
    {
        // Depth rises 1 m per row from 10 m; a falling tide of 1 m makes the
        // 13.5 m line sit where the charted depth is 14.5 m, one row north.
        var path = S102Grids.Write(8, 8, (r, _) => 10f + r, verticalDatum: Lat);
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(path, manager, new MoonSharpLuaEngine(), new ProjNetCrsTransformFactory());
        try
        {
            var mariner = MarinerSettings.Default with
            {
                ShallowContour = Depth.FromMetres(2),
                SafetyContour = Depth.FromMetres(13.5),
                DeepContour = Depth.FromMetres(30),
            };
            var adjustment = Adjustment(new Water(Lat, (_, _) => -1.0), applyUndeclared: false);

            async Task<double> SafetyLatitude(IDepthAdjustment? depthAdjustment)
            {
                var result = await processor.BuildCoveragePortrayalAsync(
                    new RenderContext { Mariner = mariner, DepthAdjustment = depthAdjustment },
                    TestContext.Current.CancellationToken);
                var safety = result.SubLayers.OfType<IsolineCoverageSubLayer>()
                    .Single(l => l.LayerKey == S102DatasetProcessor.SafetyContourLayerKey);
                return Rendering.Scene.WebMercator.ToLonLat(0, safety.Isolines[0].MercatorCoordinates[1]).Latitude;
            }

            Assert.Equal(50.035, await SafetyLatitude(null), 3);
            Assert.Equal(50.045, await SafetyLatitude(adjustment), 3);
            Assert.Equal(50.035, await SafetyLatitude(null), 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static WaterLevelDepthAdjustment Adjustment(Water water, bool applyUndeclared) =>
        new(new WaterLevelSnapshot(water, T0.AddHours(1)), applyUndeclared, "test");

    private static StyledCoverageLayer Layer(int rows, int columns, Func<int, int, float> depth)
    {
        var values = new float[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
                values[(r * columns) + c] = depth(r, c);
        }

        var metadata = new GridMetadata
        {
            NumRows = rows,
            NumColumns = columns,
            OriginLatitude = -10,
            OriginLongitude = 20,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]> { [S102DepthAdjuster.DepthField] = values },
            },
            NoDataValue = Fill,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
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

    /// <summary>One constant-in-time series per point, on one datum, for a day from <see cref="T0"/>.</summary>
    private sealed class Water(int? datum, Func<double, double, double?> height) : IWaterLevelSource
    {
        public IReadOnlyList<WaterLevelSourceInfo> Datasets { get; } =
            [new WaterLevelSourceInfo("104TEST", WaterLevelSourceKind.Grid, datum)];

        public IReadOnlyList<IWaterLevelSeries> Locate(GeoPosition position) =>
            height(position.Latitude, position.Longitude) is { } h ? [new Series(Datasets[0], h)] : [];

        private sealed class Series(WaterLevelSourceInfo source, double height) : IWaterLevelSeries
        {
            public WaterLevelSourceInfo Source => source;

            public DateTime Start => T0;

            public DateTime End => T0.AddDays(1);

            public WaterLevelReading? ValueAt(DateTime utc) =>
                utc < Start || utc > End ? null : new WaterLevelReading(height, T0);

            public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end) => [];
        }
    }
}
