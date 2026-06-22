using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the pure geometry of the TiledScene ("B") render subsystem's
/// custom layer renderer (<see cref="S100VectorSceneRenderer"/>): the
/// translation-invariant blit anchoring (validity + translate), the scale-
/// denominator derivation that drives SCAMIN culling, and the margin / device-
/// scale / EPSG:3857 projection that builds the worker's rasterisation viewport.
/// The SkiaSharp worker raster + UI composite require a live surface and are
/// exercised by the MCP perf harness, not xunit.
/// </summary>
public sealed class S100VectorSceneRendererTests
{
    private static S100VectorSceneRenderer.RecordAnchor Anchor(
        double centerX = 1000,
        double centerY = 2000,
        double widthDip = 1024,
        double heightDip = 1024,
        double resolution = 10) =>
        new(centerX, centerY, widthDip, heightDip, resolution);

    [Fact]
    public void IsValid_TrueForUnmovedViewport()
    {
        // A 1024-DIP record around a 512-DIP viewport leaves a 256 DIP margin.
        var anchor = Anchor(widthDip: 1024, heightDip: 1024);

        Assert.True(S100VectorSceneRenderer.IsValid(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10));
    }

    [Fact]
    public void IsValid_FalseWhenResolutionChanges()
    {
        var anchor = Anchor(resolution: 10);

        Assert.False(S100VectorSceneRenderer.IsValid(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 20));
    }

    [Fact]
    public void IsValid_TrueForPanWithinMargin()
    {
        // 256 DIP margin (1024 record, 512 view) at 10 m/px ⇒ 2560 m of travel.
        var anchor = Anchor(centerX: 1000, centerY: 2000, widthDip: 1024, heightDip: 1024, resolution: 10);

        Assert.True(S100VectorSceneRenderer.IsValid(
            anchor, centerX: 1000 + 2000, centerY: 2000 - 2000, width: 512, height: 512, resolution: 10));
    }

    [Fact]
    public void IsValid_FalseForPanBeyondMargin()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, widthDip: 1024, heightDip: 1024, resolution: 10);

        // 2600 m east at 10 m/px = 260 DIP > 256 DIP margin.
        Assert.False(S100VectorSceneRenderer.IsValid(
            anchor, centerX: 1000 + 2600, centerY: 2000, width: 512, height: 512, resolution: 10));
    }

    [Fact]
    public void IsValid_FalseWhenViewportLargerThanRecord()
    {
        var anchor = Anchor(widthDip: 512, heightDip: 512);

        Assert.False(S100VectorSceneRenderer.IsValid(
            anchor, centerX: 1000, centerY: 2000, width: 1024, height: 1024, resolution: 10));
    }

    [Fact]
    public void ComputeTranslate_CentersUnmovedRecord()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, widthDip: 1024, heightDip: 1024, resolution: 10);

        var (tx, ty) = S100VectorSceneRenderer.ComputeTranslate(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10);

        // Record (1024) centred under the view (512) ⇒ offset by -256 on each axis.
        Assert.Equal(-256, tx, 6);
        Assert.Equal(-256, ty, 6);
    }

    [Fact]
    public void ComputeTranslate_ShiftsOppositeToPan()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, widthDip: 1024, heightDip: 1024, resolution: 10);

        // Pan 1000 m east (centerX 1000→2000): 100 DIP at 10 m/px. The image must
        // shift 100 DIP further left than the centred -256 baseline.
        var (tx, _) = S100VectorSceneRenderer.ComputeTranslate(
            anchor, centerX: 2000, centerY: 2000, width: 512, height: 512, resolution: 10);

        Assert.Equal(-356, tx, 6);
    }

    [Fact]
    public void ComputeTranslate_NorthPanShiftsImageDown()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, widthDip: 1024, heightDip: 1024, resolution: 10);

        // Pan 1000 m north (centerY 2000→3000): screen +Y is down, so the image
        // moves down by 100 DIP relative to the centred -256 baseline.
        var (_, ty) = S100VectorSceneRenderer.ComputeTranslate(
            anchor, centerX: 1000, centerY: 3000, width: 512, height: 512, resolution: 10);

        Assert.Equal(-156, ty, 6);
    }

    [Fact]
    public void ScaleDenominatorFor_RoundTripsDenominatorToResolution()
    {
        // At the equator (centerY = 0) cos = 1, so the inverse is exact.
        const double denominator = 25_000;
        var resolution = MapsuiDisplayListRenderer.DenominatorToResolution(denominator, latitudeRadians: 0);

        var recovered = S100VectorSceneRenderer.ScaleDenominatorFor(centerX: 0, centerY: 0, resolution);

        Assert.Equal(denominator, recovered, 3);
    }

    [Fact]
    public void ScaleDenominatorFor_AppliesLatitudeCosineCorrection()
    {
        // Off the equator the denominator for a fixed resolution is smaller
        // (cos < 1), matching DenominatorToResolution's cos(midLat) correction.
        var equator = S100VectorSceneRenderer.ScaleDenominatorFor(centerX: 0, centerY: 0, resolution: 10);
        var high = S100VectorSceneRenderer.ScaleDenominatorFor(centerX: 0, centerY: 6_000_000, resolution: 10);

        Assert.True(high < equator);
    }

    [Fact]
    public void BuildViewport_EnlargesByMarginAndScalesToDevice()
    {
        // Centre at EPSG:3857 origin so the projected bounds are symmetric.
        var request = new S100VectorSceneRenderer.RasterRequest(
            CenterX: 0,
            CenterY: 0,
            Resolution: 10,
            ScaleDenominator: 25_000,
            WidthDip: 800,
            HeightDip: 600,
            DeviceScale: 2.0,
            Generation: 1);

        var viewport = S100VectorSceneRenderer.BuildViewport(request);

        var margin = S100VectorSceneRenderer.MarginPx;
        var expectedWidthDip = 800 + 2 * margin;
        var expectedHeightDip = 600 + 2 * margin;

        Assert.Equal((int)System.Math.Round(expectedWidthDip * 2.0), viewport.WidthPixels);
        Assert.Equal((int)System.Math.Round(expectedHeightDip * 2.0), viewport.HeightPixels);
        Assert.Equal(25_000, viewport.ScaleDenominator, 6);

        // Symmetric about the origin in both axes.
        Assert.Equal(-viewport.MinLongitude, viewport.MaxLongitude, 9);
        Assert.Equal(-viewport.MinLatitude, viewport.MaxLatitude, 9);

        // Half-width in metres = recordWidthDip/2 * resolution; converted back to
        // longitude via Web-Mercator it must equal MaxLongitude.
        var halfWidthMetres = expectedWidthDip * 0.5 * 10;
        var (expectedLon, _) = EncDotNet.S100.Rendering.Scene.WebMercator.ToLonLat(halfWidthMetres, 0);
        Assert.Equal(expectedLon, viewport.MaxLongitude, 6);
    }
}
