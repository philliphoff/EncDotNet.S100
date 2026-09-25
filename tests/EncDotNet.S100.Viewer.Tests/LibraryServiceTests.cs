using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryServiceTests : IDisposable
{
    private readonly LibraryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private static ExchangeSetSource ExchangeSet(string path) => new(Guid.NewGuid(), null, path);

    [Fact]
    public async Task AddCollection_persists_the_definition_and_indexes_the_source()
    {
        var set = _context.CreateS57ExchangeSet();
        using var library = _context.CreateService();
        library.Initialize();

        var collection = library.AddCollection("Puget Sound", [ExchangeSet(set)]);
        await library.WhenIdle();

        var snapshot = Assert.Single(library.Collections);
        Assert.Equal("Puget Sound", snapshot.Definition.Name);
        var source = Assert.Single(snapshot.Sources);
        Assert.Equal(LibrarySourceState.Ready, source.State);
        Assert.Equal(2, snapshot.ItemCount);

        Assert.Contains("Puget Sound", File.ReadAllText(_context.StorePath));
        Assert.True(File.Exists(Path.Combine(_context.IndexCacheDirectory, source.Id.ToString("N") + ".index.json.gz")));
        Assert.Equal(collection.Id, snapshot.Id);
    }

    [Fact]
    public async Task A_new_session_restores_collections_and_reuses_cached_indexes()
    {
        var set = _context.CreateS57ExchangeSet();
        Guid sourceId;
        using (var first = _context.CreateService())
        {
            first.Initialize();
            sourceId = first.AddCollection("Charts", [ExchangeSet(set)]).Sources[0].Id;
            await first.WhenIdle();
        }

        var cachePath = Path.Combine(_context.IndexCacheDirectory, sourceId.ToString("N") + ".index.json.gz");
        var cachedAt = File.GetLastWriteTimeUtc(cachePath);

        using var second = _context.CreateService();
        second.Initialize();
        await second.WhenIdle();

        var source = Assert.Single(Assert.Single(second.Collections).Sources);
        Assert.Equal(2, source.Index!.Items.Count);
        // The source was unchanged, so its cached index was reused, not rewritten.
        Assert.Equal(cachedAt, File.GetLastWriteTimeUtc(cachePath));
    }

    [Fact]
    public async Task RemoveCollection_forgets_it_and_its_cache_but_never_the_data()
    {
        var set = _context.CreateS57ExchangeSet();
        using var library = _context.CreateService();
        library.Initialize();
        var collection = library.AddCollection("Charts", [ExchangeSet(set)]);
        await library.WhenIdle();

        Assert.True(library.RemoveCollection(collection.Id));

        Assert.Empty(library.Collections);
        Assert.Empty(Directory.EnumerateFiles(_context.IndexCacheDirectory));
        Assert.True(File.Exists(Path.Combine(set, "CATALOG.031")));
        Assert.DoesNotContain("Charts", File.ReadAllText(_context.StorePath));
    }

    [Fact]
    public async Task AddSources_RemoveSource_and_Rename_update_the_store()
    {
        using var library = _context.CreateService();
        library.Initialize();
        var collection = library.AddCollection("A", [ExchangeSet(_context.CreateS57ExchangeSet("one"))]);
        var second = ExchangeSet(_context.CreateS57ExchangeSet("two"));

        Assert.True(library.AddSources(collection.Id, [second]));
        Assert.True(library.RenameCollection(collection.Id, "B"));
        await library.WhenIdle();
        Assert.Equal(4, library.Find(collection.Id)!.ItemCount);

        Assert.True(library.RemoveSource(collection.Id, second.Id));
        var snapshot = library.Find(collection.Id)!;
        Assert.Equal("B", snapshot.Definition.Name);
        Assert.Equal(2, snapshot.ItemCount);
        Assert.False(library.AddSources(Guid.NewGuid(), [second]));
    }

    [Fact]
    public async Task Read_only_mode_never_writes_the_store()
    {
        using var library = _context.CreateService(readOnly: true);
        library.Initialize();
        library.AddCollection("Charts", [ExchangeSet(_context.CreateS57ExchangeSet())]);
        await library.WhenIdle();

        Assert.False(File.Exists(_context.StorePath));
        Assert.Single(library.Collections);
    }

    [Fact]
    public async Task Indexing_failure_marks_the_source_failed_with_the_error()
    {
        using var library = _context.CreateService(new CollectionIndexer([new ThrowingIndexer()]));
        library.Initialize();
        library.AddCollection("Broken", [ExchangeSet(_context.Root)]);
        await library.WhenIdle();

        var source = Assert.Single(Assert.Single(library.Collections).Sources);
        Assert.Equal(LibrarySourceState.Failed, source.State);
        Assert.Equal("Indexing exploded.", source.Error);
    }

    [Fact]
    public void Unreadable_store_starts_an_empty_library()
    {
        File.WriteAllText(_context.StorePath, "{ not json");
        using var library = _context.CreateService();

        library.Initialize();

        Assert.Empty(library.Collections);
    }

    [Fact]
    public async Task Session_catalogues_are_transient_until_kept()
    {
        var path = LibraryTestContext.Datasets("S128", "S128_TDS_sample.gml");
        var dataset = S128Dataset.Open(path);
        using var library = _context.CreateService();
        library.Initialize();
        var changes = 0;
        library.Changed += (_, _) => changes++;

        library.AddSessionCatalogue("sample", path, dataset);

        var session = Assert.Single(library.Collections);
        Assert.True(session.IsSession);
        Assert.Equal(LibraryService.SessionCollectionId, session.Id);
        Assert.Equal(dataset.Entries.Count, session.ItemCount);
        Assert.True(changes > 0);
        Assert.False(File.Exists(_context.StorePath));

        var kept = library.KeepSessionCatalogue(session.Sources[0].Id);
        await library.WhenIdle();

        Assert.NotNull(kept);
        var collection = Assert.Single(library.Collections);
        Assert.False(collection.IsSession);
        Assert.IsType<S128CatalogueSource>(Assert.Single(collection.Sources).Definition);
        Assert.Equal(dataset.Entries.Count, collection.ItemCount);
        Assert.Contains("s128Catalogue", File.ReadAllText(_context.StorePath));
    }

    [Fact]
    public void RemoveSessionCatalogue_drops_the_session_collection_when_empty()
    {
        var path = LibraryTestContext.Datasets("S128", "S128_TDS_sample.gml");
        using var library = _context.CreateService();
        library.AddSessionCatalogue("sample", path, S128Dataset.Open(path));

        Assert.True(library.RemoveSessionCatalogue("sample"));
        Assert.False(library.RemoveSessionCatalogue("sample"));
        Assert.Empty(library.Collections);
    }
}
