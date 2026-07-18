using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services.Caching;

namespace EncDotNet.S100.Viewer.Tests;

public class DiskS57CatalogCacheTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string _cataloguePath;

    public DiskS57CatalogCacheTests()
    {
        _cacheDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "s57cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);

        _cataloguePath = System.IO.Path.Combine(_cacheDir, "CATALOG.031");
        File.WriteAllBytes(_cataloguePath, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private static IReadOnlyList<S57ExchangeSetCell> SampleCells() => new[]
    {
        new S57ExchangeSetCell
        {
            CellName = "US5MA1BO",
            RelativePath = "US5MA1BO.000",
            UpdateRelativePaths = new[] { "US5MA1BO.001" },
            BoundingBox = new BoundingBox
            {
                WestBoundLongitude = -71.0,
                EastBoundLongitude = -70.0,
                SouthBoundLatitude = 41.0,
                NorthBoundLatitude = 42.0,
            },
        },
    };

    [Fact]
    public void Second_read_is_a_hit_and_skips_the_producer()
    {
        var cache = new DiskS57CatalogCache(_cacheDir, 1_000_000);
        var calls = 0;

        IReadOnlyList<S57ExchangeSetCell> Produce(string _)
        {
            calls++;
            return SampleCells();
        }

        var first = cache.GetOrRead(_cataloguePath, Produce);
        var second = cache.GetOrRead(_cataloguePath, Produce);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
        Assert.Equal("US5MA1BO", second[0].CellName);
        Assert.Equal("US5MA1BO.001", second[0].UpdateRelativePaths[0]);
    }

    [Fact]
    public void Catalogue_change_invalidates_the_entry()
    {
        var cache = new DiskS57CatalogCache(_cacheDir, 1_000_000);
        var calls = 0;

        IReadOnlyList<S57ExchangeSetCell> Produce(string _)
        {
            calls++;
            return SampleCells();
        }

        cache.GetOrRead(_cataloguePath, Produce);

        // Rewrite the catalogue with different content + length so the
        // recorded mtime/size no longer match.
        File.WriteAllBytes(_cataloguePath, new byte[] { 9, 8, 7, 6, 5 });

        cache.GetOrRead(_cataloguePath, Produce);

        Assert.Equal(2, calls);
        Assert.Equal(2, cache.Misses);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void Corrupt_sidecar_is_treated_as_a_miss()
    {
        var cache = new DiskS57CatalogCache(_cacheDir, 1_000_000);
        cache.GetOrRead(_cataloguePath, _ => SampleCells());

        foreach (var sidecar in Directory.EnumerateFiles(_cacheDir, "*.s57cat"))
            File.WriteAllBytes(sidecar, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var calls = 0;
        var result = cache.GetOrRead(_cataloguePath, _ =>
        {
            calls++;
            return SampleCells();
        });

        Assert.Single(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Missing_catalogue_runs_producer_without_persisting()
    {
        var cache = new DiskS57CatalogCache(_cacheDir, 1_000_000);
        var missing = System.IO.Path.Combine(_cacheDir, "GONE.031");

        var result = cache.GetOrRead(missing, _ => SampleCells());

        Assert.Single(result);
        Assert.Empty(Directory.GetFiles(_cacheDir, "*.s57cat"));
    }
}
