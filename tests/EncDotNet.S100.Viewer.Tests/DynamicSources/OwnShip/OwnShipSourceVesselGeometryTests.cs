using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

public class OwnShipSourceVesselGeometryTests
{
    private sealed class FakeGeometryProvider : IOwnShipVesselGeometryProvider
    {
        public DynamicVesselGeometry? Current { get; set; }
        public event EventHandler? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static OwnShipPosition Fix(
        double lat = 50.8, double lon = -1.3,
        double? cog = 90.0, double? sogMs = 5.0)
        => new(lat, lon, cog, sogMs, DateTimeOffset.UnixEpoch);

    [Fact]
    public void PublishedFeature_CarriesVesselGeometryFromProvider()
    {
        var stub = new StubOwnShipPositionProvider();
        var geom = new FakeGeometryProvider
        {
            Current = new DynamicVesselGeometry
            {
                LengthMetres = 50, BeamMetres = 10,
                BowOffsetMetres = 25, PortOffsetMetres = 5,
            },
        };
        using var src = new OwnShipSource(stub, geom);
        stub.Push(Fix());

        var feat = Assert.Single(src.CurrentFeatures);
        Assert.NotNull(feat.VesselGeometry);
        Assert.Equal(50, feat.VesselGeometry!.LengthMetres);
    }

    [Fact]
    public void Metadata_RendererKey_IsOwnship()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        Assert.Equal("ownship", src.Metadata.RendererKey);
    }

    [Fact]
    public void GeometryChange_AfterFix_RepublishesCurrentFix_WithNewGeometry()
    {
        var stub = new StubOwnShipPositionProvider();
        var geom = new FakeGeometryProvider
        {
            Current = new DynamicVesselGeometry
            {
                LengthMetres = 50, BeamMetres = 10,
                BowOffsetMetres = 25, PortOffsetMetres = 5,
            },
        };
        using var src = new OwnShipSource(stub, geom);
        stub.Push(Fix());

        var events = new List<DynamicFeaturesChanged>();
        src.Changed += (_, e) => events.Add(e);

        geom.Current = new DynamicVesselGeometry
        {
            LengthMetres = 100, BeamMetres = 20,
            BowOffsetMetres = 50, PortOffsetMetres = 10,
        };
        geom.Raise();

        var ev = Assert.Single(events);
        Assert.Equal(DynamicSourceChangeKind.Updated, ev.Kind);
        Assert.Contains("ownship", ev.ChangedIds);

        var feat = Assert.Single(src.CurrentFeatures);
        Assert.Equal(100, feat.VesselGeometry!.LengthMetres);
    }

    [Fact]
    public void GeometryChange_BeforeAnyFix_IsNoOp()
    {
        var stub = new StubOwnShipPositionProvider();
        var geom = new FakeGeometryProvider();
        using var src = new OwnShipSource(stub, geom);

        var events = new List<DynamicFeaturesChanged>();
        src.Changed += (_, e) => events.Add(e);

        geom.Raise();

        Assert.Empty(events);
        Assert.Empty(src.CurrentFeatures);
    }
}
