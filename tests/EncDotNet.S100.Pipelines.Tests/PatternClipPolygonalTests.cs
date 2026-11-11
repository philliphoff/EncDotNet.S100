using EncDotNet.S100.Rendering.Scene;
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
        Assert.Same(polygon, PatternPriorityClipper.ExtractPolygonal(polygon));
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

        var result = PatternPriorityClipper.ExtractPolygonal(mixed);

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

        var result = PatternPriorityClipper.ExtractPolygonal(mixed);

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

        Assert.Null(PatternPriorityClipper.ExtractPolygonal(collection));
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

        var subject = PatternPriorityClipper.ExtractPolygonal(mixed)!;
        var clip = Square(5, 0, 10);

        var difference = OverlayNGRobust.Overlay(
            subject, clip, SpatialFunction.Difference);

        Assert.NotNull(difference);
        Assert.False(difference.IsEmpty);
    }

    [Fact]
    public void OverlayNGRobust_MixedDimensionInput_ThrowsWrappedOverlayFailure()
    {
        // Documents the failure the clipper must tolerate: once its fallback
        // strategies fail too, OverlayNGRobust wraps the "mixed-dimension"
        // ArgumentExceptions in an AggregateException, which a plain
        // catch (ArgumentException) misses.
        var mixed = Factory.CreateGeometryCollection([Square(0, 0, 10), Line(2, 2, 8, 2)]);

        var ex = Assert.Throws<AggregateException>(() =>
            OverlayNGRobust.Overlay(mixed, Square(5, 0, 10), SpatialFunction.Difference));

        Assert.True(PatternPriorityClipper.IsOverlayFailure(ex));
    }

    [Fact]
    public void TryOverlay_MixedDimensionSubject_ClipsPolygonalPart()
    {
        // The shape OverlayNG's snapping fallback produced for UKHO trial cell
        // 101GB0050242S: a Difference result of a polygon plus a collapsed line,
        // which the following land Difference rejected and failed the load.
        var mixed = Factory.CreateGeometryCollection([Square(0, 0, 10), Line(2, 2, 8, 2)]);

        var result = PatternPriorityClipper.TryOverlay(mixed, Square(5, 0, 10), SpatialFunction.Difference);

        Assert.NotNull(result);
        Assert.Equal(Dimension.Surface, result!.Dimension);
        Assert.Equal(50, result.Area, 6);
    }

    [Fact]
    public void TryOverlay_ResultWithoutPolygons_IsEmptyPolygon()
    {
        var lineOnly = Factory.CreateGeometryCollection([Line(0, 0, 10, 0)]);

        var result = PatternPriorityClipper.TryOverlay(lineOnly, Square(5, 5, 10), SpatialFunction.Difference);

        Assert.NotNull(result);
        Assert.True(result!.IsEmpty);
        Assert.IsAssignableFrom<IPolygonal>(result);
    }

    [Fact]
    public void IsOverlayFailure_RecognisesWrappedOverlayFailuresOnly()
    {
        Assert.True(PatternPriorityClipper.IsOverlayFailure(new ArgumentException("x")));
        Assert.True(PatternPriorityClipper.IsOverlayFailure(new TopologyException("x")));
        Assert.True(PatternPriorityClipper.IsOverlayFailure(
            new AggregateException(new ArgumentException("a"), new TopologyException("b"))));
        Assert.False(PatternPriorityClipper.IsOverlayFailure(
            new AggregateException(new ArgumentException("a"), new InvalidOperationException("b"))));
        Assert.False(PatternPriorityClipper.IsOverlayFailure(new InvalidOperationException("x")));
    }

    [Fact]
    public void SimplifyForClip_DuplicatePolygons_SimplifiesInsteadOfThrowing()
    {
        // NOAA ENC US5CHSED (Charleston Harbor) gave a dense pattern area with
        // the same polygon twice. TopologyPreservingSimplifier keys rings by
        // value and threw "An item with the same key has already been added",
        // which failed the whole cell's portrayal.
        var dense = DenseCircle(0, 0, 100, PatternPriorityClipper.MinPointsToSimplify);
        var duplicated = Factory.CreateMultiPolygon([dense, (Polygon)dense.Copy()]);

        var result = PatternPriorityClipper.SimplifyForClip(duplicated);

        Assert.NotSame(duplicated, result);
        Assert.Equal(1, result.NumGeometries);
        Assert.True(result.NumPoints < dense.NumPoints);
        // Simplifying cuts at most the tolerance off every part of the outline.
        var maxLoss = dense.Length * PatternPriorityClipper.SimplifyToleranceMetres;
        Assert.InRange(result.Area, dense.Area - maxLoss, dense.Area);
    }

    [Fact]
    public void SimplifyForClip_ShellRepeatingAnotherPolygonsHole_ReturnsInputInsteadOfThrowing()
    {
        // A ring shared between two different polygons isn't a duplicate
        // component, so the simplifier still sees the same ring twice.
        var island = DenseCircle(0, 0, 100, PatternPriorityClipper.MinPointsToSimplify);
        var hole = Factory.CreateLinearRing(island.Shell.Coordinates);
        var lagoon = Factory.CreatePolygon(Square(-500, -500, 1000).Shell, [hole]);
        var geometry = Factory.CreateMultiPolygon([lagoon, island]);

        var result = PatternPriorityClipper.SimplifyForClip(geometry);

        Assert.Same(geometry, result);
    }

    [Fact]
    public void RemoveDuplicateComponents_NoDuplicates_ReturnsSameInstance()
    {
        var geometry = Factory.CreateMultiPolygon([Square(0, 0, 10), Square(20, 0, 10)]);

        Assert.Same(geometry, PatternPriorityClipper.RemoveDuplicateComponents(geometry));
    }

    [Fact]
    public void RemoveDuplicateComponents_KeepsTheFirstOfEachDistinctComponent()
    {
        var a = Square(0, 0, 10);
        var b = Square(20, 0, 10);
        var geometry = Factory.CreateMultiPolygon([a, b, (Polygon)a.Copy(), (Polygon)b.Copy(), a]);

        var result = Assert.IsType<MultiPolygon>(PatternPriorityClipper.RemoveDuplicateComponents(geometry));

        Assert.Equal(2, result.NumGeometries);
        Assert.Same(a, result.GetGeometryN(0));
        Assert.Same(b, result.GetGeometryN(1));
    }

    private static Polygon DenseCircle(double cx, double cy, double radius, int points)
    {
        var coordinates = new Coordinate[points + 1];
        for (var i = 0; i < points; i++)
        {
            var angle = 2 * Math.PI * i / points;
            coordinates[i] = new Coordinate(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
        }

        coordinates[points] = coordinates[0];
        return Factory.CreatePolygon(coordinates);
    }

    private static LineString Line(double x0, double y0, double x1, double y1) =>
        Factory.CreateLineString([new Coordinate(x0, y0), new Coordinate(x1, y1)]);
}
