namespace EncDotNet.S100.Datasets.Weather.Tests;

public class WeatherGridLayoutTests
{
    [Fact]
    public void South_to_north_grid_is_unchanged()
    {
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(2, 3, 10, -20, 0.5, 0.25));

        Assert.Equal(new WeatherGrid(2, 3, 10, -20, 0.5, 0.25), layout.Grid);
        Assert.False(layout.RowsFlipped);
        Assert.False(layout.ColumnsFlipped);
        Assert.Equal(0, layout.ColumnRotation);
        Assert.Equal(TestGrids.Indexed(2, 3), layout.Normalize(TestGrids.Indexed(2, 3)));
    }

    [Fact]
    public void North_to_south_rows_are_flipped()
    {
        // Rows at 60°N, 59°N, 58°N (GRIB2 scanning mode bit 2 clear).
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(3, 2, 60, 0, -1, 1));

        Assert.True(layout.RowsFlipped);
        Assert.Equal(58, layout.Grid.OriginLatitude);
        Assert.Equal(1, layout.Grid.SpacingLatitudinal);

        float[] native = [600, 601, 590, 591, 580, 581];
        Assert.Equal([580, 581, 590, 591, 600, 601], layout.Normalize(native));
    }

    [Fact]
    public void Columns_running_west_are_flipped()
    {
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 3, 0, 10, 1, -1));

        Assert.True(layout.ColumnsFlipped);
        Assert.Equal(8, layout.Grid.OriginLongitude);
        Assert.Equal([8f, 9f, 10f], layout.Normalize([10f, 9f, 8f]));
    }

    [Fact]
    public void Global_0_to_360_grid_moves_columns_at_180_and_above_to_the_west()
    {
        // Columns at 0, 90, 180, 270°E.
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 4, 0, 0, 1, 90));

        Assert.Equal(-180, layout.Grid.OriginLongitude);
        Assert.Equal(4, layout.Grid.Columns);
        Assert.Equal(2, layout.ColumnRotation);
        // −180 (180°E), −90 (270°E), 0, 90.
        Assert.Equal([180f, 270f, 0f, 90f], layout.Normalize([0f, 90f, 180f, 270f]));
    }

    [Fact]
    public void Global_quarter_degree_grid_starts_at_minus_180()
    {
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(721, 1440, 90, 0, -0.25, 0.25));

        Assert.Equal(-90, layout.Grid.OriginLatitude);
        Assert.Equal(-180, layout.Grid.OriginLongitude, 9);
        Assert.Equal(1440, layout.Grid.Columns);
        Assert.Equal(720, layout.ColumnRotation);
    }

    [Fact]
    public void Repeated_wrap_around_column_is_dropped()
    {
        // 0, 90, 180, 270 and 360°E: the last repeats the first.
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 5, 0, 0, 1, 90));

        Assert.Equal(4, layout.Grid.Columns);
        Assert.Equal(1, layout.DroppedColumns);
        Assert.Equal([180f, 270f, 0f, 90f], layout.Normalize([0f, 90f, 180f, 270f, 360f]));
    }

    [Fact]
    public void Global_grid_already_in_minus_180_to_180_is_unchanged()
    {
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 4, 0, -180, 1, 90));

        Assert.Equal(-180, layout.Grid.OriginLongitude);
        Assert.Equal(0, layout.ColumnRotation);
        Assert.Equal([1f, 2f, 3f, 4f], layout.Normalize([1f, 2f, 3f, 4f]));
    }

    [Fact]
    public void Grid_entirely_east_of_180_is_shifted_by_minus_360()
    {
        // 185°E to 225°E is 175°W to 135°W.
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(2, 41, 50, 185, 1, 1));

        Assert.Equal(-175, layout.Grid.OriginLongitude, 9);
        Assert.Equal(41, layout.Grid.Columns);
        Assert.Equal(0, layout.ColumnRotation);
        Assert.Equal(TestGrids.Indexed(2, 41), layout.Normalize(TestGrids.Indexed(2, 41)));
    }

    [Fact]
    public void Grid_starting_at_180_is_shifted_by_minus_360()
    {
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 3, 0, 180, 1, 1));

        Assert.Equal(-180, layout.Grid.OriginLongitude, 9);
    }

    [Fact]
    public void Grid_ending_at_180_is_kept()
    {
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 11, 0, 170, 1, 1));

        Assert.Equal(170, layout.Grid.OriginLongitude);
    }

    [Fact]
    public void Grid_from_175E_to_225E_crosses_the_antimeridian_and_is_rejected()
    {
        // 175°E to 225°E is 175°E to 135°W: no contiguous −180 to 180 form.
        var error = Assert.Throws<UnsupportedWeatherGridException>(
            () => WeatherGridLayout.Create(new WeatherNativeGrid(2, 51, 50, 175, 1, 1)));

        Assert.Contains("antimeridian", error.Message, StringComparison.Ordinal);
        Assert.Contains("175° to 225°", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Grid_crossing_180_in_minus_180_to_180_terms_is_rejected()
    {
        // 170°E running east for 20 columns reaches 190°E.
        Assert.Throws<UnsupportedWeatherGridException>(
            () => WeatherGridLayout.Create(new WeatherNativeGrid(1, 21, 0, 170, 1, 1)));
    }

    [Fact]
    public void Grid_crossing_0_in_0_to_360_terms_is_kept()
    {
        // 350°E to 10°E written as 350..370: it's 10°W to 10°E.
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(1, 21, 0, 350, 1, 1));

        Assert.Equal(-10, layout.Grid.OriginLongitude, 9);
    }

    [Theory]
    [InlineData(0, 2, 0.0, 1.0)]
    [InlineData(2, 0, 1.0, 1.0)]
    [InlineData(2, 2, 0.0, 1.0)]
    [InlineData(2, 2, 1.0, 0.0)]
    [InlineData(2, 2, double.NaN, 1.0)]
    public void Invalid_geometry_is_rejected(int rows, int columns, double latitudeIncrement, double longitudeIncrement)
    {
        Assert.Throws<UnsupportedWeatherGridException>(
            () => WeatherGridLayout.Create(new WeatherNativeGrid(rows, columns, 0, 0, latitudeIncrement, longitudeIncrement)));
    }

    [Fact]
    public void Latitudes_beyond_the_pole_are_rejected()
    {
        var error = Assert.Throws<UnsupportedWeatherGridException>(
            () => WeatherGridLayout.Create(new WeatherNativeGrid(4, 2, 89, 0, 1, 1)));

        Assert.Contains("−90° to 90°", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_value_sentinels_and_NaN_become_NaN()
    {
        var layout = WeatherGridLayout.Create(TestGrids.Regular(1, 4));

        var result = layout.Normalize([1f, 9999f, float.NaN, -1f], [9999f, -1f]);

        Assert.Equal(1f, result[0]);
        Assert.True(float.IsNaN(result[1]));
        Assert.True(float.IsNaN(result[2]));
        Assert.True(float.IsNaN(result[3]));
    }

    [Fact]
    public void Normalize_rejects_a_step_of_the_wrong_length()
    {
        var layout = WeatherGridLayout.Create(TestGrids.Regular(2, 2));

        Assert.Throws<ArgumentException>(() => layout.Normalize(new float[3]));
    }

    [Fact]
    public void Flip_and_rotation_combine()
    {
        // Two rows at 10°N then 0°N; columns at 0, 90, 180, 270°E.
        var layout = WeatherGridLayout.Create(new WeatherNativeGrid(2, 4, 10, 0, -10, 90));

        float[] native = [10, 11, 12, 13, 0, 1, 2, 3];
        Assert.Equal([2f, 3f, 0f, 1f, 12f, 13f, 10f, 11f], layout.Normalize(native));
        Assert.Equal(new WeatherGrid(2, 4, 0, -180, 10, 90), layout.Grid);
    }
}
