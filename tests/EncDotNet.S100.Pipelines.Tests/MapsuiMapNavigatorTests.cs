using EncDotNet.S100.DataModel;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;

namespace EncDotNet.S100.Pipelines.Tests;

public sealed class MapsuiMapNavigatorTests
{
    [Fact]
    public void Constructor_NullMap_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MapsuiMapNavigator(null!));
    }

    [Fact]
    public void SetViewportToExtent_SizedViewport_AppliesExactExtent()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        var extent = new MRect(-60, -60, 60, 60);

        navigation.SetViewportToExtent(extent);

        AssertExtent(extent, map.Navigator.Viewport.ToExtent());
    }

    [Fact]
    public void SetViewportToCenterAndResolution_ValidValues_AppliesWithoutAnimation()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);

        navigation.SetViewportToCenterAndResolution(new MPoint(125, -75), 4);

        Assert.Equal(125, map.Navigator.Viewport.CenterX, 6);
        Assert.Equal(-75, map.Navigator.Viewport.CenterY, 6);
        Assert.Equal(4, map.Navigator.Viewport.Resolution, 6);
    }

    [Fact]
    public void SetViewportToCenterAndResolution_ZoomLimited_LandsOnTheRequestedCentre()
    {
        // Issue #749: when Mapsui clamps the resolution itself it pulls the
        // centre back toward the previous viewport's centre.
        using var map = SizedMap();
        map.Navigator.OverrideZoomBounds = new MMinMax(1, 100);
        var navigation = new MapsuiMapNavigator(map);
        navigation.SetViewportToCenterAndResolution(new MPoint(5_000, 5_000), 50);

        navigation.SetViewportToCenterAndResolution(new MPoint(0, 0), 10_000);

        Assert.Equal(0, map.Navigator.Viewport.CenterX, 6);
        Assert.Equal(0, map.Navigator.Viewport.CenterY, 6);
        Assert.Equal(100, map.Navigator.Viewport.Resolution, 6);
    }

    [Fact]
    public void SetViewportToExtent_ZoomLimited_CentresTheExtent()
    {
        using var map = SizedMap();
        map.Navigator.OverrideZoomBounds = new MMinMax(1, 100);
        var navigation = new MapsuiMapNavigator(map);
        navigation.SetViewportToCenterAndResolution(new MPoint(5_000, 5_000), 50);

        navigation.SetViewportToExtent(new MRect(-1_000_000, -1_000_000, 1_000_000, 1_000_000));

        Assert.Equal(0, map.Navigator.Viewport.CenterX, 6);
        Assert.Equal(0, map.Navigator.Viewport.CenterY, 6);
        Assert.Equal(100, map.Navigator.Viewport.Resolution, 6);
    }

    [Fact]
    public void SetRotation_ValidDegrees_AppliesWithoutAnimation()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);

        navigation.SetRotation(37);

        Assert.Equal(37, map.Navigator.Viewport.Rotation, 6);
    }

    [Fact]
    public void CenterOn_ValidWgs84Position_PreservesResolution()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        navigation.SetViewportToCenterAndResolution(new MPoint(0, 0), 12);
        var position = new GeoPosition(50.45, -3.58);

        navigation.CenterOn(position, durationMilliseconds: 0);

        var (expectedX, expectedY) = SphericalMercator.FromLonLat(
            position.Longitude,
            position.Latitude);
        Assert.Equal(expectedX, map.Navigator.Viewport.CenterX, 6);
        Assert.Equal(expectedY, map.Navigator.Viewport.CenterY, 6);
        Assert.Equal(12, map.Navigator.Viewport.Resolution, 6);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void CenterOn_InvalidWgs84Position_DoesNotChangeViewport(
        double latitude,
        double longitude)
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        navigation.SetViewportToCenterAndResolution(new MPoint(10, 20), 3);

        navigation.CenterOn(
            new GeoPosition(latitude, longitude),
            durationMilliseconds: 0);

        Assert.Equal(10, map.Navigator.Viewport.CenterX, 6);
        Assert.Equal(20, map.Navigator.Viewport.CenterY, 6);
        Assert.Equal(3, map.Navigator.Viewport.Resolution, 6);
    }

    [Fact]
    public void TryGetViewportCenterWgs84_UnsizedViewport_ReturnsNull()
    {
        using var map = new Map();
        var navigation = new MapsuiMapNavigator(map);

        Assert.Null(navigation.TryGetViewportCenterWgs84());
    }

    [Fact]
    public void TryGetViewportResolution_UnsizedViewport_ReturnsNull()
    {
        using var map = new Map();
        var navigation = new MapsuiMapNavigator(map);

        Assert.Null(navigation.TryGetViewportResolution());
    }

    [Fact]
    public void TryGetViewportResolution_SizedViewport_ReturnsAppliedResolution()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        navigation.SetViewportToCenterAndResolution(new MPoint(0, 0), 7);

        Assert.Equal(7, navigation.TryGetViewportResolution()!.Value, 6);
    }

    [Fact]
    public void TryGetViewportCenterWgs84_SizedViewport_ReturnsProjectedCenter()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        var expected = new GeoPosition(50.45, -3.58);
        var (x, y) = SphericalMercator.FromLonLat(
            expected.Longitude,
            expected.Latitude);
        navigation.SetViewportToCenterAndResolution(new MPoint(x, y), 5);

        var actual = navigation.TryGetViewportCenterWgs84();

        Assert.NotNull(actual);
        Assert.Equal(expected.Latitude, actual.Value.Latitude, 6);
        Assert.Equal(expected.Longitude, actual.Value.Longitude, 6);
    }

    [Fact]
    public void ZoomToExtent_DefaultTimingAddsTenPercentPaddingImmediately()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        var extent = new MRect(-50, -50, 50, 50);

        navigation.ZoomToExtent(extent);

        AssertExtent(new MRect(-60, -60, 60, 60), map.Navigator.Viewport.ToExtent());
    }

    [Fact]
    public void ZoomToExtent_ZeroDurationAddsTenPercentPaddingImmediately()
    {
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        var extent = new MRect(-50, -50, 50, 50);

        navigation.ZoomToExtent(extent, durationMilliseconds: 0);

        AssertExtent(new MRect(-60, -60, 60, 60), map.Navigator.Viewport.ToExtent());
    }

    [Fact]
    public void ZoomToExtent_ClipsAnExtentReachingPastTheTopOfTheWorld()
    {
        // A polar dataset's extent runs towards the pole (issue #760); only the
        // part inside the Web-Mercator world is framed.
        using var map = SizedMap();
        var navigation = new MapsuiMapNavigator(map);
        double top = SphericalMercator.FromLonLat(0, 85.05112878).y;

        navigation.ZoomToExtent(new MRect(-50, top - 100, 50, top + 1e7), durationMilliseconds: 0);

        AssertExtent(new MRect(-60, top - 110, 60, top + 10), map.Navigator.Viewport.ToExtent());
    }

    private static Map SizedMap()
    {
        var map = new Map();
        map.Navigator.SetSize(120, 120);
        return map;
    }

    private static void AssertExtent(MRect expected, MRect? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.MinX, actual.MinX, 6);
        Assert.Equal(expected.MinY, actual.MinY, 6);
        Assert.Equal(expected.MaxX, actual.MaxX, 6);
        Assert.Equal(expected.MaxY, actual.MaxY, 6);
    }
}
