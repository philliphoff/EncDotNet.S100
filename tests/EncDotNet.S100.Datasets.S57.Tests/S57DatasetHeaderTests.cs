using System.Globalization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Tests for <see cref="S57DatasetHeader"/> — the leading-records-only read of
/// an S-57 cell's <c>DSID</c> / <c>DSPM</c> used by collection indexing
/// (issue #655), which must agree with a full parse of the same cell.
/// </summary>
public class S57DatasetHeaderTests
{
    [SkippableFact]
    public void Read_real_cell_matches_full_parse()
    {
        var path = ResolveFixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(path), $"Fixture not found: {path}");

        var header = S57DatasetHeader.Read(path);
        var full = S57DocumentReader.ReadFromFile(path, logger: null);

        Assert.NotNull(header);
        var dsid = full.DataSetIdentification!;
        Assert.Equal(dsid.DataSetName, header.DataSetName);
        Assert.Equal(int.Parse(dsid.EditionNumber, CultureInfo.InvariantCulture), header.EditionNumber);
        Assert.Equal(int.Parse(dsid.UpdateNumber, CultureInfo.InvariantCulture), header.UpdateNumber);
        Assert.Equal(
            DateOnly.ParseExact(dsid.IssueDate, "yyyyMMdd", CultureInfo.InvariantCulture),
            header.IssueDate);
        Assert.Equal(full.DataSetParameters!.CompilationScale, header.CompilationScale);
    }

    [SkippableFact]
    public void Read_consumes_only_the_leading_records()
    {
        var path = ResolveFixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(path), $"Fixture not found: {path}");

        using var stream = File.OpenRead(path);
        Assert.NotNull(S57DatasetHeader.Read(stream));

        // The cell is far larger than its DDR + DSID + DSPM records.
        Assert.True(stream.Position < 16 * 1024, $"Read {stream.Position} bytes.");
        Assert.True(stream.Position < stream.Length);
    }

    [Fact]
    public void Read_returns_null_for_non_iso8211_content()
    {
        using var stream = new MemoryStream("<?xml version=\"1.0\"?><root/>"u8.ToArray());

        Assert.Null(S57DatasetHeader.Read(stream));
    }

    [SkippableFact]
    public void Read_returns_null_for_an_s101_cell()
    {
        // S-101 cells are ISO 8211 too, but their DSID lacks S-57 subfields.
        var path = Path.Combine(ResolveDatasetsRoot(), "S101", "S-101", "DATASET_FILES", "101AA00DS0019.000");
        Skip.IfNot(File.Exists(path), $"Fixture not found: {path}");

        Assert.Null(S57DatasetHeader.Read(path));
    }

    [Fact]
    public void Read_returns_null_for_empty_stream()
    {
        Assert.Null(S57DatasetHeader.Read(new MemoryStream()));
    }

    private static string ResolveDatasetsRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets");
            if (Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine("tests", "datasets");
    }

    private static string ResolveFixturePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S57", "US5MA1BO", fileName);
    }
}
