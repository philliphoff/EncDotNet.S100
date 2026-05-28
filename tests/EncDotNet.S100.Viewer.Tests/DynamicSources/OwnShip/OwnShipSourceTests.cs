using System;
using System.Collections.Generic;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

public sealed class OwnShipSourceTests
{
    private static OwnShipPosition Fix(
        double lat = 50.8, double lon = -1.3,
        double? cog = 90.0, double? sogMs = 5.0)
        => new(lat, lon, cog, sogMs, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Construction_NoFix_HasEmptyCurrentFeatures()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);

        Assert.Empty(src.CurrentFeatures);
    }

    [Fact]
    public void Construction_WhenProviderHasSeed_SnapshotIsPopulated_AndDoesNotRaise()
    {
        var stub = new StubOwnShipPositionProvider();
        stub.Push(Fix());

        var events = new List<DynamicFeaturesChanged>();
        using var src = new OwnShipSource(stub);
        src.Changed += (_, e) => events.Add(e);

        Assert.Single(src.CurrentFeatures);
        Assert.Empty(events);
    }

    [Fact]
    public void FirstUpdate_RaisesAdded_WithOwnShipId()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        DynamicFeaturesChanged? captured = null;
        src.Changed += (_, e) => captured = e;

        stub.Push(Fix());

        Assert.NotNull(captured);
        Assert.Equal(DynamicSourceChangeKind.Added, captured!.Kind);
        Assert.Equal(new[] { OwnShipSource.FeatureId }, captured.ChangedIds);
        Assert.Single(src.CurrentFeatures);
        Assert.Equal(OwnShipSource.FeatureId, src.CurrentFeatures[0].Id);
        Assert.Equal(OwnShipSource.FeatureKind, src.CurrentFeatures[0].Kind);
        Assert.Equal(GeometryType.Point, src.CurrentFeatures[0].GeometryType);
    }

    [Fact]
    public void SecondUpdate_RaisesUpdated()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        stub.Push(Fix());

        var events = new List<DynamicFeaturesChanged>();
        src.Changed += (_, e) => events.Add(e);
        stub.Push(Fix(lat: 50.81));

        Assert.Single(events);
        Assert.Equal(DynamicSourceChangeKind.Updated, events[0].Kind);
        Assert.Equal(new[] { OwnShipSource.FeatureId }, events[0].ChangedIds);
    }

    [Fact]
    public void Projection_PopulatesPositionAndMotion()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        stub.Push(Fix(lat: 50.5, lon: -1.0, cog: 123.4, sogMs: 5.144));

        var f = Assert.Single(src.CurrentFeatures);
        var pt = Assert.Single(f.Coordinates);
        Assert.Equal(50.5, pt.Latitude, 6);
        Assert.Equal(-1.0, pt.Longitude, 6);
        Assert.NotNull(f.Motion);
        Assert.Equal(123.4, f.Motion!.CourseOverGroundDeg);
        Assert.Equal(123.4, f.Motion.HeadingDeg);
        Assert.NotNull(f.Motion.SpeedOverGroundKn);
        Assert.InRange(f.Motion.SpeedOverGroundKn!.Value, 9.99, 10.01);
    }

    [Fact]
    public void Projection_WithoutMotion_LeavesDynamicMotionNull()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        stub.Push(Fix(cog: null, sogMs: null));

        var f = Assert.Single(src.CurrentFeatures);
        Assert.Null(f.Motion);
    }

    [Fact]
    public void Disable_RaisesReset_AndEmptiesCurrentFeatures()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        stub.Push(Fix());

        var events = new List<DynamicFeaturesChanged>();
        src.Changed += (_, e) => events.Add(e);
        src.IsEnabled = false;

        Assert.Empty(src.CurrentFeatures);
        Assert.Single(events);
        Assert.Equal(DynamicSourceChangeKind.Reset, events[0].Kind);
    }

    [Fact]
    public void Reenable_AfterDisable_RaisesAdded_AndRepublishes()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        stub.Push(Fix());
        src.IsEnabled = false;

        var events = new List<DynamicFeaturesChanged>();
        src.Changed += (_, e) => events.Add(e);
        src.IsEnabled = true;

        Assert.Single(events);
        Assert.Equal(DynamicSourceChangeKind.Added, events[0].Kind);
        Assert.Single(src.CurrentFeatures);
    }

    [Fact]
    public void WhileDisabled_ProviderUpdatesAreIgnored()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);
        src.IsEnabled = false;

        var events = new List<DynamicFeaturesChanged>();
        src.Changed += (_, e) => events.Add(e);
        stub.Push(Fix());

        Assert.Empty(events);
        Assert.Empty(src.CurrentFeatures);
    }

    [Fact]
    public void Metadata_HasExpectedShape()
    {
        var stub = new StubOwnShipPositionProvider();
        using var src = new OwnShipSource(stub);

        Assert.False(string.IsNullOrWhiteSpace(src.Metadata.DisplayName));
        Assert.Null(src.Metadata.RendererKey);
        Assert.Equal("ownship", src.Id);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.9438444924406)]
    [InlineData(10.0, 19.438444924406)]
    public void MetresPerSecondToKnots_MatchesMaritimeFactor(double ms, double expectedKn)
    {
        Assert.Equal(expectedKn, ms * OwnShipSource.MetresPerSecondToKnots, 4);
    }
}
