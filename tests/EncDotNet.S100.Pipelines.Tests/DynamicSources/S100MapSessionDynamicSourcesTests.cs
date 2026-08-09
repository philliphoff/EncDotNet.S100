using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

/// <summary>
/// The reusable session (<c>map.AddS100(...)</c>) exposes dynamic-source hosting
/// through <see cref="IS100MapSession.DynamicSources"/> and owns its lifecycle.
/// </summary>
public class S100MapSessionDynamicSourcesTests
{
    private const double Lat = 47.6;
    private const double Lon = -122.3;

    private static DynamicFeature Point(string id, double lat, double lon) => new()
    {
        Id = id,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { new GeoPosition(lat, lon) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Register_InstallsOverlayLayer_HitTest_Visibility_And_DisposeRemovesIt()
    {
        using var map = new Map();
        // Default (inline) marshal keeps the register/rebuild synchronous.
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new IdentityCrsTransformFactory(),
        });

        var source = new FakeDynamicFeatureSource(
            "ownship", new DynamicSourceMetadata { DisplayName = "Own Ship" });
        source.SetFeatures(new[] { Point("ownship", Lat, Lon) });

        using var registration = s100.DynamicSources.Register(source);

        // The overlay layer is installed on the map.
        var overlay = Assert.IsType<MemoryLayer>(
            Assert.Single(map.Layers, l => l is MemoryLayer m && m.Name.StartsWith("Dynamic Source:", StringComparison.Ordinal)));
        Assert.NotEmpty(overlay.Features);
        Assert.Equal(new[] { "ownship" }, s100.DynamicSources.Sources.Select(s => s.Id));

        // Hit-testing at the feature location returns it.
        var (x, y) = SphericalMercator.FromLonLat(Lon, Lat);
        var hit = Assert.Single(s100.DynamicSources.HitTest(new MPoint(x, y), resolution: 10.0));
        Assert.Equal("ownship", hit.Feature.Id);

        // Hiding the source disables its layer and drops it from the pick.
        s100.DynamicSources.SetVisible("ownship", false);
        Assert.False(overlay.Enabled);
        Assert.Empty(s100.DynamicSources.HitTest(new MPoint(x, y), resolution: 10.0));

        // Disposing the session removes the overlay layer.
        s100.Dispose();
        Assert.DoesNotContain(map.Layers, l => ReferenceEquals(l, overlay));
    }
}
