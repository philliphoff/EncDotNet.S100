using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="MapsuiRenderAssetCache"/> shared between renderer
/// instances reuses processed-SVG and pattern-tile work across renders, so a
/// dataset processor that holds the cache for its lifetime stops re-paying
/// the SVG processing cost on every <see cref="MapsuiDisplayListRenderer.Render"/>.
/// </summary>
/// <remarks>
/// Backs PR-1 of <c>docs/internal/asset-caching-audit.md</c>.
/// </remarks>
public class MapsuiRenderAssetCacheTests
{
    private const string SymbolName = "TESTSYM";
    private const string FeatureRef = "F1";

    /// <summary>
    /// A counting symbol provider stand-in that returns a small, valid SVG
    /// the first time it is asked for any name and increments a counter on
    /// every call. Subsequent calls return the same content; the test
    /// asserts the counter remains at 1 across two renders.
    /// </summary>
    private sealed class CountingSymbolProvider
    {
        private readonly Dictionary<string, int> _calls = new(StringComparer.OrdinalIgnoreCase);

        public int CallsFor(string name) => _calls.TryGetValue(name, out var c) ? c : 0;

        public string? Resolve(string name)
        {
            _calls[name] = CallsFor(name) + 1;
            // Minimal viewBox SVG so SvgProcessor.Process succeeds and Mapsui
            // can host it as a "svg-content://" source.
            return """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="3" fill="black"/></svg>""";
        }
    }

    private sealed class StubGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            featureReference == FeatureRef
                ? new FeatureGeometry
                {
                    Type = GeometryType.Point,
                    Coordinates = new[] { (Latitude: 0.0, Longitude: 0.0) },
                }
                : null;
    }

    [Fact]
    public void SharedCache_AcrossTwoRenderers_OnlyResolvesSymbolOnce()
    {
        var cache = new MapsuiRenderAssetCache();
        var provider = new CountingSymbolProvider();
        var palette = ColorPalette.Default;

        var instructions = new DrawingInstruction[]
        {
            new PointInstruction
            {
                FeatureReference = FeatureRef,
                SymbolReference = SymbolName,
            },
        };
        var geometryProvider = new StubGeometryProvider();

        var first = new MapsuiDisplayListRenderer
        {
            Palette = palette,
            AssetCache = cache,
            SymbolProvider = provider.Resolve,
        };
        first.Render(instructions, geometryProvider);

        Assert.Equal(1, provider.CallsFor(SymbolName));

        // A *new* renderer with the same cache simulates the dataset
        // processor's "render again after a palette toggle / time scrub /
        // mariner-setting change" code path. Without a shared cache the
        // symbol provider would be hit a second time.
        var second = new MapsuiDisplayListRenderer
        {
            Palette = palette,
            AssetCache = cache,
            SymbolProvider = provider.Resolve,
        };
        second.Render(instructions, geometryProvider);

        Assert.Equal(1, provider.CallsFor(SymbolName));
    }

    [Fact]
    public void SharedCache_DifferentPalettes_RecomputeIndependently()
    {
        // Per-palette segmentation is required for correctness because
        // SvgProcessor.Process recolours fills/strokes from the palette.
        // Two distinct palettes must therefore produce two cache entries.
        var cache = new MapsuiRenderAssetCache();
        var provider = new CountingSymbolProvider();

        var day = new ColorPalette("Day", new Dictionary<string, string>());
        var night = new ColorPalette("Night", new Dictionary<string, string>());
        var instructions = new DrawingInstruction[]
        {
            new PointInstruction { FeatureReference = FeatureRef, SymbolReference = SymbolName },
        };
        var geometryProvider = new StubGeometryProvider();

        new MapsuiDisplayListRenderer
        {
            Palette = day,
            AssetCache = cache,
            SymbolProvider = provider.Resolve,
        }.Render(instructions, geometryProvider);

        new MapsuiDisplayListRenderer
        {
            Palette = night,
            AssetCache = cache,
            SymbolProvider = provider.Resolve,
        }.Render(instructions, geometryProvider);

        // Once for Day, once for Night.
        Assert.Equal(2, provider.CallsFor(SymbolName));

        // Re-rendering Day must still hit the cache.
        new MapsuiDisplayListRenderer
        {
            Palette = day,
            AssetCache = cache,
            SymbolProvider = provider.Resolve,
        }.Render(instructions, geometryProvider);

        Assert.Equal(2, provider.CallsFor(SymbolName));
    }

    [Fact]
    public void NoSharedCache_AcrossTwoRenderers_ResolvesSymbolEachRender()
    {
        // Sanity check: without an explicit shared cache the legacy
        // per-renderer fallback re-resolves the symbol on each new instance.
        var provider = new CountingSymbolProvider();
        var instructions = new DrawingInstruction[]
        {
            new PointInstruction { FeatureReference = FeatureRef, SymbolReference = SymbolName },
        };
        var geometryProvider = new StubGeometryProvider();

        new MapsuiDisplayListRenderer
        {
            Palette = ColorPalette.Default,
            SymbolProvider = provider.Resolve,
        }.Render(instructions, geometryProvider);

        new MapsuiDisplayListRenderer
        {
            Palette = ColorPalette.Default,
            SymbolProvider = provider.Resolve,
        }.Render(instructions, geometryProvider);

        Assert.Equal(2, provider.CallsFor(SymbolName));
    }
}
