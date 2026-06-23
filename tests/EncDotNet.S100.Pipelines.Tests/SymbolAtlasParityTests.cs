using System;
using System.Collections.Generic;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pixel-parity tests for the overlay symbol atlas (#332 lever c2). The atlas
/// blits a once-rasterised <see cref="SKImage"/> sprite instead of replaying the
/// symbol's vector <see cref="SKPicture"/> every frame; these tests prove the
/// sprite draws the <b>identical</b> symbol — same pivot placement, same size,
/// at device scale 1× and 2× (HiDPI) — so the optimisation changes frame cost,
/// not the chart (the #332 fidelity principle).
/// </summary>
public class SymbolAtlasParityTests
{
    private static PointPaintOp Symbol(double lon, double lat, double pivotX, double pivotY) =>
        new()
        {
            FeatureReference = "sym",
            World = WebMercator.FromLonLat(lon, lat),
            Symbol = new ResolvedSymbol(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3.98mm\" " +
                "height=\"3.98mm\" viewBox=\"-1.99 -1.99 3.98 3.98\">" +
                "<circle cx=\"0\" cy=\"0\" r=\"1.6\" fill=\"red\"/>" +
                "<rect x=\"-1.99\" y=\"-1.0\" width=\"3.98\" height=\"0.4\" fill=\"black\"/></svg>",
                Scale: 2.0,
                PivotRelativeX: pivotX,
                PivotRelativeY: pivotY),
            FallbackColor = new RgbaColor(220, 20, 60, 255),
            FallbackScale = 1.0,
        };

    private static Viewport Centred(double lon, double lat, double span) =>
        new()
        {
            MinLongitude = lon - span / 2,
            MaxLongitude = lon + span / 2,
            MinLatitude = lat - span / 2,
            MaxLatitude = lat + span / 2,
            WidthPixels = 200,
            HeightPixels = 200,
            ScaleDenominator = 50000,
        };

    private static SKBitmap RenderOverlay(VectorScene scene, Viewport viewport, float deviceScale, bool atlas)
    {
        int w = (int)Math.Round(viewport.WidthPixels * deviceScale);
        int h = (int)Math.Round(viewport.HeightPixels * deviceScale);
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using (var surface = SKSurface.Create(info))
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(deviceScale);
            var renderer = new SkiaDisplayListRenderer { HonorScaleVisibility = false };
            renderer.RenderOnto(canvas, scene, viewport, new OverlayDrawOptions
            {
                UseSymbolAtlas = atlas,
                DeviceScale = deviceScale,
            });
            canvas.Flush();
            surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        }
        return bitmap;
    }

    private static (int Width, int Height) OpaqueBounds(SKBitmap b)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).Alpha != 0)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        return maxX < 0 ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
    }

    private static (double MeanAbs, int Differing, int Opaque) Compare(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        long sum = 0;
        int differing = 0, opaque = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                if (pa.Alpha != 0 || pb.Alpha != 0) opaque++;
                int d = Math.Abs(pa.Red - pb.Red) + Math.Abs(pa.Green - pb.Green)
                      + Math.Abs(pa.Blue - pb.Blue) + Math.Abs(pa.Alpha - pb.Alpha);
                sum += d;
                if (d > 48) differing++;
            }
        return ((double)sum / (a.Width * a.Height), differing, opaque);
    }

    [Theory]
    [InlineData(1.0f, 0.0, 0.0)]
    [InlineData(2.0f, 0.0, 0.0)]
    [InlineData(1.0f, 0.5, 0.5)]
    [InlineData(2.0f, 0.5, -0.5)]
    public void AtlasSprite_MatchesVectorPicture(float deviceScale, double pivotX, double pivotY)
    {
        const double lon = 10.0, lat = 0.0;
        var scene = new VectorScene(new List<PaintOp> { Symbol(lon, lat, pivotX, pivotY) });
        var viewport = Centred(lon, lat, 0.1);

        using var vector = RenderOverlay(scene, viewport, deviceScale, atlas: false);
        using var atlas = RenderOverlay(scene, viewport, deviceScale, atlas: true);

        // Same on-screen footprint: the sprite is placed by the identical pivot
        // math, so the opaque bounding box must match (allow ±1 px for edge AA).
        var (vw, vh) = OpaqueBounds(vector);
        var (aw, ah) = OpaqueBounds(atlas);
        Assert.True(vw > 0 && vh > 0, "vector reference drew nothing");
        Assert.InRange(aw, vw - 1, vw + 1);
        Assert.InRange(ah, vh - 1, vh + 1);

        // Same pixels: only antialiased edges may differ slightly. Mean absolute
        // per-pixel channel difference stays tiny and only a few edge pixels
        // exceed the tolerance.
        var (meanAbs, differing, opaque) = Compare(vector, atlas);
        Assert.True(meanAbs < 2.0, $"mean abs diff {meanAbs:F3} too high");
        Assert.True(differing < opaque * 0.15,
            $"{differing} of {opaque} opaque pixels differ — atlas not matching vector");
    }
}
