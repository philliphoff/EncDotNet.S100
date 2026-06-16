using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the per-symbol pick-target rectangle emitted by
/// <see cref="MapsuiDisplayListRenderer"/> for point symbology.
/// </summary>
/// <remarks>
/// <para>
/// Mapsui's feature picking is pixel-based, so the renderer paints a
/// near-invisible (<c>alpha = 1</c>) rectangle behind each point symbol so a
/// tap on a transparent part of the SVG still resolves the feature.
/// </para>
/// <para>
/// S-101 composite soundings (S-101 §SNDFRM04) emit one symbol per digit/ring
/// at a single anchor; without deduplication, the stacked rectangles accumulate
/// their faint fill into a visibly darkened box around the sounding. These
/// tests pin the rectangle to exactly one per (feature, anchor).
/// </para>
/// </remarks>
public class MapsuiSoundingHitRectTests
{
    private const string SymbolName = "SOUNDG18";

    private static readonly DrawingInstruction[] MultiDigitSounding =
    {
        // Three digit glyphs of one sounding feature, all anchored at the same
        // coordinate but offset locally — exactly how SNDFRM04 lowers a
        // multi-digit value.
        new PointInstruction { FeatureReference = "S1", SymbolReference = SymbolName, LocalOffsetX = -0.6 },
        new PointInstruction { FeatureReference = "S1", SymbolReference = SymbolName, LocalOffsetX = 0.0 },
        new PointInstruction { FeatureReference = "S1", SymbolReference = SymbolName, LocalOffsetX = 0.6 },
    };

    private sealed class StubGeometryProvider : IFeatureGeometryProvider
    {
        // Distinct anchor per feature reference so separate soundings project to
        // separate EPSG:3857 points; symbols of one feature share its anchor.
        public FeatureGeometry? GetGeometry(string featureReference)
        {
            var lon = 20.0 + featureReference.GetHashCode() % 1000 * 0.001;
            return new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = new[] { (Latitude: 10.0, Longitude: lon) },
            };
        }
    }

    private static string? ResolveSymbol(string name) =>
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="3" fill="black"/></svg>""";

    private static List<IFeature> Render(DrawingInstruction[] instructions)
    {
        var renderer = new MapsuiDisplayListRenderer
        {
            Palette = ColorPalette.Default,
            SymbolProvider = ResolveSymbol,
        };

        var layer = renderer.Render(instructions, new StubGeometryProvider());
        var memLayer = Assert.IsAssignableFrom<MemoryLayer>(layer);
        return memLayer.Features.ToList();
    }

    private static int CountHitRects(IEnumerable<IFeature> features) =>
        features.Sum(f => f.Styles.Count(s =>
            s is SymbolStyle { SymbolType: SymbolType.Rectangle }));

    private static int CountImageStyles(IEnumerable<IFeature> features) =>
        features.Sum(f => f.Styles.Count(s => s is ImageStyle));

    [Fact]
    public void MultiDigitSounding_EmitsExactlyOneHitRect()
    {
        var features = Render(MultiDigitSounding);

        // Every digit still draws its symbol …
        Assert.Equal(3, CountImageStyles(features));
        // … but the pick-target rectangle is emitted only once for the anchor.
        Assert.Equal(1, CountHitRects(features));
    }

    [Fact]
    public void SeparateSoundings_EachKeepTheirOwnHitRect()
    {
        // Two single-digit soundings at distinct anchors must each remain
        // independently pickable.
        var instructions = new DrawingInstruction[]
        {
            new PointInstruction { FeatureReference = "A", SymbolReference = SymbolName },
            new PointInstruction { FeatureReference = "B", SymbolReference = SymbolName },
        };

        var features = Render(instructions);

        Assert.Equal(2, CountImageStyles(features));
        Assert.Equal(2, CountHitRects(features));
    }
}
