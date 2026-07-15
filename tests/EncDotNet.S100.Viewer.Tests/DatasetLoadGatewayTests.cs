using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetLoadGatewayTests
{
    /// <summary>Stub exchange-set service; gateway logic tests never open one.</summary>
    private sealed class StubExchangeSetService : IExchangeSetService
    {
        public Task<ExchangeSetOpenResult> OpenAsync(
            string folderOrZipPath, IProgress<ExchangeSetProgress>? progress = null,
            CancellationToken cancellationToken = default,
            Services.Notifications.INotificationHandle? notification = null,
            Action<EncDotNet.S100.ExchangeSets.BoundingBox>? onFramingReady = null)
            => Task.FromResult(new ExchangeSetOpenResult { SourcePath = folderOrZipPath });
    }

    // Synchronous dispatcher so the production gateway runs without an
    // Avalonia UI thread.
    private static readonly Func<Func<Task>, Task> Inline = work => work();

    private static DatasetLoadGateway Make(out DatasetsViewModel datasets, out FakeDatasetLoaderService loader)
    {
        loader = new FakeDatasetLoaderService();
        datasets = new DatasetsViewModel(loader);
        return new DatasetLoadGateway(datasets, loader, new StubExchangeSetService(), Inline);
    }

    [Fact]
    public void Classify_returns_file_for_plain_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gw-{Guid.NewGuid():N}.000");
        File.WriteAllText(path, "x");
        try
        {
            var gateway = Make(out _, out _);
            Assert.Equal(DatasetPathKind.File, gateway.Classify(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Classify_returns_exchange_set_for_folder_with_catalogue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CATALOG.XML"), "<x/>");
        try
        {
            var gateway = Make(out _, out _);
            Assert.Equal(DatasetPathKind.ExchangeSet, gateway.Classify(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Classify_returns_exchange_set_for_catalogue_less_loose_cell_folder()
    {
        // A folder of loose cells (base ….000 + updates, no catalogue)
        // routes to the exchange-set path so its base cells load with
        // their sequential updates applied (issue #449).
        var dir = Path.Combine(Path.GetTempPath(), $"gw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CELL.000"), "base");
        File.WriteAllText(Path.Combine(dir, "CELL.001"), "update");
        try
        {
            var gateway = Make(out _, out _);
            Assert.Equal(DatasetPathKind.ExchangeSet, gateway.Classify(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Classify_returns_file_for_folder_without_cells_or_catalogue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "text");
        try
        {
            var gateway = Make(out _, out _);
            Assert.Equal(DatasetPathKind.File, gateway.Classify(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsReady_reflects_loader_initialisation()
    {
        var gateway = Make(out _, out var loader);
        loader.IsInitializedValue = false;
        Assert.False(gateway.IsReady);
        loader.IsInitializedValue = true;
        Assert.True(gateway.IsReady);
    }

    [Fact]
    public async Task RemoveAsync_removes_entry_and_calls_loader_directly()
    {
        var gateway = Make(out var datasets, out var loader);
        var entry = datasets.Add("/tmp/chart.000", "S-101");
        Assert.Contains(entry, datasets.Entries);

        var removed = await gateway.RemoveAsync(entry.DisplayName);

        Assert.Equal(1, removed);
        Assert.DoesNotContain(entry, datasets.Entries);
        // Self-sufficient: drops the loader's layers without relying on the
        // window's CollectionChanged → RemoveEntry wiring.
        Assert.Contains(entry, loader.RemovedEntries);
    }

    [Fact]
    public async Task RemoveAsync_unknown_id_removes_nothing()
    {
        var gateway = Make(out var datasets, out _);
        datasets.Add("/tmp/chart.000", "S-101");

        var removed = await gateway.RemoveAsync("not-loaded.000");

        Assert.Equal(0, removed);
        Assert.Single(datasets.Entries);
    }

    [Fact]
    public async Task RemoveAsync_removes_all_entries_sharing_a_display_name()
    {
        var gateway = Make(out var datasets, out _);
        // Two distinct paths whose file name (and thus DisplayName / catalog
        // id) collide — the documented duplicate-DisplayName case.
        datasets.Add("/a/chart.000", "S-101");
        datasets.Add("/b/chart.000", "S-101");

        var removed = await gateway.RemoveAsync("chart.000");

        Assert.Equal(2, removed);
        Assert.Empty(datasets.Entries);
    }

    [Fact]
    public async Task LoadFileAsync_removes_entry_when_load_throws()
    {
        var gateway = Make(out var datasets, out var loader);
        loader.LoadHook = (_, _) => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.LoadFileAsync("/tmp/chart.000", specHint: "S-101"));

        // No stale half-loaded entry left behind.
        Assert.Empty(datasets.Entries);
    }

    [Fact]
    public async Task LoadFileAsync_with_spec_hint_adds_and_loads_entry()
    {
        var gateway = Make(out var datasets, out var loader);
        DatasetEntry? loaded = null;
        loader.LoadHook = (e, _) => { loaded = e; return Task.CompletedTask; };

        var ok = await gateway.LoadFileAsync("/tmp/chart.000", specHint: "S-101");

        Assert.True(ok);
        Assert.NotNull(loaded);
        Assert.Equal("S-101", loaded!.ProductSpec);
        Assert.Contains(loaded, datasets.Entries);
    }
}
