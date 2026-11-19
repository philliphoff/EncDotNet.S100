using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public class DynamicSourcePickServiceTests
{
    private const double Lat = 47.6;
    private const double Lon = -122.3;

    // Focused fake: the pick service under test is the localised projection, so
    // this returns each visible source's features as hits directly (the
    // geometric hit-test itself is covered by DynamicSourceHitTesterTests).
    private sealed class FakeRegistry : IS100DynamicSourceRegistry
    {
        public List<IDynamicFeatureSource> Visible { get; } = new();

        public IDisposable Register(IDynamicFeatureSource source) =>
            throw new NotSupportedException();

        public IReadOnlyList<DynamicSourceRegistrationInfo> Sources =>
            Visible.Select(s => new DynamicSourceRegistrationInfo(s.Id, s.Metadata.DisplayName, s.Metadata.Description)).ToList();

        public bool GetVisible(string sourceId) => Visible.Any(s => s.Id == sourceId);
        public void SetVisible(string sourceId, bool visible) { }
        public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances() => Visible;

        public IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution) =>
            Visible
                .SelectMany(s => s.CurrentFeatures.Select(f => new DynamicSourceHit(s, f, 0.0)))
                .ToList();

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
    public void Pick_MakesAPickHit_WithAVesselSection()
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
                Coordinates = new[] { new GeoPosition(Lat, Lon) },
                Motion = new DynamicMotion
                {
                    CourseOverGround = Angle.FromDegrees(270.0),
                    Heading = Angle.FromDegrees(268.5),
                    SpeedOverGround = Speed.FromKnots(12.3),
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

        // Data source design note P5: an ordinary hit, as a processor describes it.
        var hit = Assert.Single(hits);
        Assert.Equal("vessel.ais.cargo", hit.FeatureType);
        Assert.Equal("AIS — test", hit.FeatureTypeName);
        Assert.Equal("367123456", hit.FeatureRef);
        Assert.Null(hit.DatasetFileName);
        Assert.Null(hit.OwningProcessor);
        var vessel = Assert.IsType<EncDotNet.S100.Datasets.Pipelines.VesselSection>(Assert.Single(hit.Sections));
        Assert.Equal("367123456", vessel.Mmsi);
        Assert.Equal("MV ALPHA", vessel.Name);
        Assert.Equal(270.0, vessel.CourseOverGroundDegrees);
        Assert.Equal(12.3, vessel.SpeedOverGroundKnots);
        Assert.Contains(hit.Attributes, r => r.Code == "callSign" && r.DisplayText == "ABC123");
        Assert.Contains(hit.Attributes, r => r.Code == "lastUpdated");
    }

    [Fact]
    public void Pick_OwnShip_IsAnOrdinaryHitWithNoTakeHelm()
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
                Coordinates = new[] { new GeoPosition(Lat, Lon) },
                LastUpdated = DateTimeOffset.UtcNow,
            },
        });
        registry.Visible.Add(source);

        var sut = new DynamicSourcePickService(registry);
        var hits = sut.Pick(ToMercator(Lat, Lon), resolution: 10.0);

        var hit = Assert.Single(hits);
        Assert.Equal("ownship", hit.FeatureType);
        Assert.Equal("Own Ship", hit.FeatureTypeName);
        // Own-ship can't take its own helm (P4).
        EncDotNet.S100.Viewer.Services.DynamicSources.TakeHelmPickAction.Register(EncDotNet.S100.Datasets.Pipelines.PickActionRegistry.Default);
        Assert.Empty(hit.Actions);
    }

    private sealed class DescribedSource : IDynamicFeatureSource, IDynamicFeatureDescriber
    {
        public string Id => "obs";
        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "Observations" };
        public IReadOnlyList<DynamicFeature> CurrentFeatures { get; init; } = [];
        public event EventHandler<DynamicFeaturesChanged>? Changed { add { } remove { } }

        public DynamicFeatureDescription? Describe(DynamicFeature feature, IFormatProvider? formatProvider = null) =>
            new("BOSTON 16 NM East of Boston, MA",
            [
                new DynamicFeatureProperty("Observed", "2026-10-27 14:50 UTC (10 min ago)"),
                new DynamicFeatureProperty("Wind", "230° 14.4 kn (7.4 m/s)"),
            ]);
    }

    [Fact]
    public void Pick_UsesTheSourcesOwnDescription_WhenItHasOne()
    {
        var registry = new FakeRegistry();
        registry.Visible.Add(new DescribedSource
        {
            CurrentFeatures =
            [
                new DynamicFeature
                {
                    Id = "ndbc:44013",
                    Kind = "weather.observation.ndbc",
                    GeometryType = GeometryType.Point,
                    Coordinates = [new GeoPosition(Lat, Lon)],
                    Attributes = new Dictionary<string, object?> { ["observation"] = new object() },
                    LastUpdated = DateTimeOffset.UnixEpoch,
                },
            ],
        });
        var sut = new DynamicSourcePickService(registry);

        var hit = Assert.Single(sut.Pick(ToMercator(Lat, Lon), resolution: 10.0));

        Assert.Equal("BOSTON 16 NM East of Boston, MA", hit.FeatureTypeName);
        Assert.Equal(2, hit.Attributes.Count); // the description's rows
        Assert.Equal("Wind", hit.Attributes[1].Name);
        Assert.Equal("230° 14.4 kn (7.4 m/s)", hit.Attributes[1].DisplayText);
        Assert.DoesNotContain(hit.Attributes, a => a.Code == "observation");
    }
}
