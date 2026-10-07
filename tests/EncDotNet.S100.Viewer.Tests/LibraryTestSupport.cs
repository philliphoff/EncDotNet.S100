using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Shared fixtures for the Library (issue #655) tests.</summary>
internal sealed class LibraryTestContext : IDisposable
{
    public LibraryTestContext()
    {
        Root = Path.Combine(Path.GetTempPath(), "library-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string StorePath => Path.Combine(Root, "collections.json");

    public string IndexCacheDirectory => Path.Combine(Root, "index-cache");

    public CollectionLibrary CreateService(CollectionIndexer? indexer = null, bool readOnly = false) =>
        new(indexer ?? CollectionIndexer.CreateDefault(), new CollectionLibraryOptions(StorePath, IndexCacheDirectory) { ReadOnly = readOnly });

    /// <summary>Copies the synthetic two-cell S-57 exchange set into the context.</summary>
    public string CreateS57ExchangeSet(string name = "set")
    {
        var source = Datasets("ExchangeSets", "Synthetic-S57-Framed");
        var target = Path.Combine(Root, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }

    public static string Datasets(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets");
            if (Directory.Exists(candidate))
                return Path.Combine([candidate, .. parts]);
        }

        throw new DirectoryNotFoundException("Could not locate tests/datasets.");
    }

    public static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class FakeLibraryDownloader : ILibraryDownloader
{
    public LibraryDownloadProgress? Progress { get; set; }

    public bool CancelledAll { get; private set; }

    public event EventHandler? ProgressChanged;

    public void RaiseProgress() => ProgressChanged?.Invoke(this, EventArgs.Empty);

    public void CancelAll() => CancelledAll = true;

    public bool Downloaded { get; set; }

    public bool Outdated { get; set; }

    public bool CanDownloadAll { get; set; }

    public int Downloads { get; private set; }

    public event EventHandler? Changed;

    public CollectionItem Localize(CollectionItem item) =>
        Downloaded && item.Location is RemoteItemLocation
            ? item with { Location = new LocalItemLocation("/tmp/x", "x.000", []) }
            : item;

    public bool IsOutdated(CollectionItem item) => Outdated;

    /// <summary>Every item's download status, e.g. a failed download (shown as a "Failed · retry" tag).</summary>
    public LibraryDownloadItemStatus? Status { get; set; }

    public LibraryDownloadItemStatus? StatusOf(CollectionItem item) => Status;

    public bool CanDownload(CollectionItem item) => CanDownloadAll || item.Location is RemoteItemLocation;

    public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
    {
        Downloads += items.Count;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeLibraryLoader : ILibraryLoader
{
    public LibraryLoadState State { get; set; }

    public List<(bool Defer, int Count)> Calls { get; } = [];

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public LibraryLoadState StateOf(CollectionItem item) => State;

    public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default)
    {
        Calls.Add((defer, items.Count));
        return Task.FromResult(new LibraryLoadResult(items.Count, 0));
    }
}

internal sealed class RecordingLibraryImporter : ILibraryImporter
{
    public List<(string Kind, Guid? Target)> Calls { get; } = [];

    public Task AddFolderAsync(Guid? targetCollectionId) => Record("folder", targetCollectionId);

    public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Record("zip", targetCollectionId);

    public Task AddOnlineCatalogueAsync(Guid? targetCollectionId) => Record("online", targetCollectionId);

    public Task AddSharedFeedAsync(Guid? targetCollectionId) => Record("feed", targetCollectionId);

    public Task AddKnownCatalogueAsync(EncDotNet.S100.Collections.KnownSources.KnownCatalogueSource source, Guid? targetCollectionId) =>
        Record("known:" + source.Id, targetCollectionId);

    public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Record("s128", targetCollectionId);

    public Task AddCollectionManifestAsync(Guid? targetCollectionId) => Record("manifest", targetCollectionId);

    public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source) =>
        Record("choose:" + source.Id, collectionId);

    public Task AddPathAsync(string path, Guid? targetCollectionId) => Record("path", targetCollectionId);

    public bool IsInLibrary(string path) => false;

    private Task Record(string kind, Guid? target)
    {
        Calls.Add((kind, target));
        return Task.CompletedTask;
    }
}
