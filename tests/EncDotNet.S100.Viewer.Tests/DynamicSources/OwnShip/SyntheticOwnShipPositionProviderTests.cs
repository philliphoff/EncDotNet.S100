using System;
using System.Collections.Generic;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

public sealed class SyntheticOwnShipPositionProviderTests
{
    private static OwnShipPosition Start(double cog, double sogMs)
        => new(Latitude: 0.0, Longitude: 0.0,
            CourseOverGroundDeg: cog, SpeedOverGroundMs: sogMs,
            Timestamp: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_RejectsMissingCourse()
    {
        var start = new OwnShipPosition(0, 0, null, 5.0, DateTimeOffset.UnixEpoch);
        Assert.Throws<ArgumentException>(() =>
            SyntheticOwnShipPositionProvider.CreateManual(start));
    }

    [Fact]
    public void Constructor_RejectsMissingSpeed()
    {
        var start = new OwnShipPosition(0, 0, 90.0, null, DateTimeOffset.UnixEpoch);
        Assert.Throws<ArgumentException>(() =>
            SyntheticOwnShipPositionProvider.CreateManual(start));
    }

    [Fact]
    public void Tick_DueEast_MovesEastButNotNorth()
    {
        using var provider = SyntheticOwnShipPositionProvider.CreateManual(
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
        using var provider = SyntheticOwnShipPositionProvider.CreateManual(
            Start(cog: 0.0, sogMs: 1.0));

        provider.Tick(TimeSpan.FromHours(1));

        var c = provider.Current!;
        Assert.InRange(c.Latitude, 0.032, 0.033);
        Assert.InRange(c.Longitude, -1e-6, 1e-6);
    }

    [Fact]
    public void Tick_RaisesUpdatedWithNewFix()
    {
        using var provider = SyntheticOwnShipPositionProvider.CreateManual(
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
        using var provider = SyntheticOwnShipPositionProvider.CreateManual(
            Start(cog: 45.0, sogMs: 7.5));

        provider.Tick(TimeSpan.FromMinutes(5));

        var c = provider.Current!;
        Assert.Equal(45.0, c.CourseOverGroundDeg);
        Assert.Equal(7.5, c.SpeedOverGroundMs);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var provider = SyntheticOwnShipPositionProvider.CreateManual(
            Start(cog: 90.0, sogMs: 1.0));
        provider.Dispose();
        provider.Dispose(); // must not throw
    }
}
