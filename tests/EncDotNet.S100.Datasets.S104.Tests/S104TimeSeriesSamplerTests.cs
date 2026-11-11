namespace EncDotNet.S100.Datasets.S104.Tests;

public class S104TimeSeriesSamplerTests
{
    private const float Fill = S104TimeSeriesSampler.FillValue;

    // 2x2 grid, origin (50.0, -1.0), 0.01 deg spacing, three falling-tide steps.
    private static S104Dataset BuildDataset()
    {
        WaterLevelCoverage Step(DateTime time, float baseHeight) => new()
        {
            OriginLatitude = 50.0,
            OriginLongitude = -1.0,
            SpacingLatitudinal = 0.01,
            SpacingLongitudinal = 0.01,
            NumPointsLatitudinal = 2,
            NumPointsLongitudinal = 2,
            TimePoint = time,
            Values =
            [
                new WaterLevelValue(baseHeight + 0.0f, 1),
                new WaterLevelValue(baseHeight + 0.1f, 1),
                new WaterLevelValue(baseHeight + 0.2f, 1),
                new WaterLevelValue(baseHeight + 0.3f, 1),
            ],
        };

        return new S104Dataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = 2,
            Coverages =
            [
                Step(new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc), 3.0f),
                Step(new DateTime(2021, 4, 1, 0, 10, 0, DateTimeKind.Utc), 2.5f),
                Step(new DateTime(2021, 4, 1, 0, 20, 0, DateTimeKind.Utc), 2.0f),
            ],
        };
    }

    [Fact]
    public void Sample_returns_series_for_all_steps_when_window_unbounded()
    {
        var dataset = BuildDataset();

        var series = S104TimeSeriesSampler.Sample(dataset, 50.0, -1.0);

        Assert.NotNull(series);
        Assert.Equal(0, series!.Row);
        Assert.Equal(0, series.Col);
        Assert.Equal(3, series.Points.Count);
        Assert.Equal(3.0, series.Points[0].HeightMeters);
        Assert.Equal(2.5, series.Points[1].HeightMeters);
        Assert.Equal(2.0, series.Points[2].HeightMeters);
    }

    [Fact]
    public void Sample_selects_nearest_cell_and_reports_cell_centre()
    {
        var dataset = BuildDataset();

        // Closer to the (row 1, col 1) node at (50.01, -0.99).
        var series = S104TimeSeriesSampler.Sample(dataset, 50.009, -0.991);

        Assert.NotNull(series);
        Assert.Equal(1, series!.Row);
        Assert.Equal(1, series.Col);
        Assert.Equal(50.01, series.CellLatitude, 6);
        Assert.Equal(-0.99, series.CellLongitude, 6);
        // baseHeight + 0.3 at cell (1,1) for the first step.
        Assert.Equal(3.3, series.Points[0].HeightMeters!.Value, 5);
    }

    [Fact]
    public void Sample_filters_steps_outside_window()
    {
        var dataset = BuildDataset();

        var series = S104TimeSeriesSampler.Sample(
            dataset,
            50.0,
            -1.0,
            from: new DateTime(2021, 4, 1, 0, 5, 0, DateTimeKind.Utc),
            to: new DateTime(2021, 4, 1, 0, 15, 0, DateTimeKind.Utc));

        Assert.NotNull(series);
        var point = Assert.Single(series!.Points);
        Assert.Equal(new DateTime(2021, 4, 1, 0, 10, 0, DateTimeKind.Utc), point.Time);
        Assert.Equal(2.5, point.HeightMeters);
    }

    [Fact]
    public void Sample_reports_null_height_for_fill_value()
    {
        var dataset = new S104Dataset
        {
            DataCodingFormat = 2,
            Coverages =
            [
                new WaterLevelCoverage
                {
                    OriginLatitude = 50.0,
                    OriginLongitude = -1.0,
                    SpacingLatitudinal = 0.01,
                    SpacingLongitudinal = 0.01,
                    NumPointsLatitudinal = 1,
                    NumPointsLongitudinal = 1,
                    TimePoint = new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new WaterLevelValue(Fill, 0)],
                },
            ],
        };

        var series = S104TimeSeriesSampler.Sample(dataset, 50.0, -1.0);

        Assert.NotNull(series);
        Assert.Null(series!.Points[0].HeightMeters);
    }

    [Fact]
    public void Sample_returns_null_when_point_outside_grid()
    {
        var dataset = BuildDataset();

        var series = S104TimeSeriesSampler.Sample(dataset, 60.0, 10.0);

        Assert.Null(series);
    }

    [Fact]
    public void Sample_returns_null_for_unsupported_coding_format()
    {
        var dataset = new S104Dataset
        {
            DataCodingFormat = 3,
            Coverages = [],
        };

        var series = S104TimeSeriesSampler.Sample(dataset, 50.0, -1.0);

        Assert.Null(series);
    }

    [Fact]
    public void Sample_throws_for_null_dataset()
    {
        Assert.Throws<ArgumentNullException>(
            () => S104TimeSeriesSampler.Sample(null!, 0, 0));
    }

    [Fact]
    public void Sample_orders_points_by_ascending_time()
    {
        var dataset = new S104Dataset
        {
            DataCodingFormat = 2,
            Coverages =
            [
                new WaterLevelCoverage
                {
                    OriginLatitude = 50.0,
                    OriginLongitude = -1.0,
                    SpacingLatitudinal = 0.01,
                    SpacingLongitudinal = 0.01,
                    NumPointsLatitudinal = 1,
                    NumPointsLongitudinal = 1,
                    TimePoint = new DateTime(2021, 4, 1, 0, 20, 0, DateTimeKind.Utc),
                    Values = [new WaterLevelValue(2.0f, 1)],
                },
                new WaterLevelCoverage
                {
                    OriginLatitude = 50.0,
                    OriginLongitude = -1.0,
                    SpacingLatitudinal = 0.01,
                    SpacingLongitudinal = 0.01,
                    NumPointsLatitudinal = 1,
                    NumPointsLongitudinal = 1,
                    TimePoint = new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new WaterLevelValue(3.0f, 1)],
                },
            ],
        };

        var series = S104TimeSeriesSampler.Sample(dataset, 50.0, -1.0);

        Assert.NotNull(series);
        Assert.Equal(2, series!.Points.Count);
        Assert.True(series.Points[0].Time < series.Points[1].Time);
        Assert.Equal(3.0, series.Points[0].HeightMeters);
        Assert.Equal(2.0, series.Points[1].HeightMeters);
    }

    [Theory]
    [InlineData(0, 2, 0.01, 0.01)]
    [InlineData(2, 0, 0.01, 0.01)]
    [InlineData(2, 2, 0.0, 0.01)]
    [InlineData(2, 2, 0.01, 0.0)]
    public void Sample_returns_null_for_degenerate_grid_geometry(
        int numLat, int numLon, double spacingLat, double spacingLon)
    {
        var dataset = new S104Dataset
        {
            DataCodingFormat = 2,
            Coverages =
            [
                new WaterLevelCoverage
                {
                    OriginLatitude = 50.0,
                    OriginLongitude = -1.0,
                    SpacingLatitudinal = spacingLat,
                    SpacingLongitudinal = spacingLon,
                    NumPointsLatitudinal = numLat,
                    NumPointsLongitudinal = numLon,
                    TimePoint = new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new WaterLevelValue(3.0f, 1)],
                },
            ],
        };

        Assert.Null(S104TimeSeriesSampler.Sample(dataset, 50.0, -1.0));
    }
}
