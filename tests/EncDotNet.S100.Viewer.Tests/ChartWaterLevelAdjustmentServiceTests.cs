using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Chart-wide water level adjustment's state (#1132 phase 3,
/// <c>docs/design/chart-wide-water-level-adjustment.md</c> §5–§7): what the
/// map is asked to draw, and what the indicator is told was drawn.
/// </summary>
public sealed class ChartWaterLevelAdjustmentServiceTests
{
    private const int Lat = 23;
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Nothing_is_requested_while_off()
    {
        var (service, settings, _, _) = Create();

        Assert.False(service.IsEnabled);
        Assert.Null(service.Requested);
        Assert.Null(service.Current);
        Assert.Equal(WaterLevelAdjustmentMode.Off, settings.WaterLevelAdjustment);
    }

    [Fact]
    public void Turning_it_on_requests_the_water_level_at_the_view_time()
    {
        var (service, settings, _, _) = Create();
        var requests = 0;
        service.RequestChanged += (_, _) => requests++;

        settings.Set(WaterLevelAdjustmentMode.AtViewTime);

        Assert.Equal(1, requests);
        var requested = Assert.IsType<EncDotNet.S100.Datasets.Pipelines.Navigation.WaterLevelDepthAdjustment>(service.Requested);
        Assert.Equal(T0.AddHours(1), requested.Field.Basis.Start);
        Assert.Equal(-0.5, requested.At(0, 0, Lat)!.Value.HeightMeters, 6);

        // Not drawn yet: the indicator has nothing applied to report.
        Assert.Null(service.Current);
    }

    [Fact]
    public void Marking_the_request_applied_reports_its_time_and_a_stale_one_is_ignored()
    {
        var (service, settings, time, _) = Create();
        settings.Set(WaterLevelAdjustmentMode.AtViewTime);
        var first = service.Requested;

        time.Set(T0.AddHours(2));
        var second = service.Requested;
        Assert.NotSame(first, second);

        service.MarkApplied(first);
        Assert.Null(service.Current);

        service.MarkApplied(second);
        Assert.Equal(T0.AddHours(2), service.Current!.AppliedTime);
        Assert.Single(service.Current.Footprint);
    }

    [Fact]
    public void Turning_it_off_clears_the_state_at_once_and_requests_charted_depths()
    {
        var (service, settings, _, _) = Create();
        settings.Set(WaterLevelAdjustmentMode.AtViewTime);
        service.MarkApplied(service.Requested);
        Assert.NotNull(service.Current);

        settings.Set(WaterLevelAdjustmentMode.Off);

        Assert.Null(service.Current);
        Assert.Null(service.Requested);
    }

    [Fact]
    public void The_override_and_the_catalog_change_the_request()
    {
        var (service, settings, _, catalog) = Create();
        settings.Set(WaterLevelAdjustmentMode.AtViewTime);
        var first = service.Requested!;

        settings.SetUndeclared(true);
        var second = service.Requested!;
        Assert.NotEqual(first.Key, second.Key);

        catalog.Change();
        Assert.NotEqual(second.Key, service.Requested!.Key);

        catalog.Remove();
        Assert.Null(service.Requested);
    }

    [Fact]
    public void Boundary_rings_merge_overlapping_footprints()
    {
        CoverageArea Square(double west, double east) => new()
        {
            ExteriorRing = [new(0, west), new(0, east), new(1, east), new(1, west), new(0, west)],
        };

        var (edges, seams) = AdjustedAreaBoundaryController.Lines([Square(0, 2), Square(1, 3), Square(10, 11)], [], mask: null);

        Assert.Equal(2, edges.Count);
        Assert.Empty(seams);
    }

    [Fact]
    public void The_seam_marks_where_S104_hands_over_to_predictions()
    {
        CoverageArea Square(double west, double east) => new()
        {
            ExteriorRing = [new(0, west), new(0, east), new(1, east), new(1, west), new(0, west)],
        };

        var s104 = Square(0, 2);
        var predicted = Square(1, 3);
        var (edges, seams) = AdjustedAreaBoundaryController.Lines([s104, predicted], [predicted], mask: null);

        Assert.NotEmpty(edges);
        // S-104's east edge, at 2°E, inside the predicted square.
        var seam = Assert.Single(seams);
        var (x, _) = Mapsui.Projections.SphericalMercator.FromLonLat(2, 0.5);
        Assert.All(seam, c => Assert.Equal(x, c.X, 1e-3));
    }

    [Fact]
    public void The_edge_is_clipped_to_charted_water_and_left_alone_where_nothing_is_charted()
    {
        CoverageArea Square(double west, double east) => new()
        {
            ExteriorRing = [new(0, west), new(0, east), new(1, east), new(1, west), new(0, west)],
        };

        // Land west of 0.5°E; uncharted elsewhere.
        var mask = new HalfLand();
        var (clipped, _) = AdjustedAreaBoundaryController.Lines([Square(0, 1)], [], mask);
        var (unclipped, _) = AdjustedAreaBoundaryController.Lines([Square(0, 1)], [], mask: null);

        var (landX, _) = Mapsui.Projections.SphericalMercator.FromLonLat(0.45, 0.5);
        Assert.All(clipped.SelectMany(l => l), c => Assert.True(c.X > landX));
        Assert.Contains(unclipped.SelectMany(l => l), c => c.X < landX);
    }

    private sealed class HalfLand : IWaterMask
    {
        public WaterMaskClass Classify(GeoPosition position) =>
            position.Longitude < 0.5 ? WaterMaskClass.Land : WaterMaskClass.Uncharted;
    }

    private static (ChartWaterLevelAdjustmentService, Settings, Time, Catalog) Create()
    {
        var settings = new Settings();
        var time = new Time(T0.AddHours(1));
        var catalog = new Catalog();
        return (new ChartWaterLevelAdjustmentService(settings, time, catalog), settings, time, catalog);
    }

    private sealed class Settings : IWaterLevelAdjustmentSettings
    {
        public WaterLevelAdjustmentMode WaterLevelAdjustment { get; private set; }

        public bool WaterLevelApplyUndeclaredDatum { get; private set; }

        public bool UseTidePredictions { get; set; }

        public event Action? WaterLevelAdjustmentChanged;

        public void Set(WaterLevelAdjustmentMode mode)
        {
            WaterLevelAdjustment = mode;
            WaterLevelAdjustmentChanged?.Invoke();
        }

        public void SetUndeclared(bool value)
        {
            WaterLevelApplyUndeclaredDatum = value;
            WaterLevelAdjustmentChanged?.Invoke();
        }
    }

    private sealed class Time(DateTime current) : IViewTime
    {
        public DateTimeOffset Current { get; private set; } = new(current);

        public event EventHandler? Changed;

        public void Set(DateTime time)
        {
            Current = new DateTimeOffset(time);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Catalog : IWaterLevelCatalog
    {
        private int _generation;

        public IWaterLevelSource? WaterLevel { get; private set; } = new Water();

        public string Key => _generation.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public event EventHandler? Changed;

        public void Change()
        {
            _generation++;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Remove()
        {
            WaterLevel = null;
            Change();
        }
    }

    private sealed class Water : IWaterLevelSource
    {
        private static readonly WaterLevelSourceInfo Info = new("104TEST", WaterLevelSourceKind.Grid, Lat);

        public IReadOnlyList<WaterLevelSourceInfo> Datasets { get; } = [Info];

        public IReadOnlyList<CoverageArea> Footprint { get; } =
        [
            new CoverageArea { ExteriorRing = [new(-1, -1), new(-1, 1), new(1, 1), new(1, -1), new(-1, -1)] },
        ];

        public IReadOnlyList<IWaterLevelSeries> Locate(GeoPosition position) => [new Series()];

        private sealed class Series : IWaterLevelSeries
        {
            public WaterLevelSourceInfo Source => Info;

            public DateTime Start => T0;

            public DateTime End => T0.AddDays(1);

            public WaterLevelReading? ValueAt(DateTime utc) => new WaterLevelReading(-0.5, utc);

            public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end) => [];
        }
    }
}
