using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Projections;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Durable multi-product golden guard for the tiled base-plane renderer, tracked
/// by the "Multi-product / multi-dataset validation" item of issue #347. It
/// converts the one-off fidelity survey across the non-S-101 products into
/// committed CI gates so a future change to the renderer cannot silently regress
/// a product's fidelity.
/// </summary>
/// <remarks>
/// <para>
/// "B" only swaps the <i>vector</i> base plane; it never touches the coverage
/// (HDF5) raster path. The guard therefore applies three tiers, each matched to
/// what can actually diverge:
/// </para>
/// <list type="number">
///   <item><b>Coverage exact-match</b> (<see cref="Coverage_AbPixelIdentical"/>)
///         — S-102/104/111 render pixel-identical through "A" and "B"; any
///         divergence is a real coupling bug.</item>
///   <item><b>Per-product B-arm goldens</b> (<see cref="Vector_BArmGolden"/>) —
///         one representative GML fixture per vector product is rendered through
///         "B" and verified against a committed snapshot, guarding the tiled
///         renderer against self-drift across every product family.</item>
///   <item><b>Label preservation</b> (<see cref="Vector_PointSymbolsDoNotSuppressLabels"/>)
///         — a structural, pixel-free assertion on the products whose portrayal
///         anchors text on co-located point symbols (S-421 route action points,
///         S-124 warnings, S-125 AtoN). It proves "B"'s declutter never drops a
///         label because of a symbol, which the coarse perceptual gate cannot
///         see (the S-421 regression that motivated this guard).</item>
/// </list>
/// <para>
/// The headless "B" path renders north-up on a software surface, so rotation
/// uprightness and GPU residency are out of scope here (the in-viewer Metal
/// recipe in <c>README.md</c> covers them).
/// </para>
/// </remarks>
public sealed class MultiProductParityTests
{
    /// <summary>
    /// Renders one representative committed fixture per GML vector product and
    /// verifies it against a committed golden — the per-product regression guard
    /// for the tiled renderer.
    /// </summary>
    [SkippableTheory]
    [InlineData("S122", "122TESTDATASET.gml")]
    [InlineData("S124", "navwarn_mixed.gml")]
    [InlineData("S125", "aton_chesapeake.gml")]
    [InlineData("S127", "marine_mixed.gml")]
    [InlineData("S128", "S128_TDS_sample.gml")]
    [InlineData("S129", "12900MCTDS130TS.gml")]
    [InlineData("S131", "harbour_surface.gml")]
    [InlineData("S201", "aton_light.gml")]
    [InlineData("S411", "iho_4112C00TDS001.gml")]
    [InlineData("S421", "RTE-TEST-GFULL.s421.gml")]
    public Task Vector_BArmGolden(string product, string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, product, fileName);
        Skip.IfNot(File.Exists(path), $"{product} test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
            Palette = PaletteType.Day,
        });

        // Perceptual tolerance absorbs sub-pixel anti-aliasing drift in the
        // tiled compositor across platforms/GPUs, matching the other baselines.
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.05)
            .UseParameters(product);
    }

    /// <summary>
    /// Structural guard (no pixels) that "B"'s label declutter never suppresses
    /// a label because of a <i>point symbol</i> — only label-vs-label overlap is
    /// resolved, matching the Mapsui "A" arm. Run against the products whose
    /// portrayal anchors text on co-located symbols, where the regression that
    /// motivated this guard (S-421 route labels dropped onto waypoint circles)
    /// actually lives. (S-125 AtoN is excluded: its synthetic fixtures carry no
    /// portrayed text, so the guard would be vacuous there.)
    /// </summary>
    [SkippableTheory]
    [InlineData("S421", "RTE-TEST-GFULL.s421.gml")]
    [InlineData("S124", "navwarn_mixed.gml")]
    public void Vector_PointSymbolsDoNotSuppressLabels(string product, string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, product, fileName);
        Skip.IfNot(File.Exists(path), $"{product} test dataset not present: {path}");

        const int width = 1024;
        const int height = 1024;

        using var harness = new RenderHarness();
        var (layers, _) = harness.BuildLayers(path, new HarnessOptions
        {
            Width = width,
            Height = height,
            Palette = PaletteType.Day,
            // "All" so no label is hidden by the Standard display filter — the
            // guard must see every label the portrayal emits.
            DisplayCategory = null,
        });

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
        Assert.True(
            labels.Count > 0,
            $"{product} fixture '{fileName}' produced no labels in B's overlay plane — a label-bearing fixture must carry text for this guard to be meaningful.");

        var viewport = BuildEnclosingViewport(overlay, width, height);
        var screenCull = new SKRect(-2000, -2000, width + 2000, height + 2000);
        float cx = width * 0.5f;
        float cy = height * 0.5f;

        using var declutterer = new LabelDeclutterer();

        // The returned set is a render-thread-confined reusable buffer, so copy
        // each result before the next Declutter call reuses it.
        var withSymbols = new HashSet<TextPaintOp>(
            declutterer.Declutter(overlay, viewport, screenCull, honorScaleVisibility: false, 0, cx, cy));

        var labelsOnly = new VectorScene(overlay.Ops.Where(op => op is TextPaintOp).ToList());
        var withoutSymbols = new HashSet<TextPaintOp>(
            declutterer.Declutter(labelsOnly, viewport, screenCull, honorScaleVisibility: false, 0, cx, cy));

        Assert.True(
            withSymbols.SetEquals(withoutSymbols),
            $"{product}: point symbols changed which labels survive declutter " +
            $"(with symbols suppressed {withSymbols.Count}, labels-only suppressed {withoutSymbols.Count}). " +
            "Symbols must never displace labels (B>=A parity, issue #347).");
    }

    /// <summary>
    /// Builds a north-up <see cref="Viewport"/> whose geographic bounds enclose
    /// every overlay anchor (with a margin), so the declutter pass projects all
    /// labels on-screen and none are culled — making the symbol-vs-label
    /// comparison non-vacuous.
    /// </summary>
    private static Viewport BuildEnclosingViewport(VectorScene overlay, int width, int height)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        void Extend(double x, double y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        foreach (var op in overlay.Ops)
        {
            switch (op)
            {
                case TextPaintOp t:
                    Extend(t.World.X, t.World.Y);
                    break;
                case PointPaintOp p:
                    Extend(p.World.X, p.World.Y);
                    break;
            }
        }

        // Pad the world bbox by 10% (and guard a degenerate single-point bbox)
        // so anchors sit comfortably inside the viewport rather than on its edge.
        double padX = Math.Max((maxX - minX) * 0.1, 1000);
        double padY = Math.Max((maxY - minY) * 0.1, 1000);
        minX -= padX; maxX += padX;
        minY -= padY; maxY += padY;

        var (minLon, minLat) = SphericalMercator.ToLonLat(minX, minY);
        var (maxLon, maxLat) = SphericalMercator.ToLonLat(maxX, maxY);

        return new Viewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = width,
            HeightPixels = height,
            // honorScaleVisibility is false above, so this is unused by the
            // declutter pass; a representative large-scale denominator keeps it
            // well-formed.
            ScaleDenominator = 50_000,
        };
    }
}
