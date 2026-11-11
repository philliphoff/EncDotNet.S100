using System.Text;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The US NIC Arctic S-411 chart drew huge wedges (issue #760): it is written
/// longitude first in a 0…360 frame, so vertices east of Greenwich within 90°
/// were read latitude first, and its pack-ice ring encloses the North Pole
/// and other rings cross the frame's seam. These synthetic datasets reproduce
/// each case.
/// </summary>
public sealed class S411PolarAntimeridianGeometryTests
{
    private const double C = WebMercator.Circumference;

    // Longitude first, 0…360, round the North Pole. The vertices at 0°, 45°
    // and 60° are ambiguous on their own; the rest make the dataset
    // longitude first.
    private const string PolarRing =
        "0 80 45 81 60 79 120 80 180 82 240 80 300 78 0 80";

    // Longitude first, ±180 frame, crossing the antimeridian.
    private const string AntimeridianRing =
        "170 60 -170 60 -170 65 170 65 170 60";

    // Longitude first, continuous frame past 180° (US NWS Alaska).
    private const string ContinuousRing =
        "175 60 225 60 225 65 175 65 175 60";

    // Longitude first, 0…360 frame, crossing the 0°/360° seam (Svalbard).
    private const string SeamRing =
        "355 76 5 76 5 79 355 79 355 76";

    [Fact]
    public void Reader_ReadsEveryVertexOfALongitudeFirstDatasetLongitudeFirst()
    {
        var ring = Assert.Single(Open(PolarRing).Features).ExteriorRing;

        Assert.All(ring, p => Assert.InRange(p.Latitude, 78, 82));
        Assert.Equal(45, ring[1].Longitude);
        Assert.Equal(81, ring[1].Latitude);
    }

    [Fact]
    public void PolarRing_FillsTheCapAcrossOneWorldWithoutWedges()
    {
        var areas = Areas(PolarRing);

        var xs = areas.SelectMany(a => a.WorldShell).Select(p => p.X).ToList();
        var ys = areas.SelectMany(a => a.WorldShell).Select(p => p.Y).ToList();

        // One 360° window, in the ring's own 0…360 frame.
        Assert.Equal(0, xs.Min(), 1e-3);
        Assert.Equal(C, xs.Max(), 1e-3);

        // Closed along the top of the Web-Mercator world, never reaching
        // south of the ring's lowest vertex.
        Assert.Equal(WebMercator.FromLonLat(0, WebMercator.MaxLatitude).Y, ys.Max(), 1e-3);
        Assert.True(ys.Min() >= WebMercator.FromLonLat(0, 78).Y - 1e-3);
    }

    [Fact]
    public void PolarRing_OutlineIsSplitAtTheWindowEdgeRatherThanDrawnAcrossTheWorld()
    {
        var lines = Lines(PolarRing);

        Assert.All(lines, l => AssertNoWorldSpanningSegment(l.World));
        var xs = lines.SelectMany(l => l.World).Select(p => p.X).ToList();
        Assert.True(xs.Min() >= -1e-3);
        Assert.True(xs.Max() <= C + 1e-3);
    }

    [Theory]
    [InlineData(AntimeridianRing, -180, 20)]
    [InlineData(SeamRing, 0, 10)]
    public void SeamCrossingRing_IsSplitAtTheEdgeOfItsOwnFrame(string posList, double frameWest, double widthDegrees)
    {
        var areas = Areas(posList);

        // One piece either side of the seam, each next to the rest of a
        // dataset kept in the same frame, with no edge across the world.
        Assert.Equal(2, areas.Count);
        Assert.All(areas, a => AssertNoWorldSpanningSegment(a.WorldShell));
        Assert.All(areas.SelectMany(a => a.WorldShell), p => AssertInFrame(p.X, frameWest));
        var widths = areas.Sum(a => a.WorldShell.Max(p => p.X) - a.WorldShell.Min(p => p.X));
        Assert.Equal(widthDegrees / 360.0 * C, widths, 1e-3);
    }

    [Theory]
    [InlineData(AntimeridianRing, -180)]
    [InlineData(SeamRing, 0)]
    public void SeamCrossingRing_OutlineIsSplitAtTheEdgeOfItsOwnFrame(string posList, double frameWest)
    {
        var lines = Lines(posList);

        Assert.True(lines.Count >= 2);
        Assert.All(lines, l => AssertNoWorldSpanningSegment(l.World));
        Assert.All(lines.SelectMany(l => l.World), p => AssertInFrame(p.X, frameWest));
    }

    [Theory]
    [InlineData(SeamRing, 0)]
    [InlineData(PolarRing, 0)]
    public void ClippedArea_IsNotStrokedAlongItsCut(string posList, double frameWest)
    {
        // The pieces of a clipped ring meet at the frame's edge, where the
        // data continues in the adjacent world copy (issue #773). Stroking the
        // fill drew a line along that cut, so the fill has no stroke and the
        // ring's own edges are outlined by line ops that never run along it.
        var ops = Build(posList, new AreaInstruction { FeatureReference = FeatureId(posList), FillColor = "ICE" });
        var areas = ops.OfType<AreaPaintOp>().ToList();
        var outlines = ops.OfType<LinePaintOp>().ToList();

        Assert.True(areas.Count >= 1);
        Assert.All(areas, a => Assert.Equal(0.0, a.OutlineWidthPx));
        Assert.NotEmpty(outlines);
        double west = WebMercator.FromLonLat(frameWest, 0).X;
        foreach (var line in outlines)
        {
            AssertNoWorldSpanningSegment(line.World);
            for (int i = 1; i < line.World.Count; i++)
            {
                var (a, b) = (line.World[i - 1], line.World[i]);
                bool alongEdge = (Math.Abs(a.X - west) < 1 && Math.Abs(b.X - west) < 1)
                    || (Math.Abs(a.X - west - C) < 1 && Math.Abs(b.X - west - C) < 1);
                Assert.False(alongEdge, $"outline runs along the frame edge at segment {i}");
            }
        }
    }

    [Fact]
    public void PlainArea_KeepsItsStroke()
    {
        var ops = Build(ContinuousRing, new AreaInstruction { FeatureReference = FeatureId(ContinuousRing), FillColor = "ICE" });

        Assert.True(Assert.Single(ops.OfType<AreaPaintOp>()).OutlineWidthPx > 0);
        Assert.Empty(ops.OfType<LinePaintOp>());
    }

    [Fact]
    public void ContinuousFrameRing_ProjectsUnchanged()
    {
        // The US NWS product keeps Alaska in one continuous frame past 180°
        // (issue #413); such a ring is drawn exactly as before.
        var ring = Assert.Single(Open(ContinuousRing).Features).ExteriorRing;

        var area = Assert.Single(Areas(ContinuousRing));

        Assert.Equal(ring.Select(p => WebMercator.FromLonLat(p.Longitude, p.Latitude)), area.WorldShell);
    }

    private static void AssertInFrame(double x, double frameWest)
    {
        double west = WebMercator.FromLonLat(frameWest, 0).X;
        Assert.InRange(x, west - 1e-3, west + C + 1e-3);
    }

    private static void AssertNoWorldSpanningSegment(IReadOnlyList<(double X, double Y)> ring)
    {
        for (int i = 1; i < ring.Count; i++)
            Assert.True(Math.Abs(ring[i].X - ring[i - 1].X) < C / 2, $"segment {i} spans {ring[i].X - ring[i - 1].X} m");
    }

    private static List<AreaPaintOp> Areas(string posList) =>
        Build(posList, new AreaInstruction { FeatureReference = FeatureId(posList), FillColor = "ICE" })
            .OfType<AreaPaintOp>().ToList();

    private static List<LinePaintOp> Lines(string posList) =>
        Build(posList, new LineInstruction { FeatureReference = FeatureId(posList), LineColor = "ICE", LineWidth = 0.32 })
            .OfType<LinePaintOp>().ToList();

    private static IReadOnlyList<PaintOp> Build(string posList, DrawingInstruction instruction)
    {
        var dataset = Open(posList);
        var builder = new VectorSceneBuilder { ResolveColor = static _ => new RgbaColor(0, 0, 0, 255) };
        return builder.Build([instruction], new FeatureGeometryProvider<S411Feature>(dataset.Features)).Ops;
    }

    private static string FeatureId(string posList) => Assert.Single(Open(posList).Features).Id;

    private static S411Dataset Open(string posList)
    {
        var gml = $"""
            <ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice" xmlns:gml="http://www.opengis.net/gml/3.2">
              <ice:IceFeatureMember>
                <ice:seaice gml:id="seaice.None">
                  <ice:iceact>92</ice:iceact>
                  <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="p1">
                    <gml:exterior><gml:LinearRing><gml:posList>{posList}</gml:posList></gml:LinearRing></gml:exterior>
                  </gml:Polygon>
                </ice:seaice>
              </ice:IceFeatureMember>
            </ice:IceDataSet>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(gml));
        return S411Dataset.Open(stream);
    }
}
