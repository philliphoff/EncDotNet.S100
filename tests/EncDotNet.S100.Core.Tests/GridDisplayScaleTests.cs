using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Core.Tests;

/// <summary>The grid-spacing fallback for a coverage's minimum display scale.</summary>
public sealed class GridDisplayScaleTests
{
    private static GridMetadata Grid(double latSpacing, double lonSpacing) => new()
    {
        NumRows = 10,
        NumColumns = 10,
        OriginLatitude = 0,
        OriginLongitude = 0,
        SpacingLatitudinal = latSpacing,
        SpacingLongitudinal = lonSpacing,
    };

    [Fact]
    public void A_geographic_grid_converts_its_latitude_spacing_to_metres()
    {
        // 0.0001° ≈ 11.1 m (the IC-ENC UK S-102 trial cells): four nodes per
        // 0.28 mm pixel is about 1:159 000.
        var scale = GridDisplayScale.FromGrid(Grid(0.0001, 0.0001), geographic: true);

        Assert.Equal(159_029, scale);
    }

    [Fact]
    public void A_projected_grid_uses_its_finer_axis_in_metres()
    {
        var scale = GridDisplayScale.FromGrid(Grid(16, 32), geographic: false);

        Assert.Equal(228_571, scale);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unusable_spacing_gives_no_cutoff(double spacing)
    {
        Assert.Null(GridDisplayScale.FromSpacing(spacing));
    }
}
