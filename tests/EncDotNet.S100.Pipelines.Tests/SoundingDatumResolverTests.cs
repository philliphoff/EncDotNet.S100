using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A vector cell's sounding datum comes from its <c>SoundingDatum</c> meta
/// features' <c>verticalDatum</c> (S-101 DCEG §3.9), not from
/// <c>VerticalDatumOfData</c>.
/// </summary>
public sealed class SoundingDatumResolverTests
{
    private static Feature Area(string type, double south, double west, double north, double east, object? datum, long id = 1) => new()
    {
        FeatureType = type,
        Id = id,
        GeometryType = GeometryType.Surface,
        Coordinates =
        [
            new GeoPosition(south, west),
            new GeoPosition(south, east),
            new GeoPosition(north, east),
            new GeoPosition(north, west),
            new GeoPosition(south, west),
        ],
        Attributes = new Dictionary<string, object?> { ["verticalDatum"] = datum },
    };

    [Fact]
    public void Area_containing_the_point_wins()
    {
        var resolver = new SoundingDatumResolver(
        [
            Area("SoundingDatum", 0, 0, 1, 1, "12", 1),
            Area("SoundingDatum", 0, 1, 1, 2, 23, 2),
        ]);

        Assert.Equal(12, resolver.At(0.5, 0.5));
        Assert.Equal(23, resolver.At(0.5, 1.5));
    }

    [Fact]
    public void A_single_cell_datum_is_the_default_off_its_areas()
    {
        var resolver = new SoundingDatumResolver([Area("SoundingDatum", 0, 0, 1, 1, "23")]);

        Assert.Equal(23, resolver.At(5, 5));
    }

    [Fact]
    public void Two_datums_leave_points_outside_both_undeclared()
    {
        var resolver = new SoundingDatumResolver(
        [
            Area("SoundingDatum", 0, 0, 1, 1, "12", 1),
            Area("SoundingDatum", 0, 1, 1, 2, "23", 2),
        ]);

        Assert.Null(resolver.At(5, 5));
    }

    [Fact]
    public void Vertical_datum_of_data_is_not_a_sounding_datum()
    {
        var resolver = new SoundingDatumResolver([Area("VerticalDatumOfData", 0, 0, 1, 1, "3")]);

        Assert.Null(resolver.At(0.5, 0.5));
    }

    [Fact]
    public void Missing_or_unparsable_datum_is_ignored()
    {
        var resolver = new SoundingDatumResolver(
        [
            Area("SoundingDatum", 0, 0, 1, 1, null, 1),
            Area("SoundingDatum", 0, 0, 1, 1, "x", 2),
        ]);

        Assert.Null(resolver.At(0.5, 0.5));
    }
}
