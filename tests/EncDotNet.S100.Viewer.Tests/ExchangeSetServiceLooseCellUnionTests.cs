using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class ExchangeSetServiceLooseCellUnionTests
{
    /// <summary>
    /// Fake reader returning a fixed extent per path, recording the paths it
    /// was asked to read so a test can assert probe behaviour.
    /// </summary>
    private sealed class FakeReader : IDatasetMetadataReader
    {
        private readonly Dictionary<string, EncDotNet.S100.Pipelines.BoundingBox?> _extents;

        public FakeReader(Dictionary<string, EncDotNet.S100.Pipelines.BoundingBox?> extents)
            => _extents = extents;

        public List<string> Reads { get; } = new();

        public DatasetMetadata? TryRead(string path)
        {
            Reads.Add(path);
            if (!_extents.TryGetValue(path, out var extent))
                return null;

            return new DatasetMetadata
            {
                Spec = new SpecRef("S-101", new SpecVersion(1, 2, 0)),
                Extent = extent,
            };
        }
    }

    private static EncDotNet.S100.Pipelines.BoundingBox Geo(double s, double w, double n, double e)
        => new(s, w, n, e);

    [Fact]
    public void Unions_probeable_cell_extents()
    {
        var reader = new FakeReader(new()
        {
            ["a.000"] = Geo(0, -10, 5, -5),
            ["b.000"] = Geo(30, 10, 40, 20),
        });

        var union = ExchangeSetService.ComputeLooseCellUnion(["a.000", "b.000"], reader);

        Assert.NotNull(union);
        Assert.Equal(-10, union!.WestBoundLongitude);
        Assert.Equal(20, union.EastBoundLongitude);
        Assert.Equal(0, union.SouthBoundLatitude);
        Assert.Equal(40, union.NorthBoundLatitude);
    }

    [Fact]
    public void Skips_cells_without_an_extent()
    {
        var reader = new FakeReader(new()
        {
            ["a.000"] = null,
            ["b.000"] = Geo(0, 1, 2, 3),
            ["c.000"] = null,
        });

        var union = ExchangeSetService.ComputeLooseCellUnion(["a.000", "b.000", "c.000"], reader);

        Assert.NotNull(union);
        Assert.Equal(1, union!.WestBoundLongitude);
        Assert.Equal(3, union.EastBoundLongitude);
        Assert.Equal(0, union.SouthBoundLatitude);
        Assert.Equal(2, union.NorthBoundLatitude);
    }

    [Fact]
    public void Returns_null_when_no_cell_yields_an_extent()
    {
        var reader = new FakeReader(new()
        {
            ["a.000"] = null,
        });

        var union = ExchangeSetService.ComputeLooseCellUnion(["a.000", "unknown.000"], reader);

        Assert.Null(union);
    }
}
