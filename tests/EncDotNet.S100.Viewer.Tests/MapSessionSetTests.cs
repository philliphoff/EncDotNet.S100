using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Viewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="MapSessionSet"/> (split map, #1731): the loader's session calls
/// reach every map's session, a map added later catches up, and a removed
/// map's session is released.
/// </summary>
public sealed class MapSessionSetTests
{
    private static readonly MapPresentationState Presentation = MapPresentationState.Default;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MapDataset Dataset(string id) => new(
        new MapDatasetId(id),
        id,
        new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)) });

    private static MapViewportSnapshot Viewport(double south, double west) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = south + 1,
        MaxLongitude = west + 1,
        MercatorResolution = 5,
    };

    private static async Task<(MapSessionSet Set, FakeMapDatasetSession First, FakeMapDatasetSession Second)> TwoAsync(
        IMapViewportNotifier? firstViewport = null,
        IMapViewportNotifier? secondViewport = null)
    {
        var set = new MapSessionSet(NullLogger.Instance);
        var first = new FakeMapDatasetSession();
        var second = new FakeMapDatasetSession();
        await set.AddAsync(new MapViewId("map-1"), first, firstViewport, Presentation, Ct);
        await set.AddAsync(new MapViewId("map-2"), second, secondViewport, Presentation, Ct);
        return (set, first, second);
    }

    [Fact]
    public async Task A_load_reaches_both_sessions_and_returns_the_primary_result()
    {
        var (set, first, second) = await TwoAsync();

        set.SetDataset(Dataset("a"), 1_000_000, 22_000);
        var result = await set.RenderAsync(new MapDatasetId("a"), Presentation, Ct);

        Assert.NotNull(result);
        Assert.Contains(new MapDatasetId("a"), first.Drawn);
        Assert.Contains(new MapDatasetId("a"), second.Drawn);
        Assert.Same(first, set.Primary);
    }

    [Fact]
    public async Task A_stored_scene_gives_each_other_session_its_own_copy()
    {
        var (set, first, second) = await TwoAsync();
        var id = new MapDatasetId("a");
        set.SetDataset(Dataset("a"), null, null);
        var copies = 0;

        var published = await set.PublishStoredAsync(
            id,
            FakeMapDatasetSession.Result(id),
            _ =>
            {
                copies++;
                return Task.FromResult(FakeMapDatasetSession.Result(id));
            },
            Ct);

        Assert.True(published);
        Assert.Equal(1, copies);
        Assert.Contains(id, first.Drawn);
        Assert.Contains(id, second.Drawn);
    }

    [Fact]
    public async Task Refreshes_reach_both_sessions_each_at_its_own_viewport()
    {
        var north = new MapViewsTests.FakeNotifier();
        var south = new MapViewsTests.FakeNotifier();
        north.Publish(Viewport(50, 0));
        south.Publish(Viewport(10, 0));
        var (set, first, second) = await TwoAsync(north, south);

        Assert.True(await set.RefreshAsync(Presentation, Ct));
        await set.RefreshTimeAsync(Presentation, Ct);
        Assert.True(await set.RefreshDepthAdjustedAsync(Presentation, Ct));
        await set.RefreshRevealedAsync(fallback: null);

        foreach (var session in new[] { first, second })
        {
            Assert.Contains("Refresh", session.Calls);
            Assert.Contains("RefreshTime", session.Calls);
            Assert.Contains("RefreshDepthAdjusted", session.Calls);
            Assert.Contains("RefreshRevealed", session.Calls);
        }
        Assert.Equal(MapSessionSet.ToMercatorViewport(Viewport(50, 0)), first.LastRefreshViewport);
        Assert.Equal(MapSessionSet.ToMercatorViewport(Viewport(10, 0)), second.LastRefreshViewport);
    }

    [Fact]
    public async Task Time_mariner_settings_and_order_reach_both_sessions()
    {
        var (set, first, second) = await TwoAsync();
        set.SetDataset(Dataset("a"), null, null);
        set.SetDataset(Dataset("b"), null, null);
        var time = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var mariner = MarinerSettings.Default with { SafetyContour = Depth.FromMetres(12) };

        set.SetCurrentTime(time);
        set.SetMarinerSettings(mariner);
        set.SetOrder([new MapDatasetId("b"), new MapDatasetId("a")]);

        foreach (var session in new[] { first, second })
        {
            Assert.Equal(time, session.CurrentTime);
            Assert.Same(mariner, session.Mariner);
            Assert.Equal([new MapDatasetId("b"), new MapDatasetId("a")], session.Order);
        }
    }

    [Fact]
    public async Task Removal_reaches_both_sessions_and_never_retires_the_processor()
    {
        var (set, first, second) = await TwoAsync();
        set.SetDataset(Dataset("a"), null, null);
        set.SetDataset(Dataset("b"), null, null);
        await set.RenderAsync(new MapDatasetId("a"), Presentation, Ct);

        set.RemoveDatasets([new MapDatasetId("a")], preserveState: true);
        set.RemoveDataset(new MapDatasetId("b"));

        foreach (var session in new[] { first, second })
        {
            Assert.Empty(session.Drawn);
            // An eviction keeps the dataset's place; a removal drops it.
            Assert.Equal([new MapDatasetId("a")], session.Order);
            Assert.All(
                session.Calls.Where(call => call.StartsWith("RemoveDatasets", StringComparison.Ordinal)),
                call => Assert.EndsWith("processor=False)", call));
        }
    }

    [Fact]
    public async Task A_session_added_later_catches_up_to_the_others()
    {
        var set = new MapSessionSet(NullLogger.Instance);
        var first = new FakeMapDatasetSession();
        await set.AddAsync(new MapViewId("map-1"), first, null, Presentation, Ct);
        var mariner = MarinerSettings.Default with { SafetyContour = Depth.FromMetres(12) };
        var time = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        set.SetMarinerSettings(mariner);
        foreach (var id in new[] { "a", "b", "c", "gone" })
            set.SetDataset(Dataset(id), null, null);
        await set.RenderAsync(new MapDatasetId("a"), Presentation, Ct);
        await set.RenderAsync(new MapDatasetId("b"), Presentation, Ct);
        // c is present but not drawn (outside the time window); gone is removed.
        set.ClearLayers(new MapDatasetId("c"));
        set.RemoveDataset(new MapDatasetId("gone"));
        set.SetOrder([new MapDatasetId("c"), new MapDatasetId("b"), new MapDatasetId("a")]);
        set.SetCurrentTime(time);

        var late = new FakeMapDatasetSession();
        await set.AddAsync(new MapViewId("map-2"), late, null, Presentation, Ct);

        Assert.Same(mariner, late.Mariner);
        Assert.Equal(first.Order, late.Order);
        Assert.Equal([new MapDatasetId("a"), new MapDatasetId("b")], late.Drawn.OrderBy(id => id.Value));
        Assert.Equal(time, late.CurrentTime);
        Assert.Contains("RefreshTime", late.Calls);
        Assert.DoesNotContain("Render(c)", late.Calls);
        Assert.DoesNotContain("SetDataset(gone)", late.Calls);
        // Then it takes part like any other session.
        set.SetDataset(Dataset("d"), null, null);
        await set.RenderAsync(new MapDatasetId("d"), Presentation, Ct);
        Assert.Contains(new MapDatasetId("d"), late.Drawn);
    }

    [Fact]
    public async Task A_removed_session_is_released()
    {
        var (set, first, second) = await TwoAsync();
        set.SetDataset(Dataset("a"), null, null);
        await set.RenderAsync(new MapDatasetId("a"), Presentation, Ct);

        Assert.Same(second, set.Remove(new MapViewId("map-2")));

        Assert.False(second.HasSubscribers);
        Assert.Empty(second.Order);
        Assert.Contains("RemoveDatasets(a, preserve=False, processor=False)", second.Calls);
        var calls = second.Calls.Count;
        set.SetDataset(Dataset("b"), null, null);
        set.SetCurrentTime(DateTime.UnixEpoch);
        Assert.Equal(calls, second.Calls.Count);
        Assert.Equal([new MapViewId("map-1")], set.Views);
        Assert.Null(set.Remove(new MapViewId("map-2")));
    }

    [Fact]
    public async Task Removing_the_primary_makes_the_next_session_primary_and_forwards_its_events()
    {
        var (set, first, second) = await TwoAsync();
        var layersChanged = 0;
        var primaryChanged = 0;
        set.LayersChanged += (_, _) => layersChanged++;
        set.PrimaryChanged += (_, _) => primaryChanged++;

        // Only the primary's layer changes are forwarded.
        second.RaiseLayersChanged();
        Assert.Equal(0, layersChanged);
        first.RaiseLayersChanged();
        Assert.Equal(1, layersChanged);

        set.Remove(new MapViewId("map-1"));

        Assert.Equal(1, primaryChanged);
        Assert.Same(second, set.Primary);
        second.RaiseLayersChanged();
        Assert.Equal(2, layersChanged);
    }

    [Fact]
    public async Task A_failing_session_that_is_not_the_primary_does_not_fail_the_call()
    {
        var (set, first, second) = await TwoAsync();
        set.SetDataset(Dataset("a"), null, null);
        second.RenderFailure = new InvalidOperationException("boom");

        var result = await set.RenderAsync(new MapDatasetId("a"), Presentation, Ct);

        Assert.NotNull(result);
        Assert.Contains(new MapDatasetId("a"), first.Drawn);
        Assert.Empty(second.Drawn);
    }

    [Fact]
    public async Task A_view_can_add_only_one_session()
    {
        var set = new MapSessionSet(NullLogger.Instance);
        await set.AddAsync(new MapViewId("map-1"), new FakeMapDatasetSession(), null, Presentation, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            set.AddAsync(new MapViewId("map-1"), new FakeMapDatasetSession(), null, Presentation, Ct));
    }
}
