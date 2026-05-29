using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.DynamicSources.Ais.Tests;

public class AisDynamicFeatureSourceTests
{
    private static AisPositionReport Pos(uint mmsi, double lat, double lon,
        DateTimeOffset? timestamp = null,
        double? cog = null, double? hdg = null, double? sog = null,
        AisNavigationStatus? nav = null) =>
        new()
        {
            Mmsi = mmsi,
            Timestamp = timestamp ?? DateTimeOffset.UnixEpoch,
            Latitude = lat,
            Longitude = lon,
            CourseOverGroundDeg = cog,
            HeadingDeg = hdg,
            SpeedOverGroundKn = sog,
            NavigationStatus = nav,
        };

    private static AisStaticVoyageData Static(uint mmsi,
        AisShipType shipType = AisShipType.Cargo,
        AisDimensions? dims = null,
        string? name = null) =>
        new()
        {
            Mmsi = mmsi,
            Timestamp = DateTimeOffset.UnixEpoch,
            ShipType = shipType,
            ShipTypeClass = AisShipTypeClassExtensions.ToClass(shipType),
            Dimensions = dims,
            VesselName = name,
        };

    [Fact]
    public void Initial_state_is_empty_with_renderer_key_set()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();

        Assert.Equal("ais", source.Inner.Id);
        Assert.Equal("vessel.ais", source.Inner.Metadata.RendererKey);
        Assert.Empty(source.Inner.CurrentFeatures);
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public void First_position_report_is_added()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();
        var changes = new List<DynamicFeaturesChanged>();
        source.Inner.Changed += (_, e) => changes.Add(e);

        fake.Subscriptions[0].EmitPosition(Pos(123, 47.6, -122.3, DateTimeOffset.UnixEpoch, cog: 90, sog: 12));

        var feature = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Equal("ais:123", feature.Id);
        Assert.Equal("vessel.ais.unknown", feature.Kind);
        Assert.Equal(GeometryType.Point, feature.GeometryType);
        Assert.Equal(47.6, feature.Coordinates[0].Latitude);
        Assert.Equal(-122.3, feature.Coordinates[0].Longitude);
        Assert.Equal(90, feature.Motion?.CourseOverGroundDeg);
        Assert.Equal(12, feature.Motion?.SpeedOverGroundKn);
        Assert.Null(feature.VesselGeometry);
        Assert.Equal(123u, feature.Attributes["mmsi"]);
        var change = Assert.Single(changes);
        Assert.Equal(DynamicSourceChangeKind.Added, change.Kind);
    }

    [Fact]
    public void Static_voyage_data_merges_into_subsequent_position()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();

        var dims = new AisDimensions
        {
            LengthMetres = 200,
            BeamMetres = 30,
            BowOffsetMetres = 50,
            PortOffsetMetres = 15,
        };
        fake.Subscriptions[0].EmitStatic(Static(7, AisShipType.Tanker, dims, "TANKER ONE"));
        fake.Subscriptions[0].EmitPosition(Pos(7, 0, 0));

        var feature = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Equal("vessel.ais.tanker", feature.Kind);
        Assert.NotNull(feature.VesselGeometry);
        Assert.Equal(200, feature.VesselGeometry!.LengthMetres);
        Assert.Equal(30, feature.VesselGeometry.BeamMetres);
        Assert.Equal(50, feature.VesselGeometry.BowOffsetMetres);
        Assert.Equal(15, feature.VesselGeometry.PortOffsetMetres);
        Assert.Equal("TANKER ONE", feature.Attributes["vesselName"]);
    }

    [Fact]
    public void Late_static_data_re_projects_existing_feature()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();

        fake.Subscriptions[0].EmitPosition(Pos(42, 1, 2, DateTimeOffset.UnixEpoch, cog: 45, hdg: 50, sog: 5));
        var before = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Equal("vessel.ais.unknown", before.Kind);
        Assert.Null(before.VesselGeometry);

        var dims = new AisDimensions
        {
            LengthMetres = 50, BeamMetres = 10,
            BowOffsetMetres = 25, PortOffsetMetres = 5,
        };
        fake.Subscriptions[0].EmitStatic(Static(42, AisShipType.Cargo, dims, "CARGO 42"));

        var after = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Equal("vessel.ais.cargo", after.Kind);
        Assert.Equal("CARGO 42", after.Attributes["vesselName"]);
        Assert.NotNull(after.VesselGeometry);
        // Position / motion preserved across re-projection.
        Assert.Equal(1, after.Coordinates[0].Latitude);
        Assert.Equal(45, after.Motion?.CourseOverGroundDeg);
        Assert.Equal(50, after.Motion?.HeadingDeg);
        Assert.Equal(5, after.Motion?.SpeedOverGroundKn);
    }

    [Fact]
    public void Static_data_with_no_position_is_cached_silently()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();
        var changes = new List<DynamicFeaturesChanged>();
        source.Inner.Changed += (_, e) => changes.Add(e);

        fake.Subscriptions[0].EmitStatic(Static(99, AisShipType.PilotVessel));

        Assert.Empty(source.Inner.CurrentFeatures);
        Assert.Empty(changes);
    }

    [Fact]
    public void Sentinel_collapse_is_a_driver_concern_so_nulls_round_trip_to_motion()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();

        fake.Subscriptions[0].EmitPosition(Pos(1, 0, 0,
            cog: null, hdg: null, sog: null));

        var feature = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Null(feature.Motion?.CourseOverGroundDeg);
        Assert.Null(feature.Motion?.HeadingDeg);
        Assert.Null(feature.Motion?.SpeedOverGroundKn);
    }

    [Fact]
    public void Sweep_removes_targets_older_than_retention_window()
    {
        var fake = new FakeAisMessageSource();
        var retain = TimeSpan.FromMinutes(2);
        using var source = new AisDynamicFeatureSource("ais", fake, retain: retain).AsSyncDisposable();

        var t0 = DateTimeOffset.UnixEpoch;
        fake.Subscriptions[0].EmitPosition(Pos(1, 0, 0, t0));
        fake.Subscriptions[0].EmitPosition(Pos(2, 0, 0, t0 + TimeSpan.FromMinutes(1)));
        Assert.Equal(2, source.Inner.CurrentFeatures.Count);

        var changes = new List<DynamicFeaturesChanged>();
        source.Inner.Changed += (_, e) => changes.Add(e);
        source.Inner.Sweep(t0 + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));

        // MMSI 1 (age 2:30) is past retain (2 min); MMSI 2 (age 1:30) is not.
        var feature = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Equal("ais:2", feature.Id);
        var change = Assert.Single(changes);
        Assert.Equal(DynamicSourceChangeKind.Removed, change.Kind);
        Assert.Contains("ais:1", change.ChangedIds);
    }

    [Fact]
    public void Default_retention_window_is_six_minutes()
        => Assert.Equal(TimeSpan.FromMinutes(6), AisDynamicFeatureSource.DefaultRetainWindow);

    [Fact]
    public void Target_lost_event_removes_feature_and_static_cache()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();
        fake.Subscriptions[0].EmitStatic(Static(5, AisShipType.Cargo,
            new AisDimensions { LengthMetres = 1, BeamMetres = 1, BowOffsetMetres = 0, PortOffsetMetres = 0 }));
        fake.Subscriptions[0].EmitPosition(Pos(5, 0, 0));
        Assert.Single(source.Inner.CurrentFeatures);

        fake.Subscriptions[0].EmitTargetLost(new AisTargetLost
        {
            Mmsi = 5,
            Timestamp = DateTimeOffset.UnixEpoch,
        });

        Assert.Empty(source.Inner.CurrentFeatures);

        // After loss, a fresh position with no static yet should map to unknown class.
        fake.Subscriptions[0].EmitPosition(Pos(5, 1, 1));
        var feature = Assert.Single(source.Inner.CurrentFeatures);
        Assert.Equal("vessel.ais.unknown", feature.Kind);
    }

    [Fact]
    public void UpdateArea_delegates_to_subscription()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();

        var bbox = new BoundingBox(40, -120, 50, -110);
        Assert.True(source.Inner.UpdateArea(bbox));
        var update = Assert.Single(fake.Subscriptions[0].AreaUpdates);
        Assert.Same(bbox, update);
    }

    [Fact]
    public void UpdateArea_propagates_driver_inability()
    {
        var fake = new FakeAisMessageSource();
        using var source = new AisDynamicFeatureSource("ais", fake).AsSyncDisposable();
        fake.Subscriptions[0].SupportsAreaUpdate = false;

        Assert.False(source.Inner.UpdateArea(new BoundingBox(1, 2, 3, 4)));
    }

    [Fact]
    public async Task DisposeAsync_disposes_subscription_and_blocks_further_use()
    {
        var fake = new FakeAisMessageSource();
        var source = new AisDynamicFeatureSource("ais", fake);
        await source.DisposeAsync();

        Assert.True(fake.Subscriptions[0].Disposed);
        Assert.Throws<ObjectDisposedException>(() => source.UpdateArea(null));
    }

    [Fact]
    public void Constructor_rejects_null_or_empty_id()
    {
        var fake = new FakeAisMessageSource();
        Assert.Throws<ArgumentNullException>(() => new AisDynamicFeatureSource(null!, fake));
        Assert.Throws<ArgumentException>(() => new AisDynamicFeatureSource("", fake));
    }

    [Fact]
    public void FeatureIdForMmsi_is_stable()
        => Assert.Equal("ais:123456789", AisDynamicFeatureSource.FeatureIdForMmsi(123456789));
}

internal static class AsyncDisposableExtensions
{
    /// <summary>
    /// Wraps an <see cref="IAsyncDisposable"/> so that test
    /// <c>using</c> blocks dispose synchronously without polluting
    /// every test signature with <c>async</c>.
    /// </summary>
    public static SyncWrapper<T> AsSyncDisposable<T>(this T inner) where T : IAsyncDisposable
        => new(inner);
}

internal readonly struct SyncWrapper<T>(T inner) : IDisposable where T : IAsyncDisposable
{
    public T Inner { get; } = inner;

    public void Dispose() => Inner.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
