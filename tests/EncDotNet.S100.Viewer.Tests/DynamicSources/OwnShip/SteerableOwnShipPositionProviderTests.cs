using System;
using System.Collections.Generic;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

public sealed class SteerableOwnShipPositionProviderTests
{
    private static OwnShipPosition Start(
        double cog = 90.0, double sogMs = 1.0,
        double lat = 0.0, double lon = 0.0, double? heading = null)
        => new(Latitude: lat, Longitude: lon,
            CourseOverGround: Angle.FromDegrees(cog), SpeedOverGround: Speed.FromMetresPerSecond(sogMs),
            Timestamp: DateTimeOffset.UnixEpoch, Heading: heading is { } h ? Angle.FromDegrees(h) : null);

    // ---- Parity with the previous synthetic driver --------------------

    [Fact]
    public void Tick_DueEast_MovesEastButNotNorth()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 1.0));

        provider.Tick(TimeSpan.FromHours(1));

        var c = provider.Current!;
        Assert.InRange(c.Latitude, -1e-6, 1e-6);
        // 3600 m at the equator ≈ 0.03237° of longitude.
        Assert.InRange(c.Longitude, 0.032, 0.033);
    }

    [Fact]
    public void Tick_DueNorth_MovesNorthButNotEast()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 0.0, sogMs: 1.0));

        provider.Tick(TimeSpan.FromHours(1));

        var c = provider.Current!;
        Assert.InRange(c.Latitude, 0.032, 0.033);
        Assert.InRange(c.Longitude, -1e-6, 1e-6);
    }

    [Fact]
    public void Tick_RaisesUpdatedWithNewFix()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 1.0));

        var fixes = new List<OwnShipPosition>();
        provider.Updated += (_, p) => fixes.Add(p);

        provider.Tick(TimeSpan.FromSeconds(1));
        provider.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(2, fixes.Count);
        Assert.True(fixes[1].Longitude > fixes[0].Longitude);
    }

    [Fact]
    public void Tick_PreservesCourseAndSpeed()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 45.0, sogMs: 7.5));

        provider.Tick(TimeSpan.FromMinutes(5));

        var c = provider.Current!;
        Assert.Equal(45.0, c.CourseOverGround?.TotalDegrees);
        Assert.Equal(7.5, c.SpeedOverGround?.TotalMetresPerSecond);
    }

    // ---- Construction tolerates a motion-less seed --------------------

    [Fact]
    public void Constructor_AllowsNullCourseAndSpeed()
    {
        var start = new OwnShipPosition(10, 20, null, null, DateTimeOffset.UnixEpoch);
        using var provider = SteerableOwnShipPositionProvider.CreateManual(start);

        var c = provider.Current!;
        Assert.Equal(0.0, c.CourseOverGround?.TotalDegrees);
        Assert.Equal(0.0, c.SpeedOverGround?.TotalMetresPerSecond);
        Assert.Equal(10, c.Latitude);
        Assert.Equal(20, c.Longitude);
    }

    [Fact]
    public void Tick_WhenStationary_DoesNotMove()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 0.0));

        provider.Tick(TimeSpan.FromHours(1));

        var c = provider.Current!;
        Assert.Equal(0.0, c.Latitude, 1e-9);
        Assert.Equal(0.0, c.Longitude, 1e-9);
    }

    // ---- Helm: absolute and relative steering -------------------------

    [Fact]
    public void SetState_ReplacesPositionAndKinematics()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 5.0));

        provider.SetState(latitude: 47.6, longitude: -122.3,
            courseOverGroundDeg: 180.0, speedOverGroundMs: 3.0, headingDeg: 175.0);

        var c = provider.Current!;
        Assert.Equal(47.6, c.Latitude);
        Assert.Equal(-122.3, c.Longitude);
        Assert.Equal(180.0, c.CourseOverGround?.TotalDegrees);
        Assert.Equal(3.0, c.SpeedOverGround?.TotalMetresPerSecond);
        Assert.Equal(175.0, c.Heading?.TotalDegrees);
    }

    [Fact]
    public void SetState_NullComponents_LeaveExistingState()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 5.0));

        provider.SetState(latitude: 1.0, longitude: 2.0);

        var c = provider.Current!;
        Assert.Equal(1.0, c.Latitude);
        Assert.Equal(2.0, c.Longitude);
        Assert.Equal(90.0, c.CourseOverGround?.TotalDegrees);
        Assert.Equal(5.0, c.SpeedOverGround?.TotalMetresPerSecond);
    }

    [Fact]
    public void SetState_RaisesUpdated()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 5.0));
        OwnShipPosition? last = null;
        provider.Updated += (_, p) => last = p;

        provider.SetState(latitude: 5.0, longitude: 6.0);

        Assert.NotNull(last);
        Assert.Equal(5.0, last!.Latitude);
    }

    [Fact]
    public void SetCourse_And_NudgeCourse_Normalize()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 10.0, sogMs: 1.0));

        provider.SetCourse(370.0);
        Assert.Equal(10.0, provider.Current!.CourseOverGround!.Value.TotalDegrees, 1e-9);

        provider.NudgeCourse(-20.0);
        Assert.Equal(350.0, provider.Current!.CourseOverGround!.Value.TotalDegrees, 1e-9);
    }

    [Fact]
    public void SetSpeed_And_NudgeSpeed_ClampNonNegative()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 5.0));

        provider.SetSpeed(-3.0);
        Assert.Equal(0.0, provider.Current!.SpeedOverGround?.TotalMetresPerSecond);

        provider.NudgeSpeed(2.5);
        Assert.Equal(2.5, provider.Current!.SpeedOverGround?.TotalMetresPerSecond);

        provider.NudgeSpeed(-10.0);
        Assert.Equal(0.0, provider.Current!.SpeedOverGround?.TotalMetresPerSecond);
    }

    [Fact]
    public void Hold_Then_Resume_RestoresSpeed()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 6.0));

        provider.Hold();
        Assert.Equal(0.0, provider.Current!.SpeedOverGround?.TotalMetresPerSecond);

        provider.Resume();
        Assert.Equal(6.0, provider.Current!.SpeedOverGround?.TotalMetresPerSecond);
    }

    [Fact]
    public void Resume_WhenMoving_IsNoOp()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 6.0));

        provider.Resume();
        Assert.Equal(6.0, provider.Current!.SpeedOverGround?.TotalMetresPerSecond);
    }

    // ---- Helm-state readback (IOwnShipHelmState) ----------------------

    [Fact]
    public void HelmState_ReflectsHoldResume()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 6.0));

        Assert.False(provider.IsHeld);
        Assert.Equal(6.0, provider.CommandedSpeedMs);

        provider.Hold();
        Assert.True(provider.IsHeld);
        Assert.Equal(6.0, provider.CommandedSpeedMs); // remembered for Resume

        provider.Resume();
        Assert.False(provider.IsHeld);
        Assert.Equal(6.0, provider.CommandedSpeedMs);
    }

    [Fact]
    public void HelmState_ReflectsCommandedSpeed()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 6.0));

        provider.SetSpeed(3.5);
        Assert.Equal(3.5, provider.CommandedSpeedMs);
        Assert.False(provider.IsHeld);
    }

    [Fact]
    public void HelmState_ReflectsTurnRate()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 0.0, sogMs: 0.0));

        Assert.Equal(0.0, provider.TurnRateDegPerSec);

        provider.SetTurnRate(2.5);
        Assert.Equal(2.5, provider.TurnRateDegPerSec);
    }

    [Fact]
    public void SteerToward_PointsCourseAtTarget()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 0.0, sogMs: 1.0, lat: 0.0, lon: 0.0));

        // A point due east on the equator → bearing 90°.
        provider.SteerToward(latitude: 0.0, longitude: 10.0);
        Assert.Equal(90.0, provider.Current!.CourseOverGround!.Value.TotalDegrees, 1e-3);

        // A point due north → bearing 0°.
        provider.SteerToward(latitude: 10.0, longitude: 0.0);
        Assert.Equal(0.0, provider.Current!.CourseOverGround!.Value.TotalDegrees, 1e-3);
    }

    // ---- Turn rate ----------------------------------------------------

    [Fact]
    public void SetTurnRate_RotatesCourseOverTime()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 0.0, sogMs: 0.0));

        provider.SetTurnRate(3.0); // 3°/s
        provider.Tick(TimeSpan.FromSeconds(10));

        Assert.Equal(30.0, provider.Current!.CourseOverGround!.Value.TotalDegrees, 1e-6);
    }

    [Fact]
    public void TurnRate_WrapsThrough360()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 350.0, sogMs: 0.0));

        provider.SetTurnRate(2.0);
        provider.Tick(TimeSpan.FromSeconds(10)); // +20° → 370° → 10°

        Assert.Equal(10.0, provider.Current!.CourseOverGround!.Value.TotalDegrees, 1e-6);
    }

    // ---- Heading passthrough ------------------------------------------

    [Fact]
    public void Heading_DefaultsToNull_SoSourceMirrorsCourse()
    {
        using var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 1.0, heading: null));

        Assert.Null(provider.Current!.Heading);
    }

    // ---- Lifecycle ----------------------------------------------------

    [Fact]
    public void Tick_AfterDispose_DoesNothing()
    {
        var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 1.0));
        var before = provider.Current!;
        provider.Dispose();

        provider.Tick(TimeSpan.FromHours(1));

        Assert.Equal(before.Longitude, provider.Current!.Longitude);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var provider = SteerableOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 1.0));
        provider.Dispose();
        provider.Dispose(); // must not throw
    }
}
