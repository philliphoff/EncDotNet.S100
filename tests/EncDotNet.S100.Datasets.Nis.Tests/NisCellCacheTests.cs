using EncDotNet.S100.Datasets.Nis.Tests.TestSupport;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Nis.Tests;

/// <summary>
/// Tests for <see cref="NisCellCache"/> and <see cref="NisCellPath"/> on Esri's
/// sample NIS, which tools/nis-fixtures/fetch-tutorial-nis.sh downloads; skipped
/// without it.
/// </summary>
public sealed class NisCellCacheTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("nis-cells-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Every_cell_is_written_once_and_reused()
    {
        TutorialNis.SkipUnlessPresent();

        var cells = NisCellCache.Materialize(TutorialNis.Path, _cache);

        Assert.Equal(["US5CA98M.005", "US5NY1DM.010"], cells.Select(c => c.DatasetName));
        Assert.Equal(["US5CA98M.000", "US5NY1DM.000"], cells.Select(c => Path.GetFileName(c.Path)));
        var document = S57DocumentReader.Read(File.ReadAllBytes(cells[0].Path));
        Assert.Equal("US5CA98M.000", document.DataSetIdentification?.DataSetName);

        var written = File.GetLastWriteTimeUtc(cells[0].Path);
        var again = NisCellCache.Materialize(TutorialNis.Path, _cache);
        Assert.Equal(cells.Select(c => c.Path), again.Select(c => c.Path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(again[0].Path));
        Assert.Single(Directory.GetDirectories(_cache));
    }

    [Fact]
    public void A_changed_geodatabase_gets_new_cells()
    {
        TutorialNis.SkipUnlessPresent();
        var copy = Path.Combine(_cache, "copy.gdb");
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.GetFiles(TutorialNis.Path))
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        var root = Path.Combine(_cache, "cells");

        var before = NisCellCache.Fingerprint(copy);
        var first = NisCellCache.Materialize(copy, root);
        File.SetLastWriteTimeUtc(Path.Combine(copy, "a00000001.gdbtable"), DateTime.UtcNow.AddMinutes(1));
        File.WriteAllText(Path.Combine(copy, "edit.lock"), "ArcGIS holds a lock");

        Assert.NotEqual(before, NisCellCache.Fingerprint(copy));
        var second = NisCellCache.Materialize(copy, root);
        Assert.NotEqual(Path.GetDirectoryName(first[0].Path), Path.GetDirectoryName(second[0].Path));
        Assert.DoesNotContain(NisCellCache.Files(copy), f => f.EndsWith(".lock", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cell_path_names_a_cell_of_an_nis()
    {
        TutorialNis.SkipUnlessPresent();
        var cells = NisCellCache.Materialize(TutorialNis.Path, _cache);

        Assert.True(NisCellPath.TryParse(Path.Combine(TutorialNis.Path, "US5CA98M.005"), out var full));
        Assert.Equal("US5CA98M.005", full?.Find(cells)?.DatasetName);
        Assert.True(NisCellPath.TryParse(Path.Combine(TutorialNis.Path, "us5ny1dm"), out var shortName));
        Assert.Equal("US5NY1DM.010", shortName?.Find(cells)?.DatasetName);
        Assert.True(NisCellPath.TryParse(Path.Combine(TutorialNis.Path, "XX5NONE1"), out var none));
        Assert.Null(none?.Find(cells));

        // A real file in the geodatabase isn't a cell path.
        Assert.False(NisCellPath.TryParse(Path.Combine(TutorialNis.Path, "a00000001.gdbtable"), out _));
    }

    [Fact]
    public void Only_an_nis_geodatabase_holds_cell_paths()
    {
        Assert.False(NisCellPath.TryParse(Path.Combine(_cache, "US5CA98M.005"), out _));
        Assert.False(NisCellPath.TryParse(null, out _));
        Assert.False(FileGdb.FileGdbNisSource.IsNisGeodatabase(_cache));
    }
}
