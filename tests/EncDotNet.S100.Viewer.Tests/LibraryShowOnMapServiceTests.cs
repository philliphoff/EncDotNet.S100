using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// "Show on map" for a Library source (#809): its local datasets are opened
/// under one Datasets row after each index, follow the source's contents, and
/// close when the option is turned off.
/// </summary>
public sealed class LibraryShowOnMapServiceTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
            disposable.Dispose();
        _context.Dispose();
    }

    private (CollectionLibrary Library, DatasetsViewModel Datasets, LibraryShowOnMapService Service) Create()
    {
        var library = _context.CreateService();
        var datasets = new DatasetsViewModel(new LibraryLoadServiceTests.NoopDatasetLoader());
        var exchangeSets = new ExchangeSetService(datasets, Notifications.TestNotifications.Create());
        var loader = new LibraryLoadService(exchangeSets, datasets);
        var service = new LibraryShowOnMapService(library, loader, new FakeLibraryDownloader(), _ => "Charts", dispatch: a => a());
        _disposables.AddRange([library, exchangeSets, loader, service]);
        library.Initialize();
        return (library, datasets, service);
    }

    private static async Task SettleAsync(CollectionLibrary library, LibraryShowOnMapService service)
    {
        await library.WhenIdle().WaitAsync(TestContext.Current.CancellationToken);
        await service.LastApply.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_shown_source_opens_under_one_row_and_follows_its_contents()
    {
        var (library, datasets, service) = Create();
        // A folder of two exchange sets: two roots, one source.
        var root = Path.Combine(_context.Root, "charts");
        Directory.CreateDirectory(root);
        Directory.Move(_context.CreateS57ExchangeSet("a"), Path.Combine(root, "a"));
        Directory.Move(_context.CreateS57ExchangeSet("b"), Path.Combine(root, "b"));
        var source = new LocalFolderSource(Guid.NewGuid(), null, root) { ShowOnMap = true };
        var collection = library.AddCollection("Charts", [source]);

        await SettleAsync(library, service);

        Assert.Equal(4, datasets.Entries.Count);
        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal("Charts", header.DisplayName);
        Assert.Equal(2, header.SetCount);
        Assert.Equal(4, service.Shown[source.Id]);

        // Datasets the source no longer has are closed on its next index.
        Directory.Delete(Path.Combine(root, "b"), recursive: true);
        library.Refresh(sourceId: source.Id);
        await SettleAsync(library, service);
        Assert.Equal(2, datasets.Entries.Count);
        Assert.Equal(1, Assert.Single(datasets.ExchangeSetHeaders).SetCount);

        // Turning it off closes the rest.
        library.UpdateSource(collection.Id, source with { ShowOnMap = false });
        await SettleAsync(library, service);
        Assert.Empty(datasets.Entries);
        Assert.Empty(datasets.ExchangeSetHeaders);
        Assert.Empty(service.Shown);
    }

    [Fact]
    public async Task Sources_not_shown_are_left_alone_and_removing_a_shown_source_closes_it()
    {
        var (library, datasets, service) = Create();
        var hidden = library.AddCollection("Hidden", [new LocalFolderSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("a"))]);
        await SettleAsync(library, service);
        Assert.Empty(datasets.Entries);

        var shown = new LocalFolderSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("b")) { ShowOnMap = true };
        var collection = library.AddCollection("Shown", [shown]);
        await SettleAsync(library, service);
        Assert.Equal(2, datasets.Entries.Count);

        library.RemoveCollection(collection.Id);
        await SettleAsync(library, service);
        Assert.Empty(datasets.Entries);
        Assert.NotNull(hidden);
    }
}
