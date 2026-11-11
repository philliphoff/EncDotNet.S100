using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Tests;

public class LocalManifestIndexerTests
{
    private static readonly CollectionIndexer Indexer = CollectionIndexer.CreateDefault();

    /// <summary>
    /// Builds <c>AU/set</c> and <c>BE/set</c> (each one synthetic S-101
    /// exchange set) and a manifest over them in <paramref name="temp"/>.
    /// </summary>
    private static string CreateTree(TempDirectory temp, string groups)
    {
        var set = TestPaths.Dataset("ExchangeSets", "Synthetic-S101Updates");
        temp.CopyTree(set, Path.Combine("AU", "set"));
        temp.CopyTree(set, Path.Combine("BE", "set"));
        return WriteManifest(temp, groups);
    }

    private static string WriteManifest(TempDirectory temp, string groups)
    {
        var path = Path.Combine(temp.Path, "test.s100collection.json");
        File.WriteAllText(path, $$"""
            { "format": "encdotnet-s100-collection", "version": 1, "title": "Test", "groups": [ {{groups}} ] }
            """);
        return path;
    }

    private const string TwoGroups = """
        { "id": "AU", "name": "Australia", "paths": ["AU"] },
        { "id": "BE", "name": "Belgium", "paths": ["BE"] }
        """;

    private static LocalManifestSource Source(string path, params string[] groups) =>
        new(Guid.NewGuid(), null, path, new LocalManifestFilter { Groups = groups });

    private static Task<SourceIndex> IndexAsync(CollectionSource source, SourceIndex? previous = null) =>
        Indexer.IndexAsync(source, previous).AsTask();

    [Fact]
    public async Task Items_are_tagged_with_their_group_and_prefixed_keys()
    {
        using var temp = new TempDirectory();
        var source = Source(CreateTree(temp, TwoGroups));

        var index = await IndexAsync(source);

        Assert.DoesNotContain(index.Diagnostics, d => d.Severity != IndexDiagnosticSeverity.Info);
        Assert.NotNull(index.Fingerprint);
        Assert.Equal(["AU", "BE"], index.Items.Select(i => i.Properties[LocalManifestIndexer.GroupProperty]));
        Assert.Equal(["Australia", "Belgium"], index.Items.Select(i => i.Properties[LocalManifestIndexer.GroupNameProperty]));
        Assert.Equal(["AU:AU/set/S-101/SYNTH101.000", "BE:BE/set/S-101/SYNTH101.000"], index.Items.Select(i => i.Key));
        Assert.Equal(["AU:AU/set", "BE:BE/set"], index.Items.Select(i => i.GroupKey));

        var location = Assert.IsType<LocalItemLocation>(index.Items[0].Location);
        Assert.Equal(Path.Combine(temp.Path, "AU", "set"), location.RootPath);
    }

    [Fact]
    public async Task Keys_stay_unique_when_groups_share_a_folder()
    {
        using var temp = new TempDirectory();
        var source = Source(CreateTree(temp, """
            { "id": "A", "paths": ["AU", "AU/set"] },
            { "id": "B", "paths": ["AU"] }
            """));

        var index = await IndexAsync(source);

        Assert.Equal(3, index.Items.Count);
        Assert.Equal(index.Items.Count, index.Items.Select(i => i.Key).Distinct().Count());
        Assert.Equal(["A", "A", "B"], index.Items.Select(i => i.Properties[LocalManifestIndexer.GroupProperty]));
    }

    [Fact]
    public async Task Filter_limits_the_groups_indexed()
    {
        using var temp = new TempDirectory();
        var source = Source(CreateTree(temp, TwoGroups), "be");

        var index = await IndexAsync(source);

        var item = Assert.Single(index.Items);
        Assert.Equal("BE", item.Properties[LocalManifestIndexer.GroupProperty]);
    }

    [Fact]
    public async Task Missing_paths_and_unknown_groups_are_warnings()
    {
        using var temp = new TempDirectory();
        var source = Source(CreateTree(temp, TwoGroups + """, { "id": "PE", "name": "Peru", "paths": ["PE"] }"""), "AU", "PE", "XX");

        var index = await IndexAsync(source);

        Assert.Single(index.Items);
        Assert.Equal([new SourceIndexGroup("AU", "Australia"), new SourceIndexGroup("PE", "Peru", 1)], index.Groups);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Warning && d.Message.Contains("'Peru'"));
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Warning && d.Message.Contains("'XX'"));
        Assert.DoesNotContain(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Unreadable_manifest_is_an_error_without_a_fingerprint()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "bad.s100collection.json");
        File.WriteAllText(path, """{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "id": "A" } ] }""");

        var index = await IndexAsync(Source(path));
        var missing = await IndexAsync(Source(Path.Combine(temp.Path, "missing.s100collection.json")));

        Assert.Empty(index.Items);
        Assert.Null(index.Fingerprint);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error && d.Message.Contains("groups[0]"));
        Assert.Contains(missing.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error && d.Message == "Manifest not found.");
    }

    [Fact]
    public async Task Unchanged_source_reuses_its_index()
    {
        using var temp = new TempDirectory();
        var source = Source(CreateTree(temp, TwoGroups));
        var first = await IndexAsync(source);

        var second = await IndexAsync(source, first);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Fingerprint_follows_the_manifest_and_selected_groups_only()
    {
        using var temp = new TempDirectory();
        var source = Source(CreateTree(temp, TwoGroups), "AU");
        var indexer = new LocalManifestIndexer(new LocalSourceIndexer());
        async Task<string?> Fingerprint() => await indexer.GetFingerprintAsync(source, CancellationToken.None);

        var original = await Fingerprint();
        Assert.Equal(original, (await IndexAsync(source)).Fingerprint);

        // An unselected group changing does not matter.
        File.WriteAllText(Path.Combine(temp.Path, "BE", "set", "NEW.TXT"), "x");
        Assert.Equal(original, await Fingerprint());

        // A selected group changing does.
        File.WriteAllText(Path.Combine(temp.Path, "AU", "set", "NEW.TXT"), "x");
        var changed = await Fingerprint();
        Assert.NotEqual(original, changed);

        // So does editing the manifest.
        WriteManifest(temp, TwoGroups.Replace("Australia", "Oz", StringComparison.Ordinal));
        Assert.NotEqual(changed, await Fingerprint());
    }
}
