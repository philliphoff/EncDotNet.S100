using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public class DeferredAisFeatureSourceTests
{
    private const double Threshold = 50.0;

    [Fact]
    public void Stays_inactive_when_viewport_above_threshold()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        notifier.Publish(SnapshotWithSpan(60, 60));

        Assert.False(src.Decorator.IsActivated);
        Assert.Empty(src.Decorator.CurrentFeatures);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Stays_inactive_when_only_one_span_meets_threshold()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        // Lat span 5° satisfies; lon span 360° (global) does not.
        notifier.Publish(SnapshotWithSpan(5, 360));

        Assert.False(src.Decorator.IsActivated);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Activates_when_both_spans_at_or_below_threshold()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        notifier.Publish(SnapshotWithSpan(10, 10));

        Assert.True(src.Decorator.IsActivated);
        Assert.Equal(1, spy.Calls);
        Assert.NotNull(src.Decorator.ActivationBoundingBox);
        var seed = src.Decorator.ActivationBoundingBox!;
        Assert.Equal(-5.0, seed.SouthLatitude, 6);
        Assert.Equal(5.0, seed.NorthLatitude, 6);
        Assert.Equal(-5.0, seed.WestLongitude, 6);
        Assert.Equal(5.0, seed.EastLongitude, 6);
    }

    [Fact]
    public void Activates_at_exact_threshold()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        notifier.Publish(SnapshotWithSpan(Threshold, Threshold));

        Assert.True(src.Decorator.IsActivated);
    }

    [Fact]
    public void Activates_only_once_even_after_repeated_qualifying_viewports()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        notifier.Publish(SnapshotWithSpan(10, 10));
        notifier.Publish(SnapshotWithSpan(8, 8));
        notifier.Publish(SnapshotWithSpan(60, 60)); // zoomed back out
        notifier.Publish(SnapshotWithSpan(5, 5));   // back in

        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public void Forwards_inner_changed_events_after_activation()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        var raised = new List<DynamicFeaturesChanged>();
        src.Decorator.Changed += (_, e) => raised.Add(e);

        notifier.Publish(SnapshotWithSpan(10, 10));
        Assert.Single(raised); // initial Reset on activation.

        // Drive the inner source's underlying message stream.
        spy.LastMessageSource!.Subscriptions[^1].EmitPosition(new AisPositionReport
        {
            Mmsi = 123456789,
            Timestamp = DateTimeOffset.UtcNow,
            Latitude = 0,
            Longitude = 0,
        });

        Assert.True(raised.Count >= 2);
        Assert.Equal(DynamicSourceChangeKind.Added, raised[^1].Kind);
    }

    [Fact]
    public void Calls_UpdateArea_on_subsequent_viewport_changes_after_activation()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        // debounce = Zero: synchronous UpdateArea propagation for tests.
        using var src = new TestableDeferredSource(spy.Build, notifier, debounce: TimeSpan.Zero);

        notifier.Publish(SnapshotWithSpan(10, 10));
        var fakeSub = spy.LastMessageSource!.Subscriptions[^1];

        Assert.Empty(fakeSub.AreaUpdates); // seed went via constructor, not UpdateArea.

        notifier.Publish(SnapshotWithSpan(4, 4));
        notifier.Publish(SnapshotWithSpan(2, 2));

        Assert.Equal(2, fakeSub.AreaUpdates.Count);
        var last = fakeSub.AreaUpdates[^1]!;
        Assert.Equal(-1.0, last.SouthLatitude, 6);
        Assert.Equal(1.0, last.NorthLatitude, 6);
    }

    [Fact]
    public async Task Debounces_UpdateArea_to_a_single_call()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier, debounce: TimeSpan.FromMilliseconds(40));

        notifier.Publish(SnapshotWithSpan(10, 10));
        var fakeSub = spy.LastMessageSource!.Subscriptions[^1];

        // Burst a handful of viewport changes faster than the debounce.
        for (int i = 0; i < 5; i++)
        {
            notifier.Publish(SnapshotWithSpan(8 - i * 0.5, 8 - i * 0.5));
        }

        // Wait long enough for the trailing call to fire.
        await Task.Delay(200);

        Assert.Single(fakeSub.AreaUpdates);
    }

    [Fact]
    public async Task Dispose_disposes_inner_source()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        var src = new TestableDeferredSource(spy.Build, notifier, debounce: TimeSpan.Zero);

        notifier.Publish(SnapshotWithSpan(10, 10));
        var fakeSub = spy.LastMessageSource!.Subscriptions[^1];

        await src.Decorator.DisposeAsync();
        Assert.True(fakeSub.Disposed);
    }

    [Fact]
    public async Task Dispose_when_never_activated_is_a_noop()
    {
        var notifier = new FakeMapViewportNotifier();
        var spy = new SpyFactory();
        var src = new TestableDeferredSource(spy.Build, notifier);

        await src.Decorator.DisposeAsync();
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Reads_initial_snapshot_from_notifier_at_construction()
    {
        var notifier = new FakeMapViewportNotifier();
        notifier.Publish(SnapshotWithSpan(10, 10)); // populate Current first.

        var spy = new SpyFactory();
        using var src = new TestableDeferredSource(spy.Build, notifier);

        // Without subscribing-to-event-then-publish, the gate should
        // still trip because the decorator reads notifier.Current in
        // its constructor.
        Assert.True(src.Decorator.IsActivated);
    }

    [Fact]
    public void Throws_on_non_positive_threshold()
    {
        var notifier = new FakeMapViewportNotifier();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeferredAisFeatureSource(
                id: "ais",
                activationSpanDegrees: 0,
                factory: _ => throw new InvalidOperationException(),
                notifier: notifier));
    }

    private static MapViewportSnapshot SnapshotWithSpan(double latSpan, double lonSpan) =>
        new()
        {
            MinLatitude = -latSpan / 2,
            MaxLatitude = latSpan / 2,
            MinLongitude = -lonSpan / 2,
            MaxLongitude = lonSpan / 2,
        };

    /// <summary>Fake viewport publisher.</summary>
    private sealed class FakeMapViewportNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }
        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(MapViewportSnapshot snapshot)
        {
            Current = snapshot;
            ViewportChanged?.Invoke(this, snapshot);
        }
    }

    /// <summary>
    /// Records every factory invocation and stashes the message
    /// source backing the latest <see cref="AisDynamicFeatureSource"/>
    /// so tests can drive position reports through the fake driver.
    /// </summary>
    private sealed class SpyFactory
    {
        public int Calls { get; private set; }
        public BoundingBox? LastBbox { get; private set; }
        public FakeAisMessageSource? LastMessageSource { get; private set; }

        public AisDynamicFeatureSource Build(BoundingBox bbox)
        {
            Calls++;
            LastBbox = bbox;
            LastMessageSource = new FakeAisMessageSource();
            return new AisDynamicFeatureSource(
                id: "ais",
                messageSource: LastMessageSource,
                request: new AisSubscriptionRequest { Area = bbox });
        }
    }

    /// <summary>
    /// Disposable wrapper that owns a <see cref="DeferredAisFeatureSource"/>.
    /// </summary>
    private sealed class TestableDeferredSource : IDisposable
    {
        public DeferredAisFeatureSource Decorator { get; }

        public TestableDeferredSource(
            Func<BoundingBox, AisDynamicFeatureSource> factory,
            IMapViewportNotifier notifier,
            TimeSpan? debounce = null)
        {
            Decorator = new DeferredAisFeatureSource(
                id: "ais",
                activationSpanDegrees: Threshold,
                factory: factory,
                notifier: notifier,
                debounce: debounce);
        }

        public void Dispose() => Decorator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// In-memory <see cref="IAisMessageSource"/> for these tests. We
/// duplicate this from the AIS test project (where the equivalent is
/// <c>internal</c>) so we don't have to expose internals across
/// assemblies just for this PR.
/// </summary>
internal sealed class FakeAisMessageSource : IAisMessageSource
{
    public AisSourceMetadata Metadata { get; } = new()
    {
        DisplayName = "Fake AIS source",
        Description = "Test fake.",
    };

    public List<FakeAisSubscription> Subscriptions { get; } = new();

    public IAisSubscription Subscribe(AisSubscriptionRequest request)
    {
        var sub = new FakeAisSubscription(request);
        Subscriptions.Add(sub);
        return sub;
    }
}

internal sealed class FakeAisSubscription : IAisSubscription
{
    private AisSubscriptionRequest _active;

    public FakeAisSubscription(AisSubscriptionRequest request)
    {
        _active = request;
    }

    public AisSubscriptionRequest ActiveRequest => _active;
    public bool Disposed { get; private set; }
    public bool SupportsAreaUpdate { get; set; } = true;
    public List<BoundingBox?> AreaUpdates { get; } = new();

    public event EventHandler<AisPositionReport>? PositionReportReceived;
    public event EventHandler<AisStaticVoyageData>? StaticVoyageDataReceived;
    public event EventHandler<AisTargetLost>? TargetLost;

    public bool TryUpdateArea(BoundingBox? area)
    {
        if (!SupportsAreaUpdate) return false;
        AreaUpdates.Add(area);
        _active = _active with { Area = area };
        return true;
    }

    public void EmitPosition(AisPositionReport report)
        => PositionReportReceived?.Invoke(this, report);

    public void EmitStatic(AisStaticVoyageData data)
        => StaticVoyageDataReceived?.Invoke(this, data);

    public void EmitTargetLost(AisTargetLost lost)
        => TargetLost?.Invoke(this, lost);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
