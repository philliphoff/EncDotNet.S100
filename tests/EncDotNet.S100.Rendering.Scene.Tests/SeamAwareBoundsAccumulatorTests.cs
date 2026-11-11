namespace EncDotNet.S100.Rendering.Scene.Tests;

/// <summary>
/// Tests for <see cref="SeamAwareBoundsAccumulator"/> — the seam-aware EPSG:3857
/// extent used by the headless auto-fit (issue #413). The key case: geometry
/// straddling the ±180° antimeridian must resolve to a narrow, contiguous
/// world-X window instead of a near-global span.
/// </summary>
public sealed class SeamAwareBoundsAccumulatorTests
{
    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    private static (double X, double Y) World(double lon, double lat) =>
        WebMercator.FromLonLat(lon, lat);

    private static double LonSpanDegrees(double minX, double maxX) =>
        (maxX - minX) * RadToDeg / WebMercator.EarthRadius;

    [Fact]
    public void No_geometry_returns_false()
    {
        var acc = new SeamAwareBoundsAccumulator();
        Assert.False(acc.TryResolve(out _, out _, out _, out _));
    }

    [Fact]
    public void Normal_extent_matches_naive_min_max()
    {
        var acc = new SeamAwareBoundsAccumulator();
        var (x0, y0) = World(2.0, 51.0);
        var (x1, y1) = World(5.0, 53.0);
        acc.Add(x0, y0);
        acc.Add(x1, y1);

        Assert.True(acc.TryResolve(out double minX, out double minY, out double maxX, out double maxY));
        Assert.Equal(Math.Min(x0, x1), minX, 3);
        Assert.Equal(Math.Max(x0, x1), maxX, 3);
        Assert.Equal(Math.Min(y0, y1), minY, 3);
        Assert.Equal(Math.Max(y0, y1), maxY, 3);
        // No shift: the window stays within ±180°.
        Assert.True(maxX <= WebMercator.Circumference / 2.0 + 1.0);
    }

    [Fact]
    public void Antimeridian_extent_resolves_to_narrow_shifted_window()
    {
        // Two clusters: one near +179°, one near −179° (10° apart across the
        // seam). The naive min/max would span ~358°; the seam-aware window must
        // be ~10° wide and shifted so maxX exceeds +½ circumference.
        var acc = new SeamAwareBoundsAccumulator();
        foreach (var lon in new[] { 178.0, 179.0, 179.9 })
            acc.Add(World(lon, 70.0).X, World(lon, 70.0).Y);
        foreach (var lon in new[] { -179.9, -179.0, -178.0 })
            acc.Add(World(lon, 71.0).X, World(lon, 71.0).Y);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));

        double spanDeg = LonSpanDegrees(minX, maxX);
        Assert.InRange(spanDeg, 3.0, 30.0); // narrow, not near-global
        // The western cluster was shifted +360°, so maxX runs past +180°.
        Assert.True(maxX > WebMercator.Circumference / 2.0,
            "Seam-shifted window should extend past +½ circumference.");
        // Converting maxX back to longitude yields ~+182° (i.e. −178° + 360°).
        double maxLon = maxX * RadToDeg / WebMercator.EarthRadius;
        Assert.InRange(maxLon, 180.0, 185.0);
    }

    [Fact]
    public void Truly_wide_extent_stays_naive()
    {
        // Data spread broadly across the globe (no single dominant empty arc):
        // the emptiest gap is the seam-wrapping arc, so the naive extent is kept
        // and no false seam-shift occurs.
        var acc = new SeamAwareBoundsAccumulator();
        for (double lon = -170; lon <= 170; lon += 20)
            acc.Add(World(lon, 0.0).X, World(lon, 0.0).Y);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));

        double spanDeg = LonSpanDegrees(minX, maxX);
        Assert.InRange(spanDeg, 300.0, 360.0); // remains wide, unshifted
        Assert.True(maxX <= WebMercator.Circumference / 2.0 + 1.0);
    }

    [Fact]
    public void LonLat_box_marks_occupancy_and_bounds_latitude()
    {
        var acc = new SeamAwareBoundsAccumulator();
        acc.AddLonLatBox(west: 10.0, east: 40.0, south: 50.0, north: 60.0);

        Assert.True(acc.TryResolve(out double minX, out double minY, out double maxX, out double maxY));
        Assert.InRange(LonSpanDegrees(minX, maxX), 29.0, 31.0);
        var (_, ySouth) = World(0, 50.0);
        var (_, yNorth) = World(0, 60.0);
        Assert.Equal(ySouth, minY, 0);
        Assert.Equal(yNorth, maxY, 0);
    }

    [Fact]
    public void LonLat_box_crossing_seam_is_detected()
    {
        // A single coverage box crossing the seam (west > east) must resolve to
        // a narrow shifted window, not a near-global one.
        var acc = new SeamAwareBoundsAccumulator();
        acc.AddLonLatBox(west: 175.0, east: -155.0, south: 60.0, north: 72.0);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));
        Assert.InRange(LonSpanDegrees(minX, maxX), 25.0, 35.0);
        Assert.True(maxX > WebMercator.Circumference / 2.0);
    }

    // Issue #1097: the 0.5° occupancy bins must not round a box's X extent out
    // to the bin edges.

    [Theory]
    [InlineData(10.1, 10.3)]   // inside one bin
    [InlineData(10.4, 10.6)]   // across a bin edge
    [InlineData(-6.125, 0.125)]
    public void Sub_degree_lon_lat_box_resolves_to_its_true_extent(double west, double east)
    {
        var acc = new SeamAwareBoundsAccumulator();
        acc.AddLonLatBox(west, east, south: 50.0, north: 50.2);

        Assert.True(acc.TryResolve(out double minX, out double minY, out double maxX, out double maxY));
        Assert.Equal(World(west, 0).X, minX, 1e-6);
        Assert.Equal(World(east, 0).X, maxX, 1e-6);
        Assert.Equal(World(0, 50.0).Y, minY, 1e-6);
        Assert.Equal(World(0, 50.2).Y, maxY, 1e-6);
    }

    [Fact]
    public void Sub_degree_box_with_vector_geometry_resolves_to_the_union()
    {
        var acc = new SeamAwareBoundsAccumulator();
        var (vx0, vy0) = World(10.0, 50.0);
        var (vx1, vy1) = World(10.15, 50.1);
        acc.Add(vx0, vy0);
        acc.Add(vx1, vy1);
        acc.AddLonLatBox(west: 10.1, east: 10.3, south: 50.05, north: 50.25);

        Assert.True(acc.TryResolve(out double minX, out double minY, out double maxX, out double maxY));
        Assert.Equal(vx0, minX, 1e-6);
        Assert.Equal(World(10.3, 0).X, maxX, 1e-6);
        Assert.Equal(vy0, minY, 1e-6);
        Assert.Equal(World(0, 50.25).Y, maxY, 1e-6);
    }

    [Fact]
    public void Sub_degree_box_crossing_seam_resolves_to_its_true_shifted_extent()
    {
        var acc = new SeamAwareBoundsAccumulator();
        acc.AddLonLatBox(west: 179.9, east: -179.9, south: 60.0, north: 60.2);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));
        Assert.Equal(World(179.9, 0).X, minX, 1e-6);
        Assert.Equal(World(180.1, 0).X, maxX, 1e-6);
    }

    [Fact]
    public void Box_running_past_180_is_treated_as_crossing_the_seam()
    {
        // A grid stored in 0°–360° longitudes reports east > 180°.
        var acc = new SeamAwareBoundsAccumulator();
        acc.AddLonLatBox(west: 179.9, east: 180.1, south: 60.0, north: 60.2);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));
        Assert.Equal(World(179.9, 0).X, minX, 1e-6);
        Assert.Equal(World(180.1, 0).X, maxX, 1e-6);
    }

    [Fact]
    public void Sub_degree_box_across_seam_from_vector_geometry_resolves_to_the_union()
    {
        // A chart cell just west of +180° and a sub-degree coverage just east of
        // −180°: the union is ~1.1° wide across the seam, not near-global.
        var acc = new SeamAwareBoundsAccumulator();
        var (vx0, vy0) = World(179.0, 60.0);
        var (vx1, vy1) = World(179.8, 60.3);
        acc.Add(vx0, vy0);
        acc.Add(vx1, vy1);
        acc.AddLonLatBox(west: -179.95, east: -179.9, south: 60.1, north: 60.2);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));
        Assert.Equal(vx0, minX, 1e-6);
        Assert.Equal(World(180.1, 0).X, maxX, 1e-6);
    }

    [Fact]
    public void Box_spanning_the_whole_world_stays_global()
    {
        var acc = new SeamAwareBoundsAccumulator();
        acc.AddLonLatBox(west: -180.0, east: 180.0, south: -60.0, north: 60.0);

        Assert.True(acc.TryResolve(out double minX, out _, out double maxX, out _));
        Assert.Equal(World(-180, 0).X, minX, 1e-6);
        Assert.Equal(World(180, 0).X, maxX, 1e-6);
    }
}
