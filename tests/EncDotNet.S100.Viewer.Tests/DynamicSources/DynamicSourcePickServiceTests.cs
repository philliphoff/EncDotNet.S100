using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Projections;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public class DynamicSourcePickServiceTests
{
    private const double Lat = 47.6;
    private const double Lon = -122.3;

    private sealed class FakeRegistry : IDynamicFeatureSourceRegistry
    {
        public List<IDynamicFeatureSource> Visible { get; } = new();

        public IReadOnlyList<DynamicSourceRegistrationInfo> Sources =>
            Visible.Select(s => new DynamicSourceRegistrationInfo(s.Id, s.Metadata.DisplayName, s.Metadata.Description)).ToList();

        public bool GetVisible(string sourceId) => Visible.Any(s => s.Id == sourceId);
        public void SetVisible(string sourceId, bool visible) { }
        public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances() => Visible;
        public event Action? SourcesChanged { add { } remove { } }
    }

    private static MPoint ToMercator(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        return new MPoint(x, y);
    }

    [Fact]
    public void Pick_ReturnsEmpty_WhenNoSourcesVisible()
    {
        var sut = new DynamicSourcePickService(new FakeRegistry());

        var hits = sut.Pick(ToMercator(Lat, Lon), resolution: 10.0);

        Assert.Empty(hits);
    }

    [Fact]
    public void Pick_ProjectsHit_WithAttributesAndMotion()
    {
        var registry = new FakeRegistry();
        var source = new FakeDynamicFeatureSource("ais.test", new DynamicSourceMetadata
        {
            DisplayName = "AIS — test",
            RendererKey = "ais",
        });
        source.SetFeatures(new[]
        {
            new DynamicFeature
            {
                Id = "367123456",
                Kind = "vessel.ais.cargo",
                GeometryType = GeometryType.Point,
                Coordinates = new[] { (Lat, Lon) },
                Motion = new DynamicMotion
                {
                    CourseOverGroundDeg = 270.0,
                    HeadingDeg = 268.5,
                    SpeedOverGroundKn = 12.3,
                },
                Attributes = new Dictionary<string, object?>
                {
                    ["mmsi"] = 367123456,
                    ["vesselName"] = "MV ALPHA",
                    ["callSign"] = "ABC123",
                },
                LastUpdated = DateTimeOffset.UtcNow,
            },
        });
        registry.Visible.Add(source);

        var sut = new DynamicSourcePickService(registry);
        var hits = sut.Pick(ToMercator(Lat, Lon), resolution: 10.0);

        var hit = Assert.Single(hits);
        Assert.Equal("ais.test", hit.SourceId);
        Assert.Equal("AIS — test", hit.SourceDisplayName);
        Assert.Equal("367123456", hit.FeatureId);
        Assert.Equal("vessel.ais.cargo", hit.Kind);
        Assert.Equal("MV ALPHA", hit.DisplayLabel); // vesselName wins over feature id
        Assert.Equal(Lat, hit.Latitude);
        Assert.Equal(Lon, hit.Longitude);
        Assert.NotNull(hit.Motion);
        Assert.Equal(270.0, hit.Motion!.CourseOverGroundDeg);

        // Attribute rows include Position, COG, Heading, SOG, then declared attributes.
        Assert.Contains(hit.Attributes, r => r.Label == "Position");
        Assert.Contains(hit.Attributes, r => r.Label == "COG" && r.Value.Contains("270"));
        Assert.Contains(hit.Attributes, r => r.Label == "SOG" && r.Value.Contains("12.3"));
        Assert.Contains(hit.Attributes, r => r.Label == "MMSI");
        Assert.Contains(hit.Attributes, r => r.Label == "Name" && r.Value == "MV ALPHA");
        Assert.Contains(hit.Attributes, r => r.Label == "Call sign" && r.Value == "ABC123");
    }

    [Fact]
    public void Pick_FallsBackToFeatureId_WhenVesselNameMissing()
    {
        var registry = new FakeRegistry();
        var source = new FakeDynamicFeatureSource("ownship", new DynamicSourceMetadata { DisplayName = "Own Ship" });
        source.SetFeatures(new[]
        {
            new DynamicFeature
            {
                Id = "ownship",
                Kind = "ownship",
                GeometryType = GeometryType.Point,
                Coordinates = new[] { (Lat, Lon) },
                LastUpdated = DateTimeOffset.UtcNow,
            },
        });
        registry.Visible.Add(source);

        var sut = new DynamicSourcePickService(registry);
        var hits = sut.Pick(ToMercator(Lat, Lon), resolution: 10.0);

        Assert.Equal("ownship", Assert.Single(hits).DisplayLabel);
    }
}
