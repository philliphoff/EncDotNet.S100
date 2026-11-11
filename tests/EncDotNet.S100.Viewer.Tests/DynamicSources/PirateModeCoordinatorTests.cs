using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public sealed class PirateModeCoordinatorTests
{
    private sealed class StubHelm : IOwnShipHelm
    {
        public void SetState(double latitude, double longitude, double? courseOverGroundDeg = null, double? speedOverGroundMs = null, double? headingDeg = null) { }
        public void SetCourse(double courseDeg) { }
        public void NudgeCourse(double deltaDeg) { }
        public void SetSpeed(double speedMs) { }
        public void NudgeSpeed(double deltaMs) { }
        public void SetTurnRate(double degreesPerSecond) { }
        public void SteerToward(double latitude, double longitude) { }
        public void Hold() { }
        public void Resume() { }
    }

    private sealed class StubGeometryOverride : IOwnShipVesselGeometryOverride
    {
        public void SetOverride(DynamicVesselGeometry? geometry) { }
        public void ClearOverride() { }
    }

    private sealed class FakeRegistry : IS100DynamicSourceRegistry
    {
        public Dictionary<string, bool> Visible { get; } = new();

        public IDisposable Register(IDynamicFeatureSource source) => throw new NotSupportedException();
        public IReadOnlyList<DynamicSourceRegistrationInfo> Sources => Array.Empty<DynamicSourceRegistrationInfo>();
        public bool GetVisible(string sourceId) => Visible.TryGetValue(sourceId, out var v) ? v : true;
        public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances() => Array.Empty<IDynamicFeatureSource>();
        public IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution) => Array.Empty<DynamicSourceHit>();
        public void SetVisible(string sourceId, bool visible) => Visible[sourceId] = visible;
        public event Action? SourcesChanged { add { } remove { } }
    }

    private static DynamicFeature Target(string id = "ais:123") => new()
    {
        Id = id,
        Kind = "vessel.ais.cargo",
        GeometryType = GeometryType.Point,
        Coordinates = new[] { new GeoPosition(50.0, -1.0) },
        Motion = new DynamicMotion { CourseOverGround = Angle.FromDegrees(90.0), Heading = Angle.FromDegrees(88.0), SpeedOverGround = Speed.FromKnots(10.0) },
        LastUpdated = DateTimeOffset.UnixEpoch,
    };

    private static (PirateModeController Controller, ExcludingAisFeatureSource Exclusion) Build(params DynamicFeature[] features)
    {
        var raw = new FakeDynamicFeatureSource(
            "ais",
            new DynamicSourceMetadata { DisplayName = "AIS targets", RendererKey = "vessel.ais" });
        raw.SetFeatures(features);
        var exclusion = new ExcludingAisFeatureSource(raw);
        var controller = new PirateModeController(exclusion, new StubHelm(), new StubGeometryOverride());
        return (controller, exclusion);
    }

    [Fact]
    public void Engage_OpensBothGates_PersistsSettings_AndFollows()
    {
        var (controller, exclusion) = Build(Target("ais:123456789"));
        var registry = new FakeRegistry();
        var settings = new ViewerSettings { IsReadOnly = true };
        var overlayEnabled = false;

        var coordinator = new PirateModeCoordinator(
            controller, registry, settings, e => overlayEnabled = e);

        var outcome = coordinator.Engage(123456789u);

        Assert.Equal(PirateFollowOutcome.AppliedFix, outcome);
        Assert.True(overlayEnabled);
        Assert.True(registry.GetVisible(OwnShipSource.FeatureId));
        Assert.True(settings.DynamicSourceVisibility[ViewerSettings.OwnShipVisibilityKey]);
        Assert.Equal(OwnShipPositionSource.FollowAisTarget.ToString(), settings.OwnShipPositionSource);
        Assert.Equal(123456789u, settings.OwnShipFollowMmsi);
        Assert.True(controller.IsActive);
        Assert.Equal(123456789u, controller.FollowedMmsi);
        Assert.Equal("ais:123456789", exclusion.ExcludedId);
    }

    [Fact]
    public void Disengage_StopsController_AndRevertsSettings()
    {
        var (controller, _) = Build(Target("ais:123456789"));
        var registry = new FakeRegistry();
        var settings = new ViewerSettings { IsReadOnly = true };

        var coordinator = new PirateModeCoordinator(
            controller, registry, settings, _ => { });

        coordinator.Engage(123456789u);
        coordinator.Disengage();

        Assert.False(controller.IsActive);
        Assert.Equal(OwnShipPositionSource.Simulated.ToString(), settings.OwnShipPositionSource);
        Assert.Null(settings.OwnShipFollowMmsi);
    }

    [Fact]
    public void RestoreFromSettings_WhenFollowConfigured_ReArmsAndOpensGates()
    {
        var (controller, _) = Build(Target("ais:555"));
        var registry = new FakeRegistry();
        var settings = new ViewerSettings
        {
            IsReadOnly = true,
            OwnShipPositionSource = "FollowAisTarget",
            OwnShipFollowMmsi = 555u,
        };
        var overlayEnabled = false;

        var coordinator = new PirateModeCoordinator(
            controller, registry, settings, e => overlayEnabled = e);

        var outcome = coordinator.RestoreFromSettings();

        Assert.Equal(PirateFollowOutcome.AppliedFix, outcome);
        Assert.True(overlayEnabled);
        Assert.True(controller.IsActive);
        Assert.Equal(555u, controller.FollowedMmsi);
    }

    [Fact]
    public void RestoreFromSettings_WhenSimulated_DoesNothing()
    {
        var (controller, _) = Build(Target("ais:555"));
        var registry = new FakeRegistry();
        var settings = new ViewerSettings { IsReadOnly = true, OwnShipPositionSource = "Simulated" };
        var overlayEnabled = false;

        var coordinator = new PirateModeCoordinator(
            controller, registry, settings, e => overlayEnabled = e);

        var outcome = coordinator.RestoreFromSettings();

        Assert.Null(outcome);
        Assert.False(overlayEnabled);
        Assert.False(controller.IsActive);
    }
}
