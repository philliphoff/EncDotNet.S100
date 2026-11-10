using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="ReliefShading"/>, the optional hillshade over a depth
/// grid, and <see cref="S102Relief"/>, its S-102 display switch.
/// </summary>
public sealed class ReliefShadingTests
{
    private const float NoData = 1_000_000f;

    [Fact]
    public void A_flat_seabed_keeps_the_band_colour()
    {
        var depths = Grid(4, 5, (_, _) => 12f);

        var factors = ReliefShading.Default.Compute(depths, 4, 5, NoData, 5, 5);

        Assert.All(factors, f => Assert.Equal(1f, f, precision: 5));
    }

    [Fact]
    public void A_slope_facing_the_north_west_light_is_lighter_and_one_facing_away_is_darker()
    {
        // Gentle slopes (1 m over 20 m cells): a slope steeper than the light
        // is darker than flat ground whichever way it faces.
        // Depth grows to the east: the seabed falls away eastward, so it faces
        // east, away from a north-west light.
        var facingEast = Grid(3, 3, (_, c) => 10f + c);
        // Depth grows to the west: the seabed faces west, towards the light.
        var facingWest = Grid(3, 3, (_, c) => 12f - c);

        float away = ReliefShading.Default.Compute(facingEast, 3, 3, NoData, 20, 20)[4];
        float towards = ReliefShading.Default.Compute(facingWest, 3, 3, NoData, 20, 20)[4];

        Assert.True(away < 1f, $"Expected a darker cell, got {away}.");
        Assert.True(towards > 1f, $"Expected a lighter cell, got {towards}.");
    }

    [Fact]
    public void Row_zero_is_south()
    {
        // Depth grows with the row, so the seabed falls away to the north and
        // faces north, towards a north-west light.
        var facingNorth = Grid(3, 3, (r, _) => 10f + r);

        float factor = ReliefShading.Default.Compute(facingNorth, 3, 3, NoData, 20, 20)[4];

        Assert.True(factor > 1f, $"Expected a lighter cell, got {factor}.");
    }

    [Fact]
    public void Larger_cells_flatten_the_same_depth_step()
    {
        var depths = Grid(3, 3, (_, c) => 10f + c);

        float fine = ReliefShading.Default.Compute(depths, 3, 3, NoData, 1, 1)[4];
        float coarse = ReliefShading.Default.Compute(depths, 3, 3, NoData, 50, 50)[4];

        Assert.True(fine < coarse && coarse < 1f, $"fine {fine}, coarse {coarse}");
    }

    [Fact]
    public void No_data_cells_stay_unshaded_and_do_not_darken_their_neighbours()
    {
        var depths = Grid(3, 3, (r, c) => r == 1 && c == 2 ? NoData : 8f);

        var factors = ReliefShading.Default.Compute(depths, 3, 3, NoData, 1, 1);

        Assert.All(factors, f => Assert.Equal(1f, f, precision: 5));
    }

    [Fact]
    public void Zero_strength_leaves_every_colour_alone()
    {
        var depths = Grid(3, 3, (r, c) => r * 3f + c);

        var factors = (ReliefShading.Default with { Strength = 0 }).Compute(depths, 3, 3, NoData, 1, 1);

        Assert.All(factors, f => Assert.Equal(1f, f, precision: 5));
    }

    [Fact]
    public void A_depth_count_that_does_not_match_the_grid_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ReliefShading.Default.Compute(new float[5], 2, 3, NoData, 1, 1));
    }

    [Theory]
    [InlineData(1f, 0x80, 0x40, 0x20)]
    [InlineData(0.5f, 0x40, 0x20, 0x10)]
    [InlineData(2f, 0xFF, 0xFF, 0xFF)]
    [InlineData(1.5f, 0xC0, 0xA0, 0x90)]
    public void Shading_an_opaque_pixel_darkens_towards_black_and_lightens_towards_white(
        float factor, int r, int g, int b)
    {
        uint pixel = Pack(0x80, 0x40, 0x20, 0xFF);

        uint shaded = ReliefShading.ShadeRgba8888(pixel, factor);

        Assert.Equal(Pack((byte)r, (byte)g, (byte)b, 0xFF), shaded);
    }

    [Fact]
    public void A_transparent_pixel_is_left_alone()
    {
        Assert.Equal(0u, ReliefShading.ShadeRgba8888(0u, 1.5f));
    }

    [Fact]
    public void A_translucent_pixel_keeps_its_alpha_and_stays_premultiplied()
    {
        // 50 % white, premultiplied: lightening cannot take it past its alpha.
        uint pixel = Pack(0x80, 0x80, 0x80, 0x80);

        uint shaded = ReliefShading.ShadeRgba8888(pixel, 2f);

        Assert.Equal(0x80u, shaded >> 24);
        Assert.True((shaded & 0xFF) <= 0x80);
    }

    [Fact]
    public void The_S102_switch_is_off_unless_set()
    {
        Assert.False(S102Relief.IsOn(null));
        Assert.False(S102Relief.IsOn(new EcdisDisplaySettings()));
        Assert.True(S102Relief.IsOn(new EcdisDisplaySettings
        {
            LayerVisibility = new Dictionary<string, bool> { [S102Relief.LayerKey] = true },
        }));
    }

    [Fact]
    public void Applying_the_S102_relief_measures_geographic_cells_on_the_ground()
    {
        // 0.0001° cells at the equator are about 11.1 m square.
        var depths = Grid(3, 3, (_, c) => 10f + c);
        var layer = Layer(depths, 3, 3, spacing: 0.0001);

        var shaded = S102Relief.Apply(layer, nativeToWgs84: null, ReliefShading.Default);

        var expected = ReliefShading.Default.Compute(depths, 3, 3, NoData, 11.1195, 11.1195);
        Assert.NotNull(shaded.ReliefShade);
        Assert.Equal(expected[4], shaded.ReliefShade![4], precision: 4);
        Assert.Same(layer.ColorScheme, shaded.ColorScheme);
        Assert.Same(layer.Coverage, shaded.Coverage);
    }

    private static StyledCoverageLayer Layer(float[] depths, int rows, int columns, double spacing)
    {
        var metadata = new GridMetadata
        {
            NumRows = rows,
            NumColumns = columns,
            OriginLongitude = 0,
            OriginLatitude = 0,
            SpacingLongitudinal = spacing,
            SpacingLatitudinal = spacing,
        };
        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]> { [S102DepthAdjuster.DepthField] = depths },
            },
            ColorScheme = new CoverageColorScheme { FieldName = S102DepthAdjuster.DepthField, Bands = [] },
            NoDataValue = NoData,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
    }

    private static float[] Grid(int rows, int columns, Func<int, int, float> value)
    {
        var grid = new float[rows * columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                grid[r * columns + c] = value(r, c);
        return grid;
    }

    private static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | (g << 8) | (b << 16) | (a << 24));
}
