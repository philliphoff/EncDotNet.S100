using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais.Tests;

/// <summary>
/// In-memory <see cref="IAisMessageSource"/> for tests — drives the
/// dynamic source through scripted scenarios without any wire format.
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
