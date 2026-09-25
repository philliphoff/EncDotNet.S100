using System.IO.Compression;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Collections.Tests;

public class LocalSourceIndexerTests
{
    private static readonly CollectionIndexer Indexer = CollectionIndexer.CreateDefault();

    private static Task<SourceIndex> IndexAsync(CollectionSource source, CollectionIndexer? indexer = null) =>
        (indexer ?? Indexer).IndexAsync(source).AsTask();

    private static ExchangeSetSource ExchangeSet(string path) => new(Guid.NewGuid(), null, path);

    private static LocalFolderSource Folder(string path, bool recursive = true) =>
        new(Guid.NewGuid(), null, path, recursive);

    [Fact]
    public async Task S100_exchange_set_folds_updates_into_their_base()
    {
        var root = TestPaths.Dataset("ExchangeSets", "Synthetic-S101Updates");

        var index = await IndexAsync(ExchangeSet(root));

        var item = Assert.Single(index.Items);
        Assert.Equal("S-101", item.ProductSpec);
        Assert.Equal("SYNTH101", item.Name);
        Assert.Equal(1, item.Edition);
        Assert.Equal(2, item.Update);
        Assert.Equal(new DateOnly(2026, 1, 14), item.IssueDate);
        Assert.Equal("./S-101/SYNTH101.000", item.Key);

        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal(Path.GetFullPath(root), location.RootPath);
        Assert.Equal("S-101/SYNTH101.000", location.RelativePath);
        Assert.Equal(["S-101/SYNTH101.001", "S-101/SYNTH101.002"], location.UpdateRelativePaths);
        Assert.Equal("CATALOG.XML", location.CatalogueRelativePath);
        Assert.False(location.IsZip);
    }

    [Fact]
    public async Task S100_orphan_update_is_indexed_with_a_warning()
    {
        var index = await IndexAsync(ExchangeSet(TestPaths.Dataset("ExchangeSets", "Synthetic-S101Orphan")));

        var item = Assert.Single(index.Items);
        Assert.Equal("SYNTH101", item.Name);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Warning);
    }

    /// <summary>
    /// Copies the synthetic S-57 exchange set into <paramref name="temp"/>,
    /// replacing the placeholder US5WA51M cell with a real cell so its DSID
    /// can be read; US5WA52M stays a placeholder.
    /// </summary>
    private static string CreateS57Set(TempDirectory temp)
    {
        var root = temp.CopyTree(TestPaths.Dataset("ExchangeSets", "Synthetic-S57-Framed"), "set");
        File.Copy(
            TestPaths.Dataset("S57", "US5MA1BO", "US5MA1BO.000"),
            Path.Combine(root, "US5WA51M", "US5WA51M.000"),
            overwrite: true);
        return root;
    }

    [Fact]
    public async Task S57_exchange_set_reads_catalogue_bounds_and_cell_headers()
    {
        using var temp = new TempDirectory();
        var root = CreateS57Set(temp);
        var expected = Datasets.S57.S57DatasetHeader.Read(Path.Combine(root, "US5WA51M", "US5WA51M.000"))!;

        var index = await IndexAsync(ExchangeSet(root));

        Assert.Equal(["US5WA51M", "US5WA52M"], index.Items.Select(i => i.Name));
        foreach (var item in index.Items)
        {
            Assert.Equal("S-57", item.ProductSpec);
            Assert.Equal(5, item.UsageBand);
            Assert.NotNull(item.Bounds);
            Assert.Equal("./" + item.Name, item.Key);
            var location = Assert.IsType<LocalItemLocation>(item.Location);
            Assert.Equal($"{item.Name}/{item.Name}.000", location.RelativePath);
            Assert.Equal("CATALOG.031", location.CatalogueRelativePath);
        }

        var real = index.Items[0];
        Assert.Equal(expected.EditionNumber, real.Edition);
        Assert.Equal(expected.UpdateNumber, real.Update);
        Assert.Equal(expected.IssueDate, real.IssueDate);
        Assert.Equal(expected.CompilationScale, real.CompilationScale);

        // The placeholder cell is still listed, with its header gap reported.
        Assert.Null(index.Items[1].Edition);
        var warning = Assert.Single(index.Diagnostics);
        Assert.Equal(IndexDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("US5WA52M/US5WA52M.000", warning.Path);
    }

    [Fact]
    public async Task Folder_walk_indexes_each_exchange_set_without_descending_into_it()
    {
        var index = await IndexAsync(Folder(TestPaths.Dataset("ExchangeSets")));

        var groups = index.Items.Select(i => i.GroupKey).Distinct().ToHashSet();
        Assert.Contains("Synthetic-S101Updates", groups);
        Assert.Contains("Synthetic-S57-Framed", groups);
        Assert.Contains("Synthetic-Renderable", groups);

        // Files inside an exchange set are catalogue members, never loose items.
        Assert.All(index.Items, i => Assert.NotNull(i.GroupKey));
        Assert.StartsWith("local-v1:", index.Fingerprint);
    }

    [Fact]
    public async Task Non_recursive_folder_ignores_subfolders()
    {
        var index = await IndexAsync(Folder(TestPaths.Dataset("ExchangeSets"), recursive: false));

        Assert.Empty(index.Items);
    }

    [Fact]
    public async Task Zipped_s100_exchange_set_is_indexed_in_place()
    {
        var zip = TestPaths.Dataset("S101.zip");

        var index = await IndexAsync(ExchangeSet(zip));

        Assert.Equal(19, index.Items.Count);
        var location = Assert.IsType<LocalItemLocation>(index.Items[0].Location);
        Assert.True(location.IsZip);
        Assert.Equal(Path.GetFullPath(zip), location.RootPath);
        Assert.StartsWith("S-101/", location.RelativePath);
        Assert.Equal("S101.zip!/", index.Items[0].GroupKey);
    }

    [Fact]
    public async Task Zipped_s57_exchange_set_under_a_prefix_resolves_paths_through_the_prefix()
    {
        using var temp = new TempDirectory();
        var zipPath = Path.Combine(temp.Path, "cells.zip");
        using var setTemp = new TempDirectory();
        var source = CreateS57Set(setTemp);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var entry = "ENC_ROOT/" + Path.GetRelativePath(source, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entry);
            }
        }

        var index = await IndexAsync(Folder(temp.Path));

        Assert.Equal(2, index.Items.Count);
        var item = index.Items[0];
        Assert.Equal("cells.zip!/ENC_ROOT", item.GroupKey);
        Assert.NotNull(item.Edition);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.True(location.IsZip);
        Assert.Equal("ENC_ROOT/US5WA51M/US5WA51M.000", location.RelativePath);
        Assert.Equal("ENC_ROOT/CATALOG.031", location.CatalogueRelativePath);
    }

    [Fact]
    public async Task Noaa_s100_catalogue_yields_polygons_and_catalogue_metadata()
    {
        using var temp = new TempDirectory();
        File.Copy(TestPaths.Fixture("noaa-s104-catalog.xml"), Path.Combine(temp.Path, "CATALOG.XML"));

        var index = await IndexAsync(ExchangeSet(temp.Path));

        Assert.Equal(2, index.Items.Count);
        var item = index.Items[0];
        Assert.Equal("S-104", item.ProductSpec);
        Assert.Equal("104US004SC1BO_20251217T12Z", item.Name);
        Assert.Equal(1, item.Edition);
        Assert.Equal(0, item.Update);
        Assert.Equal(new DateOnly(2025, 12, 20), item.IssueDate);
        var polygon = Assert.Single(item.Coverage!.Polygons);
        Assert.Equal(5, polygon.Exterior.Count);
        Assert.Equal(-80.1, item.Bounds!.Value.West, 3);
        Assert.Equal(32.7, item.Bounds!.Value.North, 3);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal("../Southeast/Charleston/104US004SC1BO_20251217T12Z.h5", location.RelativePath);
    }

    [Fact]
    public async Task Loose_s57_cell_without_probe_reports_header_but_no_bounds()
    {
        using var temp = new TempDirectory();
        File.Copy(TestPaths.Dataset("S57", "US5MA1BO", "US5MA1BO.000"), Path.Combine(temp.Path, "US5MA1BO.000"));

        var index = await IndexAsync(Folder(temp.Path));

        var item = Assert.Single(index.Items);
        Assert.Equal("S-57", item.ProductSpec);
        Assert.Equal("US5MA1BO", item.Name);
        Assert.Equal("US5MA1BO.000", item.Key);
        Assert.Null(item.GroupKey);
        Assert.Equal(5, item.UsageBand);
        Assert.NotNull(item.Edition);
        Assert.Null(item.Bounds);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal(temp.Path, location.RootPath);
        Assert.Equal("US5MA1BO.000", location.RelativePath);
        Assert.Null(location.CatalogueRelativePath);
    }

    [Fact]
    public async Task Loose_files_use_the_probe_for_spec_bounds_and_scale()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "warnings.gml"), "<gml/>");
        File.WriteAllText(Path.Combine(temp.Path, "grid.h5"), "not really hdf5");
        File.WriteAllText(Path.Combine(temp.Path, "notes.gml"), "<other/>");

        DatasetMetadata? Probe(string path, CancellationToken _) => Path.GetFileName(path) switch
        {
            "warnings.gml" => new DatasetMetadata
            {
                Spec = SpecRef.Parse("S-124/2.0.0"),
                Extent = new BoundingBox(40, -75, 42, -70),
                DisplayScale = new DisplayScaleRange(1_500_000, 22_000),
            },
            "grid.h5" => new DatasetMetadata
            {
                Spec = SpecRef.Parse("S-102/3.0.0"),
                Extent = new BoundingBox(4_000_000, 300_000, 4_100_000, 400_000),
                HorizontalCrsEpsg = 32618,
            },
            _ => null,
        };

        var index = await IndexAsync(Folder(temp.Path), CollectionIndexer.CreateDefault(Probe));

        Assert.Equal(["grid", "warnings"], index.Items.Select(i => i.Name).Order());

        var warnings = index.Items.Single(i => i.Name == "warnings");
        Assert.Equal("S-124", warnings.ProductSpec);
        Assert.Equal("2.0.0", warnings.ProductSpecVersion);
        Assert.Equal(new GeoBounds(40, -75, 42, -70), warnings.Bounds);
        Assert.Equal(1_500_000, warnings.MinimumDisplayScale);
        Assert.Equal(22_000, warnings.MaximumDisplayScale);

        // A projected extent is not usable as geographic bounds.
        var grid = index.Items.Single(i => i.Name == "grid");
        Assert.Null(grid.Bounds);
        Assert.Contains(index.Diagnostics, d => d.Path!.EndsWith("grid.h5", StringComparison.Ordinal));
        Assert.Contains(index.Diagnostics, d => d.Path!.EndsWith("notes.gml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unchanged_source_reuses_the_previous_index_and_a_change_rebuilds_it()
    {
        using var temp = new TempDirectory();
        var root = temp.CopyTree(TestPaths.Dataset("ExchangeSets", "Synthetic-S57-Framed"), "set");
        var source = ExchangeSet(root);

        var first = await IndexAsync(source);
        var second = await Indexer.IndexAsync(source, first);
        Assert.Same(first, second);

        var cell = Path.Combine(root, "US5WA51M", "US5WA51M.000");
        File.SetLastWriteTimeUtc(cell, File.GetLastWriteTimeUtc(cell).AddMinutes(1));

        var third = await Indexer.IndexAsync(source, first);
        Assert.NotSame(first, third);
        Assert.NotEqual(first.Fingerprint, third.Fingerprint);
    }

    [Fact]
    public async Task Missing_source_reports_an_error_and_no_fingerprint()
    {
        var index = await IndexAsync(Folder(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())));

        Assert.Empty(index.Items);
        Assert.Null(index.Fingerprint);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Walking_the_committed_datasets_never_fails()
    {
        var index = await IndexAsync(Folder(TestPaths.Datasets));

        Assert.NotEmpty(index.Items);
        Assert.DoesNotContain(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error);
    }
}
