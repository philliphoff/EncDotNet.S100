using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Tests;

public class GeoBoundsTests
{
    [Fact]
    public void Union_of_disjoint_boxes_spans_both()
    {
        var a = new GeoBounds(10, -80, 20, -70);
        var b = new GeoBounds(15, -60, 30, -50);

        Assert.Equal(new GeoBounds(10, -80, 30, -50), a.Union(b));
    }

    [Fact]
    public void Union_takes_the_short_way_across_the_antimeridian()
    {
        var west = new GeoBounds(51, 172, 53, 179);
        var east = new GeoBounds(51, -179, 54, -170);

        var union = west.Union(east);

        Assert.True(union.CrossesAntimeridian);
        Assert.Equal(new GeoBounds(51, 172, 54, -170), union);
        Assert.Equal(18, union.LongitudeSpan, 6);
    }

    [Fact]
    public void Union_with_contained_box_returns_container()
    {
        var outer = new GeoBounds(0, 170, 10, -170);
        var inner = new GeoBounds(2, 175, 8, 179);

        Assert.Equal(new GeoBounds(0, 170, 10, -170), outer.Union(inner));
        Assert.Equal(new GeoBounds(0, 170, 10, -170), inner.Union(outer));
    }

    [Fact]
    public void FromPositions_detects_antimeridian_crossing()
    {
        var bounds = GeoBounds.FromPositions(
        [
            new GeoPosition(51, 178),
            new GeoPosition(52, -178),
            new GeoPosition(53, 179.5),
        ]);

        Assert.Equal(new GeoBounds(51, 178, 53, -178), bounds);
    }

    [Fact]
    public void FromPositions_returns_null_when_empty()
    {
        Assert.Null(GeoBounds.FromPositions([]));
    }

    [Fact]
    public void Contains_and_Intersects_respect_antimeridian()
    {
        var aleutians = new GeoBounds(51, 172, 54, -170);

        Assert.True(aleutians.Contains(new GeoPosition(52, 179)));
        Assert.True(aleutians.Contains(new GeoPosition(52, -175)));
        Assert.False(aleutians.Contains(new GeoPosition(52, 0)));
        Assert.True(aleutians.Intersects(new GeoBounds(50, -172, 52, -160)));
        Assert.False(aleutians.Intersects(new GeoBounds(50, -160, 52, -150)));
    }
}
