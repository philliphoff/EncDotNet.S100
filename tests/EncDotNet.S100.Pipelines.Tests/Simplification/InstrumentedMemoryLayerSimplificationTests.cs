using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.Simplification;
using Mapsui;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests.Simplification;

/// <summary>
/// Validates that <see cref="InstrumentedMemoryLayer"/> hands
/// simplified copies (or originals, when disabled) to its callers,
/// preserving the <c>S100.FeatureRef</c> tag so picking still works.
/// </summary>
public sealed class InstrumentedMemoryLayerSimplificationTests
{
    private static readonly GeometryFactory _gf = new();

    private static GeometryFeature MakeDenseLine(int n, string featureRef)
    {
        var coords = new Coordinate[n];
        for (int i = 0; i < n; i++) coords[i] = new Coordinate(i, (i % 4) * 0.1);
        var f = new GeometryFeature(_gf.CreateLineString(coords));
        f["S100.FeatureRef"] = featureRef;
        return f;
    }

    [Fact]
    public void Disabled_PassesOriginalsThrough()
    {
        var feat = MakeDenseLine(500, "F1");
        var layer = new InstrumentedMemoryLayer { Features = new[] { feat } };

        var visible = layer.GetFeatures(feat.Extent!.Grow(10), resolution: 100.0).ToList();

        Assert.Single(visible);
        Assert.Same(feat, visible[0]);
    }

    [Fact]
    public void Enabled_LowResolution_SimplifiesGeometry()
    {
        var feat = MakeDenseLine(500, "F1");
        var layer = new InstrumentedMemoryLayer { Features = new[] { feat } };
        layer.EnableSimplification(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());

        // Coarse resolution → tolerance large enough to collapse the jitter.
        var visible = layer.GetFeatures(feat.Extent!.Grow(10), resolution: 100.0).ToList();

        Assert.Single(visible);
        var simplified = (GeometryFeature)visible[0];
        Assert.NotSame(feat, simplified);
        var origCount = ((LineString)feat.Geometry!).NumPoints;
        var newCount = ((LineString)simplified.Geometry!).NumPoints;
        Assert.True(newCount < origCount, $"Expected reduction; got {origCount}→{newCount}.");
        // FeatureRef and OriginalFeature back-ref preserved.
        Assert.Equal("F1", simplified["S100.FeatureRef"]);
        Assert.Same(feat, EncDotNet.S100.Renderers.Mapsui.Simplification.Simplification.GetOriginal(simplified));
    }

    [Fact]
    public void Enabled_HighResolution_TightToleranceMostlyPreserves()
    {
        // Use a sharper-feature line so a 0.5 m tolerance leaves it intact.
        var coords = new Coordinate[200];
        for (int i = 0; i < coords.Length; i++) coords[i] = new Coordinate(i, (i % 2 == 0) ? 0 : 5);
        var feat = new GeometryFeature(_gf.CreateLineString(coords));
        feat["S100.FeatureRef"] = "F2";
        var layer = new InstrumentedMemoryLayer { Features = new[] { feat } };
        layer.EnableSimplification(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());

        // Bucket 0 → tolerance 0.5 m. The 5 m peaks must survive.
        var visible = layer.GetFeatures(feat.Extent!.Grow(10), resolution: 1.0).ToList();

        Assert.Single(visible);
        var ls = (LineString)((GeometryFeature)visible[0]).Geometry!;
        // Allow some collapse but the alternating pattern should still be visible.
        Assert.True(ls.NumPoints >= coords.Length / 2,
            $"Expected most peaks preserved at tight tolerance; got {ls.NumPoints} of {coords.Length}.");
    }

    [Fact]
    public void Disable_AfterEnable_RestoresPassthrough()
    {
        var feat = MakeDenseLine(500, "F1");
        var layer = new InstrumentedMemoryLayer { Features = new[] { feat } };
        layer.EnableSimplification(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        var simplified = layer.GetFeatures(feat.Extent!.Grow(10), resolution: 100.0).ToList()[0];
        Assert.NotSame(feat, simplified);

        layer.DisableSimplification();
        var afterDisable = layer.GetFeatures(feat.Extent!.Grow(10), resolution: 100.0).ToList()[0];
        Assert.Same(feat, afterDisable);
    }

    [Fact]
    public void NullRect_ReturnsEmpty_WithSimplificationEnabled()
    {
        var feat = MakeDenseLine(500, "F1");
        var layer = new InstrumentedMemoryLayer { Features = new[] { feat } };
        layer.EnableSimplification(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        Assert.Empty(layer.GetFeatures(null, 1.0));
    }
}
