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
