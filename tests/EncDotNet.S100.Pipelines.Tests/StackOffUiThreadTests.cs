using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using Mapsui;
using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The chart stack off the UI thread (map rendering slice 6): the incremental
/// overlap index gives the same finer coverages as the O(n²) pass, a landing
/// cell's compose runs on the work scheduler and reaches the map in one
/// marshalled post, landings batch, and a stale stack never overwrites a newer
/// one.
/// </summary>
public sealed class StackOffUiThreadTests
{
    private static readonly GeometryFactory Gf = new();

    // ── CoverageOverlapIndex ──────────────────────────────────────────

    [Fact]
    public void OverlapIndex_MatchesCollectFinerCoverages_AsCellsComeAndGo()
    {
        var random = new Random(1279);
        var index = new CoverageOverlapIndex();
        var cells = new List<OverlapSuppressionCell>();
        var shared = Square(5, 5, 20);
        for (var step = 0; step < 60; step++)
        {
            // Mostly arrivals, some evictions, like a pan.
            if (cells.Count > 0 && random.NextDouble() < 0.3)
                cells.RemoveAt(random.Next(cells.Count));
            else
                cells.Insert(random.Next(cells.Count + 1), RandomCell(random, shared));

            var finer = index.Collect(cells);
            index.Retain(cells.Select(cell => cell.Coverage));
            for (var i = 0; i < cells.Count; i++)
            {
                var expected = OverlapSuppression.CollectFinerCoverages(cells[i], cells);
                var actual = finer[i];
                if (expected is null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.Equal(expected.Count, actual.Length);
                for (var j = 0; j < expected.Count; j++)
                {
                    Assert.Same(expected[j].Coverage, actual[j].Coverage);
                    Assert.Equal(expected[j].CutoffResolution, actual[j].CutoffResolution);
                }
            }
        }
    }

    [Fact]
    public void OverlapIndex_TestsEachPairOnce_AndOnlyTheNewCellsNeighbours()
    {
        // A 6×6 grid of touching cells, alternating bands.
        var cells = new List<OverlapSuppressionCell>();
        for (var x = 0; x < 6; x++)
        {
            for (var y = 0; y < 6; y++)
                cells.Add(Cell(Square(x * 10, y * 10, 10), (x + y) % 2 == 0 ? 90_000 : 22_000));
        }

        var index = new CoverageOverlapIndex();
        index.Collect(cells);
        var first = index.IntersectionTests;
        Assert.True(first < cells.Count * (cells.Count - 1) / 2, "the R-tree prunes far pairs");

        // Nothing new: no NTS test at all.
        index.Collect(cells);
        Assert.Equal(first, index.IntersectionTests);

        // A new harbour cell over four grid cells: tested against those only.
        cells.Add(Cell(Square(15, 15, 10), 8_000));
        index.Collect(cells);
        Assert.InRange(index.IntersectionTests - first, 1, 9);
    }

    [Fact]
    public void OverlapIndex_Retain_DropsEvictedCoverages()
    {
        var a = Cell(Square(0, 0, 10), 90_000);
        var b = Cell(Square(5, 5, 10), 22_000);
        var index = new CoverageOverlapIndex();
        index.Collect([a, b]);
        Assert.Equal(2, index.Count);

        index.Retain([a.Coverage]);

        Assert.Equal(1, index.Count);
        Assert.Null(index.Collect([a])[0]);
    }

    // ── Session: compose off the caller, apply in one post ────────────

    [Fact]
    public async Task ALandingCell_ComposesOnTheScheduler_AndReachesTheMapInOnePost()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var marshal = new QueuedMarshal();
        using var session = CreateSession(map, owner, marshal.Post);
        var id = Register(owner, session, "cell", 22_000);
        var changes = 0;
        int? changedOn = null;
        session.LayersChanged += (_, _) =>
        {
            changes++;
            changedOn = Environment.CurrentManagedThreadId;
        };

        var render = session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await marshal.WaitForPostsAsync(1);

        // Composed, not applied: the map and the stack still wait for the post.
        Assert.False(render.IsCompleted);
        Assert.Empty(session.GetStackedLayers());
        Assert.Empty(map.DatasetLayers());
        Assert.Equal(0, changes);

        // The post runs on this thread; read its id before awaiting, since the
        // continuation after `await render` may resume on another thread.
        var postThread = Environment.CurrentManagedThreadId;
        Assert.Equal(1, marshal.RunPending());
        Assert.NotNull(await render);
        Assert.Single(map.DatasetLayers());
        Assert.Single(session.GetStackedLayers());
        Assert.Equal(1, changes);
        Assert.Equal(postThread, changedOn);
        Assert.Equal(1, session.StackComputeCount);
    }

    [Fact]
    public async Task CellsLandingWhileAStackIsPending_JoinOneCompose()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var marshal = new QueuedMarshal();
        using var session = CreateSession(map, owner, marshal.Post);
        var a = Register(owner, session, "a", 90_000);
        var b = Register(owner, session, "b", 22_000);
        var c = Register(owner, session, "c", 8_000);
        var changes = 0;
        session.LayersChanged += (_, _) => changes++;

        var renderA = session.RenderAsync(a, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await marshal.WaitForPostsAsync(1);
        var renderB = session.RenderAsync(b, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var renderC = session.RenderAsync(c, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => session.StackRequestedVersion == 3);

        // The job waits for its apply before it captures again.
        Assert.Equal(1, marshal.Pending);
        Assert.Equal(1, session.StackComputeCount);

        marshal.RunPending();
        await renderA;
        Assert.False(renderB.IsCompleted);
        Assert.False(renderC.IsCompleted);
        Assert.Single(session.GetStackedLayers());

        await marshal.WaitForPostsAsync(1);
        Assert.Equal(1, marshal.RunPending());
        await Task.WhenAll(renderB, renderC);

        Assert.Equal(2, session.StackComputeCount);
        Assert.Equal(2, changes);
        Assert.Equal(3, session.GetStackedLayers().Count);
        Assert.Equal(3, session.StackAppliedVersion);
    }

    [Fact]
    public async Task AStackOlderThanASynchronousCompose_IsDropped()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var marshal = new QueuedMarshal();
        using var session = CreateSession(map, owner, marshal.Post);
        var a = Register(owner, session, "a", 90_000);
        var b = Register(owner, session, "b", 22_000);

        var render = session.RenderAsync(a, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await marshal.WaitForPostsAsync(1);

        // A synchronous compose (a reorder) lands first, and already includes
        // the rendered cell, so the render completes without the post.
        session.SetOrder([b, a]);
        await render;
        var layers = session.GetStackedLayers();
        Assert.Single(layers);
        var applied = session.StackAppliedVersion;
        var changes = 0;
        session.LayersChanged += (_, _) => changes++;

        marshal.RunPending();

        Assert.Equal(applied, session.StackAppliedVersion);
        Assert.Equal(layers, session.GetStackedLayers());
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task AFailedBackgroundCompose_RestoresTheCellAndFailsItsRender()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var authority = new SwitchableAuthority();
        using var session = new MapsuiDatasetLayerSession(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(authority),
            redrawRequested: null,
            stackMarshal: static action => action());
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var original = Assert.Single(map.DatasetLayers());

        authority.Throw = true;
        processor.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken));

        Assert.Same(original, Assert.Single(map.DatasetLayers()));
        Assert.Same(original, Assert.Single(session.GetDataset(id)!.Layers));

        // The next landing composes again once the cause is gone.
        authority.Throw = false;
        processor.Version = 3;
        await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.NotSame(original, Assert.Single(map.DatasetLayers()));
    }

    [Fact]
    public async Task OffThreadStack_MatchesTheSynchronousStack()
    {
        // Overlapping cells of three bands, one hidden: the same stack order,
        // display state, scale windows and suppression clips either way.
        var cells = new (string Name, int Scale, double West, bool Visible)[]
        {
            ("overview", 1_500_000, 0, true),
            ("general", 350_000, 0.5, true),
            ("coastal", 90_000, 1, true),
            ("approach", 22_000, 1.2, true),
            ("hidden", 8_000, 1.4, false),
            ("harbour", 8_000, 1.6, true),
        };

        using var syncMap = new Map();
        using var syncOwner = new DatasetProcessorOwner();
        using var sync = CreateSession(syncMap, syncOwner, stackMarshal: null);
        using var asyncMap = new Map();
        using var asyncOwner = new DatasetProcessorOwner();
        using var offThread = CreateSession(asyncMap, asyncOwner, static action => action());

        foreach (var (session, owner) in new[] { (sync, syncOwner), (offThread, asyncOwner) })
        {
            var ids = cells.Select(cell => Register(owner, session, cell.Name, cell.Scale, cell.West)).ToArray();
            await Task.WhenAll(ids.Select(id =>
                session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken)));
            for (var i = 0; i < cells.Length; i++)
            {
                if (!cells[i].Visible)
                    session.SetDataset(Dataset(ids[i], isVisible: false));
            }
        }

        var expected = sync.GetStackedLayers();
        var actual = offThread.GetStackedLayers();
        Assert.Equal(expected.Select(layer => layer.Name), actual.Select(layer => layer.Name));
        Assert.Equal(syncMap.Layers.Select(layer => layer.Name), asyncMap.Layers.Select(layer => layer.Name));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Enabled, actual[i].Enabled);
            Assert.Equal(expected[i].Opacity, actual[i].Opacity);
            Assert.Equal(((BaseLayer)expected[i]).MaxVisible, ((BaseLayer)actual[i]).MaxVisible);
            var expectedClip = CoverageClip.Get(expected[i]);
            var actualClip = CoverageClip.Get(actual[i]);
            Assert.Equal(expectedClip is null, actualClip is null);
            if (expectedClip is null)
                continue;
            Assert.Equal(expectedClip.Length, actualClip!.Length);
            for (var j = 0; j < expectedClip.Length; j++)
            {
                Assert.True(expectedClip[j].Coverage.EqualsExact(actualClip[j].Coverage));
                Assert.Equal(expectedClip[j].CutoffResolution, actualClip[j].CutoffResolution);
            }
        }

        Assert.Contains(expected, layer => CoverageClip.Get(layer) is not null);
    }

    [Fact]
    public async Task AnUnchangedClip_KeepsItsAttachedSet()
    {
        // The prepared hidden region is cached per attached set, so a compose
        // that doesn't change a cell's finer coverages must not replace it.
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner, static action => action());
        var coarse = Register(owner, session, "coarse", 90_000);
        var fine = Register(owner, session, "fine", 22_000, west: 0.5);
        await session.RenderAsync(coarse, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await session.RenderAsync(fine, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var coarseLayer = Assert.Single(session.GetDataset(coarse)!.Layers);
        var attached = CoverageClip.Get(coarseLayer);
        Assert.NotNull(attached);

        var other = Register(owner, session, "far", 22_000, west: 40);
        await session.RenderAsync(other, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.Same(attached, CoverageClip.Get(coarseLayer));
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static MapDatasetId Register(
        DatasetProcessorOwner owner,
        MapsuiDatasetLayerSession session,
        string name,
        int scale,
        double west = 0)
    {
        var id = new MapDatasetId(name);
        Assert.True(owner.TryRegister(id, new StubProcessor(name)
        {
            CellMinimumDisplayScale = scale,
            CoverageAreas = [Area(west, 0, 2)],
        }));
        session.SetDataset(Dataset(id));
        return id;
    }

    private static CoverageArea Area(double west, double south, double size) => new()
    {
        ExteriorRing =
        [
            new GeoPosition(south, west),
            new GeoPosition(south, west + size),
            new GeoPosition(south + size, west + size),
            new GeoPosition(south + size, west),
            new GeoPosition(south, west),
        ],
    };

    private static MapsuiDatasetLayerSession CreateSession(
        Map map,
        DatasetProcessorOwner owner,
        Action<Action>? stackMarshal) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()),
            redrawRequested: null,
            stackMarshal: stackMarshal);

    private static MapDataset Dataset(MapDatasetId id, bool isVisible = true) =>
        new(
            id,
            id.Value,
            new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)) },
            isVisible,
            isActive: true);

    private static OverlapSuppressionCell RandomCell(Random random, Polygon shared)
    {
        var coverage = random.NextDouble() switch
        {
            < 0.08 => null,
            < 0.16 => shared,
            _ => Square(random.Next(0, 60), random.Next(0, 60), random.Next(3, 30)),
        };
        int? scale = random.NextDouble() < 0.08 ? null : new[] { 8_000, 22_000, 22_000, 90_000, 350_000 }[random.Next(5)];
        int? tieBreak = random.NextDouble() < 0.5 ? null : new[] { 3_500, 4_000, 12_000 }[random.Next(3)];
        return new OverlapSuppressionCell
        {
            Layers = [new MemoryLayer()],
            Coverage = coverage,
            Rank = scale is int s ? new CellRank(s, tieBreak) : null,
            CutoffScaleDenominator = random.NextDouble() < 0.5 ? null : random.Next(10_000, 2_000_000),
        };
    }

    private static OverlapSuppressionCell Cell(Geometry coverage, int scale) => new()
    {
        Layers = [new MemoryLayer()],
        Coverage = coverage,
        Rank = new CellRank(scale),
    };

    private static Polygon Square(double minX, double minY, double size) =>
        Gf.CreatePolygon(
        [
            new Coordinate(minX, minY),
            new Coordinate(minX + size, minY),
            new Coordinate(minX + size, minY + size),
            new Coordinate(minX, minY + size),
            new Coordinate(minX, minY),
        ]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private sealed class SwitchableAuthority : IInteroperabilityAuthority
    {
        private readonly InteroperabilityAuthority _inner = new();

        private volatile bool _throw;

        public bool Throw
        {
            get => _throw;
            set => _throw = value;
        }

        public S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null) =>
            _inner.GetDefaultPlane(productSpec, featureTypeOrLayerKind);

        public IReadOnlyList<SubLayerStackItem> Sort(IEnumerable<SubLayerStackItem> entries) =>
            Throw ? throw new InvalidOperationException("Projection failed.") : _inner.Sort(entries);

        public IReadOnlyList<SubLayerStackItem> ApplyRules(
            IReadOnlyList<SubLayerStackItem> sortedStack,
            IReadOnlyList<LoadedDatasetInfo> loadedDatasets,
            MarinerSettings? mariner = null,
            IReadOnlyCollection<S98InteroperabilityRule>? rules = null) =>
            _inner.ApplyRules(sortedStack, loadedDatasets, mariner, rules);
    }

    /// <summary>A map thread the test pumps by hand.</summary>
    private sealed class QueuedMarshal
    {
        private readonly ConcurrentQueue<Action> _posts = new();

        public int Pending => _posts.Count;

        public void Post(Action action) => _posts.Enqueue(action);

        public Task WaitForPostsAsync(int count) => WaitUntilAsync(() => _posts.Count >= count);

        public int RunPending()
        {
            var ran = 0;
            while (_posts.TryDequeue(out var action))
            {
                action();
                ran++;
            }

            return ran;
        }
    }
}
