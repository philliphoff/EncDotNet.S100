using EncDotNet.S100.Renderers.Mapsui;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that pattern-clip geometry is reduced to polygonal-only components
/// before it reaches OverlayNG. Topology-preserving simplification can collapse
/// thin polygons into linestrings, yielding a mixed-dimension
/// <see cref="GeometryCollection"/> that OverlayNG rejects with
/// "Overlay input is mixed-dimension" — previously failing a whole cell's
/// pattern-fill clip.
/// </summary>
public class PatternClipPolygonalTests
{
    private static readonly GeometryFactory Factory = new();

    private static Polygon Square(double x0, double y0, double size)
    {
        var ring = Factory.CreateLinearRing(
        [
            new Coordinate(x0, y0),
            new Coordinate(x0 + size, y0),
            new Coordinate(x0 + size, y0 + size),
            new Coordinate(x0, y0 + size),
            new Coordinate(x0, y0),
        ]);
        return Factory.CreatePolygon(ring);
    }

    [Fact]
    public void ExtractPolygonal_PolygonInput_ReturnedUnchanged()
    {
        var polygon = Square(0, 0, 10);
        Assert.Same(polygon, MapsuiDisplayListRenderer.ExtractPolygonal(polygon));
    }

    [Fact]
    public void ExtractPolygonal_MixedDimensionCollection_KeepsOnlyPolygons()
    {
        var polygon = Square(0, 0, 10);
        var line = Factory.CreateLineString(
        [
            new Coordinate(20, 20),
            new Coordinate(30, 20),
        ]);
        var point = Factory.CreatePoint(new Coordinate(40, 40));

        var mixed = Factory.CreateGeometryCollection([polygon, line, point]);

        var result = MapsuiDisplayListRenderer.ExtractPolygonal(mixed);

        Assert.NotNull(result);
        Assert.Equal(Dimension.Surface, result!.Dimension);
        // The single polygon component is returned as-is.
        Assert.True(result.EqualsExact(polygon));
    }

    [Fact]
    public void ExtractPolygonal_MultiplePolygonsInCollection_ReturnsMultiPolygon()
    {
        var a = Square(0, 0, 10);
        var b = Square(100, 100, 10);
        var line = Factory.CreateLineString(
        [
            new Coordinate(50, 50),
            new Coordinate(60, 50),
        ]);

        var mixed = Factory.CreateGeometryCollection([a, line, b]);

        var result = MapsuiDisplayListRenderer.ExtractPolygonal(mixed);

        var multi = Assert.IsType<MultiPolygon>(result);
        Assert.Equal(2, multi.NumGeometries);
    }

    [Fact]
    public void ExtractPolygonal_NoPolygonComponent_ReturnsNull()
    {
        var line = Factory.CreateLineString(
        [
            new Coordinate(0, 0),
            new Coordinate(10, 0),
        ]);
        var collection = Factory.CreateGeometryCollection([line]);

        Assert.Null(MapsuiDisplayListRenderer.ExtractPolygonal(collection));
    }

    [Fact]
    public void ExtractPolygonal_Output_IsValidOverlayInput()
    {
        // The mixed-dimension collection would throw inside OverlayNG; the
        // extracted polygonal geometry must overlay cleanly.
        var polygon = Square(0, 0, 10);
        var line = Factory.CreateLineString(
        [
            new Coordinate(2, 2),
            new Coordinate(8, 2),
        ]);
        var mixed = Factory.CreateGeometryCollection([polygon, line]);

        var subject = MapsuiDisplayListRenderer.ExtractPolygonal(mixed)!;
        var clip = Square(5, 0, 10);

        var difference = OverlayNGRobust.Overlay(
            subject, clip, SpatialFunction.Difference);

        Assert.NotNull(difference);
        Assert.False(difference.IsEmpty);
    }
}
