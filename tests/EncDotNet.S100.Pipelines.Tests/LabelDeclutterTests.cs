using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the tiled "B" subsystem's live label plane: the
/// deterministic, priority-driven <see cref="LabelDeclutterer"/> (S-100 Part 9
/// overlap avoidance) and the renderer support it relies on. These are pure,
/// machine-independent Skia rasters / geometry.
/// </summary>
public class LabelDeclutterTests
{
    private static TextPaintOp Text(string id, double lon, double lat, string text = "LABEL") =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            Text = text,
            FontSizePx = 12,
            ForeColor = new RgbaColor(0, 0, 0, 255),
            HorizontalAlignment = TextHorizontalAlignment.Start,
            VerticalAlignment = TextVerticalAlignment.Top,
        };

    private static PointPaintOp Point(string id, double lon, double lat) =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            FallbackColor = new RgbaColor(220, 20, 60, 255),
            FallbackScale = 2.0,
        };

    private static Viewport Centred(double lon, double lat, double span, int px = 512) =>
        new()
        {
            MinLongitude = lon - span / 2,
            MaxLongitude = lon + span / 2,
            MinLatitude = lat - span / 2,
            MaxLatitude = lat + span / 2,
            WidthPixels = px,
            HeightPixels = px,
            ScaleDenominator = 50000,
        };

    private static IReadOnlySet<TextPaintOp> Declutter(VectorScene scene, Viewport viewport)
    {
        var cull = new SKRect(-256, -256, viewport.WidthPixels + 256, viewport.HeightPixels + 256);
        return new LabelDeclutterer().Declutter(
            scene, viewport, cull, honorScaleVisibility: false,
            anchorRotationDegrees: 0, centerX: viewport.WidthPixels / 2f, centerY: viewport.HeightPixels / 2f);
    }

    [Fact]
    public void Declutter_NonOverlappingLabels_SuppressesNone()
    {
        // Two labels far apart in the viewport cannot collide.
        var scene = new VectorScene(new List<PaintOp>
        {
            Text("a", 9.90, 0.0),
            Text("b", 10.10, 0.0),
        });

        var suppressed = Declutter(scene, Centred(10.0, 0.0, 0.4));

        Assert.Empty(suppressed);
    }

    [Fact]
    public void Declutter_OverlappingLabels_SuppressesLowerPriority()
    {
        // Two labels at the same anchor overlap. The op list is in ascending
        // drawing priority (later = higher), so the earlier ("low") is the loser.
        var low = Text("low", 10.0, 0.0);
        var high = Text("high", 10.0, 0.0);
        var scene = new VectorScene(new List<PaintOp> { low, high });

        var suppressed = Declutter(scene, Centred(10.0, 0.0, 0.4));

        Assert.Contains(low, suppressed);
        Assert.DoesNotContain(high, suppressed);
    }

    [Fact]
    public void Declutter_LabelOverlappingSymbol_SuppressesLabel()
    {
        // A point symbol always draws and reserves its footprint; a label at the
        // same anchor yields to it (even though the label is later/higher in the
        // op list, symbols are obstacles that never lose).
        var point = Point("sym", 10.0, 0.0);
        var label = Text("name", 10.0, 0.0);
        var scene = new VectorScene(new List<PaintOp> { point, label });

        var suppressed = Declutter(scene, Centred(10.0, 0.0, 0.4));

        Assert.Contains(label, suppressed);
    }

    [Fact]
    public void Declutter_IsDeterministic_AcrossRepeatedRuns()
    {
        var scene = new VectorScene(new List<PaintOp>
        {
            Text("a", 10.0, 0.0),
            Text("b", 10.0, 0.0),
            Text("c", 10.0001, 0.0),
            Point("p", 9.95, 0.0),
            Text("d", 9.95, 0.0),
        });
        var viewport = Centred(10.0, 0.0, 0.4);

        var first = Declutter(scene, viewport).Select(t => t.FeatureReference).OrderBy(s => s).ToArray();
        for (var i = 0; i < 5; i++)
        {
            var again = Declutter(scene, viewport).Select(t => t.FeatureReference).OrderBy(s => s).ToArray();
            Assert.Equal(first, again);
        }
    }

    [Fact]
    public void Declutter_SoundingsParticipate_AsTextVsText()
    {
        // Soundings are TextPaintOps and declutter against each other.
        var s1 = Text("s1", 10.0, 0.0, "12");
        var s2 = Text("s2", 10.0, 0.0, "34");
        var scene = new VectorScene(new List<PaintOp> { s1, s2 });

        var suppressed = Declutter(scene, Centred(10.0, 0.0, 0.4));

        Assert.Single(suppressed);
        Assert.Contains(s1, suppressed);
    }
}
