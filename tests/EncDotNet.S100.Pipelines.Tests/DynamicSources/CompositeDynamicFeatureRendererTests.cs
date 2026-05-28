using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class CompositeDynamicFeatureRendererTests
{
    private sealed class FixedRenderer : IDynamicFeatureRenderer
    {
        private readonly bool _canRender;
        private readonly IFeature[] _payload;
        public FixedRenderer(bool canRender, params IFeature[] payload)
        {
            _canRender = canRender;
            _payload = payload;
        }
        public bool CanRender(DynamicFeature feature) => _canRender;
        public IEnumerable<IFeature> Render(DynamicFeature feature) => _payload;
    }

    private static DynamicFeature Feat() => new()
    {
        Id = "x",
        GeometryType = GeometryType.Point,
        Coordinates = new[] { (0.0, 0.0) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void FirstMatching_Wins()
    {
        var winner = new GeometryFeature(new Point(0, 0));
        var loser = new GeometryFeature(new Point(1, 1));
        var r = new CompositeDynamicFeatureRenderer(new IDynamicFeatureRenderer[]
        {
            new FixedRenderer(false, loser),
            new FixedRenderer(true, winner),
            new FixedRenderer(true, loser),
        });

        Assert.True(r.CanRender(Feat()));
        var output = r.Render(Feat()).ToArray();
        Assert.Single(output);
        Assert.Same(winner, output[0]);
    }

    [Fact]
    public void NoneMatch_ReturnsEmptyAndCannotRender()
    {
        var r = new CompositeDynamicFeatureRenderer(new IDynamicFeatureRenderer[]
        {
            new FixedRenderer(false),
            new FixedRenderer(false),
        });

        Assert.False(r.CanRender(Feat()));
        Assert.Empty(r.Render(Feat()));
    }
}
