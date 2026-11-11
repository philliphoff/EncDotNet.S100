using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public sealed class PirateModeControllerTests
{
    private sealed record SetStateCall(double Lat, double Lon, double? Cog, double? SogMs, double? Heading);

    private sealed class RecordingHelm : IOwnShipHelm
    {
        public List<SetStateCall> States { get; } = new();

        public void SetState(double latitude, double longitude, double? courseOverGroundDeg = null, double? speedOverGroundMs = null, double? headingDeg = null)
            => States.Add(new SetStateCall(latitude, longitude, courseOverGroundDeg, speedOverGroundMs, headingDeg));
        public void SetCourse(double courseDeg) { }
        public void NudgeCourse(double deltaDeg) { }
        public void SetSpeed(double speedMs) { }
        public void NudgeSpeed(double deltaMs) { }
        public void SetTurnRate(double degreesPerSecond) { }
        public void SteerToward(double latitude, double longitude) { }
        public void Hold() { }
        public void Resume() { }
    }

    private sealed class RecordingGeometryOverride : IOwnShipVesselGeometryOverride
    {
        public int SetCount { get; private set; }
        public int ClearCount { get; private set; }
        public DynamicVesselGeometry? Last { get; private set; }
        public bool LastWasNull { get; private set; }

        public void SetOverride(DynamicVesselGeometry? geometry)
        {
            SetCount++;
            Last = geometry;
            LastWasNull = geometry is null;
        }

        public void ClearOverride() => ClearCount++;
    }

    private static DynamicFeature Target(
        string id = "ais:123",
        double lat = 50.0, double lon = -1.0,
        double? cog = 90.0, double? heading = 88.0, double? sogKn = 10.0,
        DynamicVesselGeometry? geometry = null)
        => new()
        {
            Id = id,
            Kind = "vessel.ais.cargo",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { new GeoPosition(lat, lon) },
            Motion = new DynamicMotion
            {
                CourseOverGround = cog is { } c ? Angle.FromDegrees(c) : null,
                Heading = heading is { } h ? Angle.FromDegrees(h) : null,
                SpeedOverGround = sogKn is { } s ? Speed.FromKnots(s) : null,
            },
            VesselGeometry = geometry,
            LastUpdated = DateTimeOffset.UnixEpoch,
        };

    private static FakeDynamicFeatureSource Raw(params DynamicFeature[] features)
    {
        var raw = new FakeDynamicFeatureSource(
            "ais",
            new DynamicSourceMetadata { DisplayName = "AIS targets", RendererKey = "vessel.ais" });
        raw.SetFeatures(features);
        return raw;
    }

    private static (PirateModeController Controller, RecordingHelm Helm, RecordingGeometryOverride Geom, ExcludingAisFeatureSource Exclusion)
        Build(FakeDynamicFeatureSource raw)
    {
        var exclusion = new ExcludingAisFeatureSource(raw);
        var helm = new RecordingHelm();
        var geom = new RecordingGeometryOverride();
        var controller = new PirateModeController(exclusion, helm, geom);
        return (controller, helm, geom, exclusion);
    }

    [Fact]
    public void Follow_KnownTarget_AppliesFixAndExcludes()
    {
        var geometry = new DynamicVesselGeometry { LengthMetres = 200, BeamMetres = 30, BowOffsetMetres = 100, PortOffsetMetres = 15 };
        var raw = Raw(Target(sogKn: 10.0, geometry: geometry));
        var (controller, helm, geom, exclusion) = Build(raw);

        var outcome = controller.Follow(123);

        Assert.Equal(PirateFollowOutcome.AppliedFix, outcome);
        Assert.True(controller.IsActive);
        Assert.Equal(123u, controller.FollowedMmsi);
        Assert.Equal("ais:123", exclusion.ExcludedId);

        var call = Assert.Single(helm.States);
        Assert.Equal(50.0, call.Lat);
        Assert.Equal(-1.0, call.Lon);
        Assert.Equal(90.0, call.Cog);
        // Heading is deliberately not adopted from AIS; the helm has no
        // gyro-heading control, so passing null lets the arrow mirror
        // course as the user steers.
        Assert.Null(call.Heading);
        Assert.NotNull(call.SogMs);
        Assert.Equal(10.0 * 0.514_444_444, call.SogMs!.Value, 4);

        Assert.Equal(1, geom.SetCount);
        Assert.Same(geometry, geom.Last);
        Assert.NotNull(controller.LastFixUtc);
    }

    [Fact]
    public void Follow_UnknownTarget_ArmsWaiting()
    {
        var raw = Raw(); // empty
        var (controller, helm, _, exclusion) = Build(raw);

        var outcome = controller.Follow(123);

        Assert.Equal(PirateFollowOutcome.ArmedWaiting, outcome);
        Assert.True(controller.IsActive);
        Assert.Equal("ais:123", exclusion.ExcludedId);
        Assert.Empty(helm.States);
        Assert.Null(controller.LastFixUtc);
    }

    [Fact]
    public void Update_AfterAdoption_DoesNotApply()
    {
        // Adopt-and-detach: once the initial fix has landed, subsequent
        // AIS updates for the followed target must NOT be applied —
        // otherwise the user's helm commands would be silently overwritten
        // on the next AIS tick.
        var raw = Raw(Target(lat: 50.0, lon: -1.0));
        var (controller, helm, _, _) = Build(raw);
        controller.Follow(123);

        raw.SetFeatures(new[] { Target(lat: 51.0, lon: -2.0) });
        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:123" },
        });

        Assert.Single(helm.States);
        Assert.Equal(50.0, helm.States[0].Lat);
        Assert.Equal(-1.0, helm.States[0].Lon);
    }

    [Fact]
    public void Update_WhileArmedWaiting_AdoptsFirstReport()
    {
        // Armed-waiting: target was not yet in the AIS snapshot when
        // Follow ran, so the very next report for that target must
        // adopt. After that, further reports are ignored.
        var raw = Raw(); // empty
        var (controller, helm, geom, _) = Build(raw);
        controller.Follow(123);

        Assert.Empty(helm.States);
        Assert.Null(controller.LastFixUtc);

        raw.SetFeatures(new[] { Target(lat: 51.0, lon: -2.0) });
        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:123" },
        });

        Assert.Single(helm.States);
        Assert.Equal(51.0, helm.States[0].Lat);
        Assert.NotNull(controller.LastFixUtc);
        Assert.Equal(1, geom.SetCount);

        // A second update must not push another fix.
        raw.SetFeatures(new[] { Target(lat: 52.0, lon: -3.0) });
        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:123" },
        });

        Assert.Single(helm.States);
    }

    [Fact]
    public void Update_ForOtherTarget_Ignored()
    {
        var raw = Raw(Target(id: "ais:123"));
        var (controller, helm, _, _) = Build(raw);
        controller.Follow(123);

        raw.SetFeatures(new[] { Target(id: "ais:123"), Target(id: "ais:999") });
        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:999" },
        });

        Assert.Single(helm.States);
    }

    [Fact]
    public void Stop_ClearsExclusionAndGeometry_LeavesHelm()
    {
        var raw = Raw(Target(geometry: new DynamicVesselGeometry { LengthMetres = 100, BeamMetres = 20, BowOffsetMetres = 50, PortOffsetMetres = 10 }));
        var (controller, helm, geom, exclusion) = Build(raw);
        controller.Follow(123);
        var helmCallsBefore = helm.States.Count;

        controller.Stop();

        Assert.False(controller.IsActive);
        Assert.Null(controller.FollowedMmsi);
        Assert.Null(exclusion.ExcludedId);
        Assert.Equal(1, geom.ClearCount);
        // No teleport: the helm is not driven again on stop.
        Assert.Equal(helmCallsBefore, helm.States.Count);
    }

    [Fact]
    public void Stop_AfterStop_IsNoOp()
    {
        var raw = Raw(Target());
        var (controller, _, geom, _) = Build(raw);
        controller.Follow(123);
        controller.Stop();
        var clearsAfterFirstStop = geom.ClearCount;

        controller.Stop();

        Assert.Equal(clearsAfterFirstStop, geom.ClearCount);
    }

    [Fact]
    public void Retarget_SwitchesExclusionAndIgnoresOldTarget()
    {
        var raw = Raw(Target(id: "ais:123"), Target(id: "ais:456", lat: 10, lon: 20));
        var (controller, helm, _, exclusion) = Build(raw);

        controller.Follow(123);
        controller.Follow(456);

        Assert.Equal("ais:456", exclusion.ExcludedId);
        Assert.Equal(456u, controller.FollowedMmsi);
        var callsAfterRetarget = helm.States.Count;

        // An update for the old target must be ignored.
        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:123" },
        });

        Assert.Equal(callsAfterRetarget, helm.States.Count);
    }

    [Fact]
    public void FollowedTargetRemoved_KeepsLastFix_NoNewHelmCall()
    {
        var raw = Raw(Target());
        var (controller, helm, _, _) = Build(raw);
        controller.Follow(123);
        var callsBefore = helm.States.Count;

        // Target aged out / lost.
        raw.SetFeatures(Array.Empty<DynamicFeature>());
        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Removed,
            ChangedIds = new[] { "ais:123" },
        });

        Assert.True(controller.IsActive); // still armed
        Assert.Equal(callsBefore, helm.States.Count); // dead-reckoning continues
    }

    [Fact]
    public void TargetWithNoDimensions_OverridesWithNull()
    {
        var raw = Raw(Target(geometry: null));
        var (controller, _, geom, _) = Build(raw);

        controller.Follow(123);

        Assert.Equal(1, geom.SetCount);
        Assert.True(geom.LastWasNull);
    }

    [Fact]
    public void Reset_AfterAdoption_DoesNotReApply()
    {
        // Adopt-and-detach: a Reset event after adoption is ignored
        // just like any other update.
        var raw = Raw(Target(lat: 50, lon: -1));
        var (controller, helm, _, _) = Build(raw);
        controller.Follow(123);

        raw.SetFeatures(new[] { Target(lat: 60, lon: -3) });
        raw.RaiseChanged(new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset });

        Assert.Single(helm.States);
        Assert.Equal(50.0, helm.States[0].Lat);
    }

    [Fact]
    public void Reset_WhileArmedWaiting_AdoptsFix()
    {
        // Reset carries no ids but must still let armed-waiting adopt
        // if the target is now present in the new snapshot.
        var raw = Raw(); // empty
        var (controller, helm, _, _) = Build(raw);
        controller.Follow(123);

        raw.SetFeatures(new[] { Target(lat: 60, lon: -3) });
        raw.RaiseChanged(new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset });

        Assert.Single(helm.States);
        Assert.Equal(60.0, helm.States[0].Lat);
    }

    [Fact]
    public void Dispose_StopsTrackingUpdates()
    {
        var raw = Raw(Target());
        var (controller, helm, _, _) = Build(raw);
        controller.Follow(123);
        var callsBefore = helm.States.Count;

        controller.Dispose();

        raw.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:123" },
        });

        Assert.Equal(callsBefore, helm.States.Count);
    }
}
