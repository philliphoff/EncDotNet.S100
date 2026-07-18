using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services.Caching;

namespace EncDotNet.S100.Viewer.Tests;

public class S57CatalogCacheSerializerTests
{
    private static S57ExchangeSetCell Cell(
        string name,
        string relative,
        IReadOnlyList<string> updates,
        BoundingBox? box) => new()
        {
            CellName = name,
            RelativePath = relative,
            UpdateRelativePaths = updates,
            BoundingBox = box,
        };

    [Fact]
    public void Round_trips_cells_with_updates_and_bbox()
    {
        var cells = new List<S57ExchangeSetCell>
        {
            Cell("US5MA1BO", "ENC_ROOT/US5MA1BO.000",
                new[] { "ENC_ROOT/US5MA1BO.001", "ENC_ROOT/US5MA1BO.002" },
                new BoundingBox
                {
                    WestBoundLongitude = -71.5,
                    EastBoundLongitude = -70.5,
                    SouthBoundLatitude = 41.0,
                    NorthBoundLatitude = 42.0,
                }),
            Cell("GB4X0000", "ENC_ROOT/GB4X0000.000", Array.Empty<string>(), null),
        };

        var round = S57CatalogCacheSerializer.TryDeserialize(
            S57CatalogCacheSerializer.Serialize(cells));

        Assert.NotNull(round);
        Assert.Equal(2, round!.Count);

        Assert.Equal("US5MA1BO", round[0].CellName);
        Assert.Equal("ENC_ROOT/US5MA1BO.000", round[0].RelativePath);
        Assert.Equal(2, round[0].UpdateRelativePaths.Count);
        Assert.Equal("ENC_ROOT/US5MA1BO.002", round[0].UpdateRelativePaths[1]);
        Assert.NotNull(round[0].BoundingBox);
        Assert.Equal(-71.5, round[0].BoundingBox!.WestBoundLongitude);
        Assert.Equal(42.0, round[0].BoundingBox!.NorthBoundLatitude);

        Assert.Empty(round[1].UpdateRelativePaths);
        Assert.Null(round[1].BoundingBox);
    }

    [Fact]
    public void Round_trips_empty_list()
    {
        var round = S57CatalogCacheSerializer.TryDeserialize(
            S57CatalogCacheSerializer.Serialize(Array.Empty<S57ExchangeSetCell>()));

        Assert.NotNull(round);
        Assert.Empty(round!);
    }

    [Fact]
    public void Truncated_frame_deserializes_to_null()
    {
        var full = S57CatalogCacheSerializer.Serialize(new[]
        {
            Cell("US5MA1BO", "ENC_ROOT/US5MA1BO.000", Array.Empty<string>(), null),
        });

        var truncated = full[..(full.Length / 2)];

        Assert.Null(S57CatalogCacheSerializer.TryDeserialize(truncated));
    }

    [Fact]
    public void Version_mismatch_deserializes_to_null()
    {
        var bytes = S57CatalogCacheSerializer.Serialize(new[]
        {
            Cell("US5MA1BO", "ENC_ROOT/US5MA1BO.000", Array.Empty<string>(), null),
        });

        // Corrupt the leading FormatVersion int.
        bytes[0] = 0xFF;

        Assert.Null(S57CatalogCacheSerializer.TryDeserialize(bytes));
    }
}
