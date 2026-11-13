using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The map session draws an <see cref="ILiveDatasetProcessor"/>'s dataset again
/// when the processor raises <see cref="ILiveDatasetProcessor.Changed"/>.
/// </summary>
public sealed class MapsuiLiveDatasetTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_burst_of_changes_renders_the_dataset_once_more()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var (id, processor) = await AddRenderedAsync(session, owner);
        var refreshed = LiveRefreshCompleted(session);

        processor.Version = 2;
        processor.RaiseChanged();
        processor.RaiseChanged();
        processor.RaiseChanged();

        await refreshed.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        // Give a second, unwanted refresh time to show up.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(2, processor.RenderCount);
        Assert.Single(map.DatasetLayers());
    }

    [Fact]
    public async Task A_change_brings_new_times_to_the_timeline()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        var (id, processor) = await AddRenderedAsync(session, owner, [first]);
        var refreshed = LiveRefreshCompleted(session);

        processor.AvailableTimes = [first, first.AddHours(1)];
        processor.RaiseChanged();

        await refreshed.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(2, session.GetDataset(id)!.Dataset.AvailableTimes.Count);
    }

    [Fact]
    public async Task Removing_the_dataset_stops_listening()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var (id, processor) = await AddRenderedAsync(session, owner);
        Assert.Equal(1, processor.ChangedSubscriberCount);

        Assert.True(session.RemoveDataset(id));

        Assert.Equal(0, processor.ChangedSubscriberCount);
    }

    [Fact]
    public async Task A_dataset_never_rendered_is_not_rendered_by_a_change()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("live");
        var processor = new LiveStubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));

        processor.RaiseChanged();
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal(0, processor.RenderCount);
    }

    [Fact]
    public async Task Disposing_the_session_stops_listening()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var session = CreateSession(map, owner);
        var (_, processor) = await AddRenderedAsync(session, owner);

        session.Dispose();

        Assert.Equal(0, processor.ChangedSubscriberCount);
    }

    private static async Task<(MapDatasetId Id, LiveStubProcessor Processor)> AddRenderedAsync(
        MapsuiDatasetLayerSession session,
        DatasetProcessorOwner owner,
        IReadOnlyList<DateTime>? times = null)
    {
        var id = new MapDatasetId("live");
        var processor = new LiveStubProcessor(id.Value) { AvailableTimes = times ?? [] };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Equal(1, processor.RenderCount);
        return (id, processor);
    }

    private static Task LiveRefreshCompleted(MapsuiDatasetLayerSession session)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.DatasetRenderCompleted += (_, e) =>
        {
            if (e.Kind == MapSessionRenderKind.LiveRefresh)
                completed.TrySetResult();
        };
        session.DatasetRenderFailed += (_, e) => completed.TrySetException(e.Exception);
        return completed.Task;
    }

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

    private static MapDataset Dataset(MapDatasetId id) =>
        new(
            id,
            id.Value,
            new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)) },
            isVisible: true,
            isActive: true);

    private sealed class LiveStubProcessor(string datasetId) : StubProcessor(datasetId), ILiveDatasetProcessor
    {
        public event EventHandler<DatasetChangedEventArgs>? Changed;

        public int ChangedSubscriberCount => Changed?.GetInvocationList().Length ?? 0;

        public void RaiseChanged() => Changed?.Invoke(this, DatasetChangedEventArgs.Empty);
    }
}
