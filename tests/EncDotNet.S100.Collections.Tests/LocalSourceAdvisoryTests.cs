using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.TropicalCyclones;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Local sources holding NHC tropical cyclone advisories (#1688): a forecast
/// zip is a dataset, not an exchange set, and a folder of one storm's zips is
/// one advisory, as the CLI's folder input reads it.
/// </summary>
public class LocalSourceAdvisoryTests
{
    private const string ForecastZip = "al092022_5day_020.zip";
    private const string WindRadiiZip = "al092022_fcst_020.zip";
    private const string BestTrackZip = "al092022_best_track.zip";

    private static readonly SpecRef TropicalCyclone = SpecRef.Parse("TC/1.0.0");

    private static readonly CollectionIndexer Indexer = CollectionIndexer.CreateDefault(Probe);

    /// <summary>
    /// The hosts' probe, cut down: an advisory as <c>DetectProductSpec</c>
    /// claims one (a forecast zip by its first entry, or an advisory folder),
    /// and a folder of GRIB2 files as a run, in the registry's order.
    /// </summary>
    private static DatasetMetadata? Probe(string path, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            // GRIB2 is registered ahead of TC, so a folder holding both is a run.
            if (Directory.EnumerateFiles(path, "*.grib2").Count() > 1)
                return new DatasetMetadata { Spec = SpecRef.Parse("GRIB2/2.0.0") };
            return NhcAdvisoryFiles.IsAdvisoryFolder(path) ? AdvisoryMetadata(path) : null;
        }

        var header = new byte[512];
        int read;
        using (var stream = File.OpenRead(path))
            read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return NhcAdvisoryReader.LooksLikeForecastZip(header.AsSpan(0, read)) ? AdvisoryMetadata(path) : null;
    }

    private static DatasetMetadata AdvisoryMetadata(string path)
    {
        var advisory = NhcAdvisoryReader.Read(path);
        return new DatasetMetadata
        {
            Spec = TropicalCyclone,
            Extent = advisory.Extent,
            HorizontalCrsEpsg = 4326,
            TimeCoverage = new TimeCoverage(advisory.TrackStart, advisory.ForecastEnd),
        };
    }

    private static Task<SourceIndex> IndexAsync(CollectionSource source, SourceIndex? previous = null) =>
        Indexer.IndexAsync(source, previous, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private static LocalFolderSource Folder(string path) => new(Guid.NewGuid(), null, path, Recursive: true);

    private static string CopyIan(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(TestPaths.Dataset("TC", "al092022"), "*.zip"))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        return folder;
    }

    [Fact]
    public async Task An_advisory_zip_on_its_own_is_one_item_read_with_its_siblings()
    {
        using var temp = new TempDirectory();
        var zip = Path.Combine(CopyIan(temp.Path), ForecastZip);

        var index = await IndexAsync(new ExchangeSetSource(Guid.NewGuid(), null, zip));

        var item = Assert.Single(index.Items);
        Assert.Equal("TC", item.ProductSpec);
        Assert.Equal("al092022_5day_020", item.Name);
        Assert.Equal(ForecastZip, item.Key);
        Assert.NotNull(item.Bounds);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal(Path.Combine(temp.Path, ForecastZip), Path.Combine(location.RootPath, location.RelativePath));
        Assert.False(location.IsZip);

        // The best track beside it was read: the item starts with Ian's past track, before the advisory.
        var start = DateTimeOffset.Parse(item.Properties["timeStart"], CultureInfo.InvariantCulture);
        Assert.True(start < new DateTimeOffset(2022, 9, 27, 0, 0, 0, TimeSpan.Zero));
        Assert.DoesNotContain(index.Diagnostics, d => d.Severity >= IndexDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task An_advisory_folder_is_one_item_located_at_the_folder()
    {
        using var temp = new TempDirectory();
        var ian = CopyIan(Path.Combine(temp.Path, "ian"));

        var index = await IndexAsync(Folder(temp.Path));

        var item = Assert.Single(index.Items);
        Assert.Equal("TC", item.ProductSpec);
        Assert.Equal("ian", item.Name);
        Assert.Equal("ian/", item.Key);
        Assert.Equal("3", item.Properties["files"]);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal(temp.Path, location.RootPath);
        Assert.Equal("ian", location.RelativePath);
        Assert.DoesNotContain(index.Diagnostics, d => d.Path is { } p && p.StartsWith(ian, StringComparison.Ordinal) && p.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_advisory_folder_as_the_source_is_one_item()
    {
        using var temp = new TempDirectory();
        var ian = CopyIan(Path.Combine(temp.Path, "ian"));

        var index = await IndexAsync(Folder(ian));

        var item = Assert.Single(index.Items);
        Assert.Equal("TC", item.ProductSpec);
        Assert.Equal("ian/", item.Key);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal(ian, Path.Combine(location.RootPath, location.RelativePath));
    }

    [Fact]
    public async Task A_folder_of_an_advisory_a_grib2_run_and_an_s57_cell_gives_each_its_own_item()
    {
        // As the CLI's folder input after #1677: the run keeps the folder, the advisory takes its forecast zip.
        using var temp = new TempDirectory();
        CopyIan(temp.Path);
        File.Copy(TestPaths.Dataset("GRIB2", "gfswave-useast.grib2"), Path.Combine(temp.Path, "gfswave-useast.grib2"));
        File.Copy(TestPaths.Dataset("GRIB2", "gfswave-useast-filter.grib2"), Path.Combine(temp.Path, "gfswave-useast-filter.grib2"));
        File.Copy(TestPaths.Dataset("S57", "US5MA1BO", "US5MA1BO.000"), Path.Combine(temp.Path, "US5MA1BO.000"));

        var index = await IndexAsync(Folder(temp.Path));

        Assert.Equal(["GRIB2", "S-57", "TC"], index.Items.Select(i => i.ProductSpec).Order(StringComparer.Ordinal));
        var run = index.Items.Single(i => i.ProductSpec == "GRIB2");
        Assert.Equal(Path.GetFileName(temp.Path) + "/", run.Key);
        var advisory = index.Items.Single(i => i.ProductSpec == "TC");
        Assert.Equal(ForecastZip, advisory.Key);
        Assert.Equal(ForecastZip, Assert.IsType<LocalItemLocation>(advisory.Location).RelativePath);
        Assert.Equal("US5MA1BO.000", index.Items.Single(i => i.ProductSpec == "S-57").Key);

        // The wind radii and best track zips are the advisory's, not candidate exchange sets.
        Assert.DoesNotContain(index.Diagnostics, d => d.Path is { } p && p.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_exchange_set_zip_beside_an_advisory_indexes_as_before()
    {
        using var temp = new TempDirectory();
        CopyIan(temp.Path);
        File.Copy(TestPaths.Dataset("S101.zip"), Path.Combine(temp.Path, "S101.zip"));

        var alone = await IndexAsync(new ExchangeSetSource(Guid.NewGuid(), null, TestPaths.Dataset("S101.zip")));
        var index = await IndexAsync(Folder(temp.Path));

        Assert.Equal(19, alone.Items.Count);
        Assert.All(alone.Items, i => Assert.True(Assert.IsType<LocalItemLocation>(i.Location).IsZip));
        var cells = index.Items.Where(i => i.ProductSpec != "TC").ToArray();
        Assert.Equal(alone.Items.Select(i => (i.ProductSpec, i.Name, i.GroupKey)), cells.Select(i => (i.ProductSpec, i.Name, i.GroupKey)));
        Assert.Single(index.Items, i => i.ProductSpec == "TC");
    }

    [Fact]
    public async Task A_sibling_zip_changing_refreshes_the_advisory()
    {
        using var temp = new TempDirectory();
        var ian = CopyIan(Path.Combine(temp.Path, "ian"));
        var source = Folder(temp.Path);

        var first = await IndexAsync(source);
        Assert.Same(first, await IndexAsync(source, first));

        var radii = Path.Combine(ian, WindRadiiZip);
        File.SetLastWriteTimeUtc(radii, File.GetLastWriteTimeUtc(radii).AddMinutes(1));
        var second = await IndexAsync(source, first);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);

        // Without the best track, the advisory starts at its synoptic time rather than Ian's first past point.
        File.Delete(Path.Combine(ian, BestTrackZip));
        var third = await IndexAsync(source, second);
        Assert.NotEqual(second.Fingerprint, third.Fingerprint);
        Assert.True(
            DateTimeOffset.Parse(Assert.Single(third.Items).Properties["timeStart"], CultureInfo.InvariantCulture)
            > DateTimeOffset.Parse(Assert.Single(second.Items).Properties["timeStart"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_source_without_an_advisory_keeps_its_fingerprint()
    {
        // The fingerprint of a source with no zipped dataset is what it always was, so its index isn't rebuilt.
        using var temp = new TempDirectory();
        var cell = Path.Combine(temp.Path, "US5MA1BO.000");
        File.Copy(TestPaths.Dataset("S57", "US5MA1BO", "US5MA1BO.000"), cell);
        var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(cell, stamp);
        var expected = "local-v5:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"US5MA1BO.000|{new FileInfo(cell).Length}|{stamp.Ticks}"))));

        var index = await IndexAsync(Folder(temp.Path));

        Assert.Equal(expected, index.Fingerprint);
    }

    [Fact]
    public async Task A_zip_without_a_catalogue_or_a_product_is_reported_as_before()
    {
        using var temp = new TempDirectory();
        File.Copy(TestPaths.Dataset("TC", "al092022", BestTrackZip), Path.Combine(temp.Path, BestTrackZip));

        var index = await IndexAsync(Folder(temp.Path));

        Assert.Empty(index.Items);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Info && d.Path!.EndsWith(BestTrackZip, StringComparison.Ordinal));
    }
}
