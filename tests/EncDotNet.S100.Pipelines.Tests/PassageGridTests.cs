using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Navigation;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="PassageGrid"/>: the grid lookup the passage
/// sources share, and the producer quirks it absorbs (#943).
/// </summary>
public class PassageGridTests
{
    private static readonly ProjNetCrsTransformFactory Transforms = new();

    [Theory]
    [InlineData(-175.0, 185.0)]
    [InlineData(175.0, 175.0)]
    [InlineData(185.0, 185.0)]
    public void A_grid_across_the_antimeridian_is_found_from_either_side(double longitude, double expectedX)
    {
        // 170°…190°, written in the 0…360° frame.
        var grid = PassageGrid.Create(50, 170, 0.5, 0.5, 4, 40, 4326, null, Transforms);

        var (x, y) = grid.ToGrid(51, longitude);

        Assert.Equal(expectedX, x, 9);
        Assert.Equal(51, y);
    }

    [Fact]
    public void A_grid_in_the_minus_180_to_180_frame_is_found_from_0_to_360()
    {
        var grid = PassageGrid.Create(50, -135, 0.1, 0.1, 10, 10, null, null, Transforms);

        Assert.Equal(-134.5, grid.ToGrid(50.5, 225.5).X, 9);
        Assert.True(grid.Intersects(new BoundingBox(0, 0, 0, 0), 50.2, 225.2, 50.3, 225.3));
        Assert.False(grid.Intersects(new BoundingBox(0, 0, 0, 0), 50.2, 0, 50.3, 1));
    }

    [Fact]
    public void A_swapped_grid_has_its_georeferencing_exchanged_back()
    {
        // Written longitude first: origin (−135, 50), 3 "latitudinal" points
        // along longitude and 2 "longitudinal" points along latitude.
        var swapped = new AxisOrderAssessment
        {
            Outcome = AxisOrderOutcome.Swapped,
            Evidence = AxisOrderEvidence.PhysicalBounds,
            PositionCount = 4,
        };

        var grid = PassageGrid.Create(-135, 50, 0.2, 0.1, 3, 2, 4326, swapped, Transforms);

        Assert.Equal((50.0, -135.0, 0.1, 0.2, 2, 3), (grid.OriginY, grid.OriginX, grid.SpacingY, grid.SpacingX, grid.Rows, grid.Columns));
    }

    [Fact]
    public void Another_geographic_crs_is_read_as_degrees()
    {
        // ETRS89, which the transform factory can't reproject.
        var grid = PassageGrid.Create(50, -1.5, 0.1, 0.1, 10, 10, 4258, null, Transforms);

        Assert.True(grid.IsGeographic);
        Assert.Equal((-1.25, 50.25), grid.ToGrid(50.25, -1.25));
    }

    [Fact]
    public void A_projected_grid_is_reprojected()
    {
        // UTM zone 30N.
        var grid = PassageGrid.Create(5_600_000, 600_000, 10, 10, 100, 100, 32630, null, Transforms);

        var (x, y) = grid.ToGrid(50.5, -1.5);
        var back = grid.ToWgs84(x, y);

        Assert.False(grid.IsGeographic);
        Assert.Null(grid.GeographicExtent);
        Assert.Equal(50.5, back.Latitude, 6);
        Assert.Equal(-1.5, back.Longitude, 6);
    }
}
