using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Projections;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-411 sea ice rendering. Covers both the
/// official IHO PC v1.2.1 sample shape (S100 GML 5.0 namespace, plural
/// <c>members</c> wrapper) and the JCOMM/CIS shape with shared
/// <c>gml:id="seaice.None"</c> identifiers exercised by the reader's
/// synthetic-id path.
/// </summary>
public sealed class S411RenderingTests
{
    [SkippableTheory]
    [InlineData("iho_4112C00TDS001.gml")]
    [InlineData("iho_4112C00TDS002.gml")]
    [InlineData("cis_seaice_synthetic.gml")]
    public Task SeaIce(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S411", fileName);
        Skip.IfNot(File.Exists(path), $"S-411 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        // The CIS render is two flat ice polygons plus their egg-code labels, so
        // the default 5% bound hid both a lost label and a changed outline
        // (#417's baseline drifted ~6k px unnoticed). Its cross-platform drift
        // is confined to text anti-aliasing (~1.9k px, 0.54%, macOS vs Linux),
        // so 1.5% leaves headroom for that while catching geometry changes. A
        // lost egg-code label is only ~100 px, below any platform-safe pixel
        // bound — SeaIce_EggCodeLabelsSurviveDeclutter guards that structurally.
        var settings = fileName == "cis_seaice_synthetic.gml"
            ? TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.015)
            : TestHelpers.VerifyBitmap(bitmap);
        return settings.UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }

    /// <summary>
    /// Structural guard (no pixels) for the WMO egg-code labels: each ice area
    /// emits its total concentration (<c>iceact</c>) and a verbose
    /// partial-concentration line at the same anchor, and the live label
    /// declutter must keep both. The two labels are only ~100 pixels of a dense
    /// render, so the snapshot alone cannot reliably catch losing one.
    /// </summary>
    [SkippableFact]
    public void SeaIce_EggCodeLabelsSurviveDeclutter()
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S411", "cis_seaice_synthetic.gml");
        Skip.IfNot(File.Exists(path), $"S-411 test dataset not present: {path}");

        const int size = 600;
        using var harness = new RenderHarness();
        var (layers, extent) = harness.BuildLayers(path, new HarnessOptions { Width = size, Height = size });

        VectorScene? overlay = null;
        foreach (var layer in layers)
        {
            if (S100VectorTileRenderer.TryGetPartitionedScene(layer, out _, out var ov))
            {
                overlay = ov;
                break;
            }
        }

        Assert.NotNull(overlay);
        var labels = overlay!.Ops.OfType<TextPaintOp>().ToList();
        Assert.Contains(labels, t => t.Text == "74");
        Assert.Contains(labels, t => t.Text == "91");
        Assert.Contains(labels, t => t.Text.Contains("Cp[", StringComparison.Ordinal));

        var (minLon, minLat) = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
        var (maxLon, maxLat) = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);
        var viewport = new Viewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = size,
            HeightPixels = size,
            // Unused: scale visibility is not honoured below.
            ScaleDenominator = 50_000,
        };

        using var declutterer = new LabelDeclutterer();
        var suppressed = declutterer.Declutter(
            overlay, viewport, new SKRect(0, 0, size, size), honorScaleVisibility: false, 0, size / 2f, size / 2f);

        Assert.True(
            suppressed.Count == 0,
            $"Declutter suppressed egg-code labels: {string.Join(", ", suppressed.Select(t => $"'{t.Text}'"))}");
    }
}
