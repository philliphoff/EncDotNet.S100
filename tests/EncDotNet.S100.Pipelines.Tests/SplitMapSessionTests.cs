using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine.Prediction;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Two headless map sessions over one <see cref="DatasetProcessorOwner"/>
/// (split map, #1728).
/// </summary>
public class SplitMapSessionTests
{
    [Fact]
    public async Task BothMapsDrawAProcessorTheOwnerRegistered()
    {
        using var owner = new DatasetProcessorOwner();
        using var mapA = SizedMap();
        using var mapB = SizedMap();
        using var a = Session(mapA, owner);
        using var b = Session(mapB, owner);
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));

        Assert.True(await a.AttachDatasetAsync(Dataset(id), cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(await b.AttachDatasetAsync(Dataset(id), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Single(mapA.DatasetLayers());
        Assert.Single(mapB.DatasetLayers());
        Assert.NotSame(mapA.DatasetLayers()[0], mapB.DatasetLayers()[0]);
    }

    [Fact]
    public async Task RemovingFromOneMapLeavesTheOtherDrawingIt()
    {
        using var owner = new DatasetProcessorOwner();
        using var mapA = SizedMap();
        using var mapB = SizedMap();
        using var a = Session(mapA, owner);
        using var b = Session(mapB, owner);
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor(id.Value);
        owner.TryRegister(id, processor);
        await a.AttachDatasetAsync(Dataset(id), cancellationToken: TestContext.Current.CancellationToken);
        await b.AttachDatasetAsync(Dataset(id), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(a.RemoveDataset(id));

        Assert.Empty(mapA.DatasetLayers());
        Assert.Single(mapB.DatasetLayers());
        Assert.True(owner.Owns(id, processor));
        Assert.Equal(0, processor.DisposeCount);

        // Map B still renders it, then the owner retires it once.
        await b.SetPresentationAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Single(mapB.DatasetLayers());
        Assert.Equal(1, owner.Retire([id], () => b.RemoveDataset(id)));
        Assert.Empty(mapB.DatasetLayers());
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task ASecondMapAttachesToAProcessorTheFirstAdded()
    {
        using var owner = new DatasetProcessorOwner();
        using var mapA = SizedMap();
        using var mapB = SizedMap();
        using var a = Session(mapA, owner);
        using var b = Session(mapB, owner);
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor(id.Value);

        // Adding on the second map fails (the identity is taken), so it
        // attaches instead, and leaves the processor alone when it fails or
        // removes.
        Assert.True(await a.AddDatasetAsync(Dataset(id), processor, cancellationToken: TestContext.Current.CancellationToken));
        using (var other = new StubProcessor(id.Value))
        {
            Assert.False(await b.AddDatasetAsync(Dataset(id), other, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.True(await b.AttachDatasetAsync(Dataset(id), cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await b.AttachDatasetAsync(Dataset(id), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(mapB.DatasetLayers());

        Assert.True(b.RemoveDataset(id));
        Assert.Single(mapA.DatasetLayers());
        Assert.Equal(0, processor.DisposeCount);

        // The map that added it owns it, and retires it on removal.
        Assert.True(a.RemoveDataset(id));
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task AttachingAnUnregisteredDatasetFails()
    {
        using var owner = new DatasetProcessorOwner();
        using var map = SizedMap();
        using var session = Session(map, owner);

        Assert.False(await session.AttachDatasetAsync(Dataset(new MapDatasetId("none")), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(map.DatasetLayers());
    }

    [Fact]
    public async Task AFailedAttachKeepsTheProcessor()
    {
        using var owner = new DatasetProcessorOwner();
        using var map = SizedMap();
        using var session = Session(map, owner);
        var id = new MapDatasetId("cell");
        var renderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new StubProcessor(id.Value)
        {
            RenderStarted = renderStarted,
            Delay = TimeSpan.FromSeconds(30),
        };
        owner.TryRegister(id, processor);
        using var cancel = new CancellationTokenSource();

        var attach = session.AttachDatasetAsync(Dataset(id), cancellationToken: cancel.Token);
        await renderStarted.Task;
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attach);
        Assert.Empty(session.GetDatasets());
        Assert.True(owner.Owns(id, processor));
        Assert.Equal(0, processor.DisposeCount);
    }

    [Fact]
    public void EachMapPredictsOnItsOwn()
    {
        using var owner = new DatasetProcessorOwner();
        using var mapA = SizedMap();
        using var mapB = SizedMap();
        using var a = Session(mapA, owner);
        using var b = Session(mapB, owner);
        var plannerA = a.Navigator.Planner!;
        var plannerB = b.Navigator.Planner!;
        Assert.NotSame(plannerA, plannerB);

        a.Navigator.SetViewportToCenterAndResolution(new MPoint(0, 0), 10);
        b.Navigator.SetViewportToCenterAndResolution(new MPoint(50_000, 50_000), 20);
        var bView = plannerB.Plan().Current;

        // A fling and an animation on map A: map B's view, motion and fling
        // calibration don't move.
        Assert.NotNull(a.Navigator.Fling(4000, 0));
        a.Navigator.CenterOnAndZoomTo(new MPoint(5_000, 2_000), 5, 500);

        Assert.True(plannerA.Motion.HasTarget);
        Assert.Equal(ViewMotionKind.Idle, plannerB.Motion.Kind);
        Assert.Equal(bView, plannerB.Plan().Current);
        Assert.Equal(1.0, plannerB.FlingScale);
        Assert.DoesNotContain(plannerB.Plan().Views, v => v.Source == PredictionSource.Destination);

        // A loader serving both maps sees both maps' demand.
        var set = new DemandPlannerSet(plannerA, plannerB);
        Assert.True(set.HasTarget);
        Assert.Contains(set.CellViews(), v => v.Source == PredictionSource.Destination);
    }

    [Fact]
    public void RetireDisposesAfterTheMapsDetach()
    {
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor(id.Value);
        owner.TryRegister(id, processor);
        bool? ownedDuringDetach = null;
        int? disposedDuringDetach = null;

        Assert.Equal(1, owner.Retire([id, new MapDatasetId("unknown")], () =>
        {
            ownedDuringDetach = owner.Owns(id, processor);
            disposedDuringDetach = processor.DisposeCount;
        }));

        Assert.False(ownedDuringDetach);
        Assert.Equal(0, disposedDuringDetach);
        Assert.Equal(1, processor.DisposeCount);
        Assert.Equal(0, owner.Count);
    }

    private static IS100MapSession Session(Map map, DatasetProcessorOwner owner) =>
        map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new IdentityCrsTransformFactory(),
            ProcessorOwner = owner,
        });

    private static Map SizedMap()
    {
        var map = new Map();
        map.Navigator.SetSize(120, 120);
        return map;
    }

    private static MapDataset Dataset(MapDatasetId id) =>
        new(
            id,
            id.Value,
            new DatasetMetadata
            {
                Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)),
            },
            isVisible: true,
            isActive: true);
}
