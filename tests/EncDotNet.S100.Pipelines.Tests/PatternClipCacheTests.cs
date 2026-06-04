using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the in-memory pattern-clip geometry cache: that routing the
/// S-101 pattern-fill priority clip through an <see cref="IPatternClipCache"/>
/// is behaviour-preserving (byte-identical clipped geometry with vs without the
/// cache) and that a second render with the same key — including a palette
/// change, which does not alter the clip inputs — is served from the cache.
/// </summary>
public class PatternClipCacheTests
{
    private const string PatternRefHigh = "DIAMOND1";
    private const string PatternRefLow = "DQUAL";

    // Two overlapping area features with distinct pattern fills at different
    // drawing priorities, so the clip actually subtracts the higher-priority
    // area out of the lower-priority one.
    private static readonly (double Lat, double Lon)[] LowSquare =
    [
        (0.0, 0.0), (0.0, 2.0), (2.0, 2.0), (2.0, 0.0), (0.0, 0.0),
    ];

    private static readonly (double Lat, double Lon)[] HighSquare =
    [
        (1.0, 1.0), (1.0, 3.0), (3.0, 3.0), (3.0, 1.0), (1.0, 1.0),
    ];

    private sealed class StubGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "low" => Surface(LowSquare),
            "high" => Surface(HighSquare),
            _ => null,
        };

        private static FeatureGeometry Surface((double Lat, double Lon)[] ring) => new()
        {
            Type = GeometryType.Surface,
            Coordinates = ring.Select(p => (p.Lat, p.Lon)).ToArray(),
        };
    }

    private static DrawingInstruction[] BuildInstructions() =>
    [
        new AreaInstruction
        {
            FeatureReference = "low",
            AreaFillReference = PatternRefLow,
            DrawingPriority = 5,
        },
        new AreaInstruction
        {
            FeatureReference = "high",
            AreaFillReference = PatternRefHigh,
            DrawingPriority = 9,
        },
    ];

    // A pattern provider that maps both pattern refs to non-null tiles so the
    // renderer's inclusion gate keeps them. Colours are irrelevant to the clip
    // geometry, so a fixed 1x1 PNG per palette is sufficient.
    private static Func<string, AreaFill?> AreaFills() =>
        name => new AreaFill
        {
            Name = name,
            PatternSymbol = name,
            V1X = 4.0,
            V1Y = 0.0,
            V2X = 0.0,
            V2Y = 4.0,
        };

    private static Func<string, string?> Symbols() =>
        _ => """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 4 4"><rect width="4" height="4" fill="black"/></svg>""";

    private static MapsuiDisplayListRenderer NewRenderer(
        ColorPalette palette,
        IPatternClipCache? cache,
        string? cacheKey) => new()
        {
            Palette = palette,
            AreaFillProvider = AreaFills(),
            SymbolProvider = Symbols(),
            PatternClipCache = cache,
            PatternClipCacheKey = cacheKey,
        };

    private static List<Geometry> PatternGeometries(Mapsui.Layers.ILayer layer)
    {
        // Pattern-fill features carry an AnchoredPatternFillStyle; collect their
        // geometries in layer order for a structural comparison.
        var geometries = new List<Geometry>();
        foreach (var feature in ((Mapsui.Layers.MemoryLayer)layer).Features)
        {
            if (feature is Mapsui.Nts.GeometryFeature gf
                && feature.Styles.Any(s => s is AnchoredPatternFillStyle))
            {
                geometries.Add(gf.Geometry!);
            }
        }
        return geometries;
    }

    [Fact]
    public void CachedClip_IsByteIdenticalTo_UncachedClip()
    {
        var palette = ColorPalette.Default;
        var provider = new StubGeometryProvider();

        var uncached = PatternGeometries(
            NewRenderer(palette, cache: null, cacheKey: null)
                .Render(BuildInstructions(), provider));

        var cache = new InMemoryPatternClipCache();
        var cached = PatternGeometries(
            NewRenderer(palette, cache, cacheKey: "k1")
                .Render(BuildInstructions(), provider));

        Assert.Equal(uncached.Count, cached.Count);
        Assert.NotEmpty(cached);
        for (int i = 0; i < uncached.Count; i++)
        {
            // EqualsExact is a structural (vertex-by-vertex) comparison.
            Assert.True(
                uncached[i].EqualsExact(cached[i]),
                $"Clipped geometry {i} differs between cached and uncached paths.");
        }

        Assert.Equal(1, cache.Misses);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void SecondRender_SameKey_IsCacheHit_AndReturnsSameInstances()
    {
        var palette = ColorPalette.Default;
        var provider = new StubGeometryProvider();
        var cache = new InMemoryPatternClipCache();

        var first = PatternGeometries(
            NewRenderer(palette, cache, cacheKey: "k1")
                .Render(BuildInstructions(), provider));
        var second = PatternGeometries(
            NewRenderer(palette, cache, cacheKey: "k1")
                .Render(BuildInstructions(), provider));

        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Hits);

        Assert.Equal(first.Count, second.Count);
        Assert.NotEmpty(first);
        for (int i = 0; i < first.Count; i++)
        {
            // A cache hit returns the very same Geometry instances.
            Assert.Same(first[i], second[i]);
        }
    }

    [Fact]
    public void PaletteChange_SameKey_IsCacheHit_GeometryUnchanged()
    {
        var provider = new StubGeometryProvider();
        var cache = new InMemoryPatternClipCache();

        var day = new ColorPalette("Day", new Dictionary<string, string>());
        var night = new ColorPalette("Night", new Dictionary<string, string>());

        var dayGeom = PatternGeometries(
            NewRenderer(day, cache, cacheKey: "k1")
                .Render(BuildInstructions(), provider));

        // Same mariner/ECDIS key, only the palette differs — the clip geometry
        // is palette-independent, so this must be a cache hit reusing the exact
        // geometry instances computed for Day.
        var nightGeom = PatternGeometries(
            NewRenderer(night, cache, cacheKey: "k1")
                .Render(BuildInstructions(), provider));

        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Hits);

        Assert.Equal(dayGeom.Count, nightGeom.Count);
        Assert.NotEmpty(dayGeom);
        for (int i = 0; i < dayGeom.Count; i++)
        {
            Assert.Same(dayGeom[i], nightGeom[i]);
        }
    }

    [Fact]
    public void GetOrCompute_NullArguments_Throw()
    {
        var cache = new InMemoryPatternClipCache();

        Assert.Throws<ArgumentNullException>(() =>
            cache.GetOrCompute(null!, () => []));
        Assert.Throws<ArgumentNullException>(() =>
            cache.GetOrCompute("k", null!));
    }
}
