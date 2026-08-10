using System.Diagnostics;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.McpCapabilities;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the viewer's <see cref="ViewerMutableDatasetCatalog"/> adapter — the
/// classify → trigger → quiesce → diff load orchestration and the sync-over-async
/// removal — that used to live in the viewer's bespoke open/close tools. The
/// tools themselves are exercised in <c>EncDotNet.S100.Mcp.Tools.Tests</c>.
/// </summary>
public class ViewerMutableDatasetCatalogTests
{
    private static ViewerMutableDatasetCatalog Make(
        FakeDatasetCatalog catalog, FakeDatasetLoadGateway gateway) =>
        new(catalog, gateway, quietMs: 50, maxWaitMs: 1000);

    [Fact]
    public async Task LoadAsync_file_reports_the_added_id()
    {
        var catalog = new FakeDatasetCatalog();
        var gateway = new FakeDatasetLoadGateway
        {
            Kind = DatasetPathKind.File,
            OnLoadFile = (p, _) => { catalog.Add(Path.GetFileName(p), "S-102"); return Task.FromResult(true); },
        };
        var sut = Make(catalog, gateway);

        var outcome = await sut.LoadAsync("/tmp/cell.000");

        Assert.Equal(DatasetSourceKind.File, outcome.Kind);
        Assert.Equal("cell.000", Assert.Single(outcome.Added).Value);
        Assert.False(outcome.TimedOut);
    }

    [Fact]
    public async Task LoadAsync_unrecognised_file_reports_nothing_added()
    {
        var catalog = new FakeDatasetCatalog();
        var gateway = new FakeDatasetLoadGateway
        {
            Kind = DatasetPathKind.File,
            OnLoadFile = (_, _) => Task.FromResult(false),
        };
        var sut = Make(catalog, gateway);

        var outcome = await sut.LoadAsync("/tmp/mystery.dat");

        Assert.Empty(outcome.Added);
    }

    [Fact]
    public async Task LoadAsync_when_gateway_not_ready_throws_not_ready()
    {
        var gateway = new FakeDatasetLoadGateway { IsReady = false };
        var sut = Make(new FakeDatasetCatalog(), gateway);

        await Assert.ThrowsAsync<DatasetCatalogNotReadyException>(
            () => sut.LoadAsync("/tmp/cell.000"));
    }

    [Fact]
    public async Task LoadAsync_exchange_set_collects_delayed_adds()
    {
        var catalog = new FakeDatasetCatalog();
        var gateway = new FakeDatasetLoadGateway
        {
            Kind = DatasetPathKind.ExchangeSet,
            OnTriggerExchangeSet = path =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(20);
                    catalog.Add("a.000", "S-101");
                    catalog.Add("b.000", "S-102");
                });
                return Task.FromResult(2);
            },
        };
        var sut = new ViewerMutableDatasetCatalog(catalog, gateway, quietMs: 150, maxWaitMs: 5000);

        var outcome = await sut.LoadAsync("/tmp/exchange");

        Assert.Equal(DatasetSourceKind.ExchangeSet, outcome.Kind);
        Assert.Equal(2, outcome.Added.Count);
        Assert.False(outcome.TimedOut);
    }

    [Fact]
    public async Task LoadAsync_exchange_set_with_nothing_dispatched_reports_nothing_added()
    {
        var catalog = new FakeDatasetCatalog();
        var gateway = new FakeDatasetLoadGateway
        {
            Kind = DatasetPathKind.ExchangeSet,
            OnTriggerExchangeSet = _ => Task.FromResult(0),
        };
        var sut = new ViewerMutableDatasetCatalog(catalog, gateway, quietMs: 100, maxWaitMs: 5000);

        var sw = Stopwatch.StartNew();
        var outcome = await sut.LoadAsync("/tmp/empty-exchange");
        sw.Stop();

        Assert.Empty(outcome.Added);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds}ms; should fail fast");
    }

    [Fact]
    public void Remove_routes_through_the_gateway()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add("a.000", "S-101");
        var gateway = new FakeDatasetLoadGateway
        {
            OnRemove = id => Task.FromResult(catalog.Remove(id)),
        };
        var sut = Make(catalog, gateway);

        Assert.True(sut.Remove(new EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetId("a.000")));
        Assert.False(sut.Remove(new EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetId("ghost")));
        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public void RemoveAll_removes_every_distinct_id()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add("a.000", "S-101");
        catalog.Add("b.h5", "S-102");
        var gateway = new FakeDatasetLoadGateway
        {
            OnRemove = id => Task.FromResult(catalog.Remove(id)),
        };
        var sut = Make(catalog, gateway);

        Assert.Equal(2, sut.RemoveAll());
        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public void Read_side_delegates_to_the_inner_catalog()
    {
        var catalog = new FakeDatasetCatalog();
        var gateway = new FakeDatasetLoadGateway();
        var sut = Make(catalog, gateway);

        var raised = 0;
        sut.Changed += (_, _) => raised++;
        catalog.Add("a.000", "S-101");

        Assert.Equal("a.000", Assert.Single(sut.Datasets).Id.Value);
        Assert.Equal(1, raised);
    }
}
