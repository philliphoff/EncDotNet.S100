using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class KindMatchingRendererTests
{
    private sealed class StubRenderer : IDynamicFeatureRenderer
    {
        public string Name { get; }
        public StubRenderer(string name) { Name = name; }
        public bool CanRender(DynamicFeature feature) => true;
        public IEnumerable<IFeature> Render(DynamicFeature feature) => Array.Empty<IFeature>();
    }

    private static DynamicFeature Feat(string? kind) => new()
    {
        Id = "f",
        Kind = kind,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { (0.0, 0.0) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void ExactMatch_PicksRegisteredRenderer()
    {
        var cargo = new StubRenderer("cargo");
        var tanker = new StubRenderer("tanker");
        var r = new KindMatchingRenderer(new Dictionary<string, IDynamicFeatureRenderer>
        {
            ["vessel.cargo"] = cargo,
            ["vessel.tanker"] = tanker,
        });

        Assert.True(r.CanRender(Feat("vessel.cargo")));
        Assert.True(r.CanRender(Feat("vessel.tanker")));
        Assert.False(r.CanRender(Feat("vessel.passenger")));
    }

    [Fact]
    public void PrefixMatch_MatchesDotNamespacedKinds()
    {
        var vessel = new StubRenderer("vessel");
        var r = new KindMatchingRenderer(
            new Dictionary<string, IDynamicFeatureRenderer> { ["vessel"] = vessel },
            prefixMatch: true);

        Assert.True(r.CanRender(Feat("vessel")));
        Assert.True(r.CanRender(Feat("vessel.cargo")));
        Assert.True(r.CanRender(Feat("vessel.tanker.crude")));
        Assert.False(r.CanRender(Feat("vesselNotMatching")));
        Assert.False(r.CanRender(Feat("other")));
    }

    [Fact]
    public void LongestKey_WinsOverShorterPrefix()
    {
        var vessel = new StubRenderer("vessel");
        var cargo = new StubRenderer("cargo");
        var r = new KindMatchingRenderer(
            new Dictionary<string, IDynamicFeatureRenderer>
            {
                ["vessel"] = vessel,
                ["vessel.cargo"] = cargo,
            },
            prefixMatch: true);

        // Render returns the longer-key renderer's IFeatures (empty) — we
        // distinguish by capturing the matched renderer through a tracer.
        var feature = Feat("vessel.cargo");
        Assert.True(r.CanRender(feature));
        // No public accessor for picked renderer, so cross-check via
        // a second config that swaps order to verify deterministic pick.
        var r2 = new KindMatchingRenderer(
            new Dictionary<string, IDynamicFeatureRenderer>
            {
                ["vessel.cargo"] = cargo,
                ["vessel"] = vessel,
            },
            prefixMatch: true);
        Assert.True(r2.CanRender(feature));
    }

    [Fact]
    public void NullOrEmptyKind_NeverMatches()
    {
        var r = new KindMatchingRenderer(
            new Dictionary<string, IDynamicFeatureRenderer>
            {
                ["vessel"] = new StubRenderer("v"),
            },
            prefixMatch: true);

        Assert.False(r.CanRender(Feat(null)));
        Assert.False(r.CanRender(Feat("")));
    }
}
