using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Library;

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

    public LibraryService CreateService(CollectionIndexer? indexer = null, bool readOnly = false) =>
        new(indexer ?? CollectionIndexer.CreateDefault(), StorePath, IndexCacheDirectory, readOnly);

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

/// <summary>An indexer that always throws, for failure-path tests.</summary>
internal sealed class ThrowingIndexer : ICollectionSourceIndexer
{
    public bool CanIndex(CollectionSource source) => true;

    public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(null);

    public ValueTask<SourceIndex> IndexAsync(
        CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Indexing exploded.");
}
