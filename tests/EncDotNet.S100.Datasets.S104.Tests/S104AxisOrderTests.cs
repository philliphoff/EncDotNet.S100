using EncDotNet.S100.DataModel;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Testing.Scenarios.Hdf5;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// The S-104 reader runs the shared axis-order check (issue #650) on station
/// positions and grid origins against the declared bounding box. Fixtures are
/// shaped like the UKHO Solent trial file
/// <c>104UK_20210401T000000Z_SolentAndAppr_dcf8.hdf5</c>, whose
/// <c>latitude</c> member holds longitudes and vice versa.
/// </summary>
public class S104AxisOrderTests
{
    private static readonly Dictionary<string, object> SolentBox = new()
    {
        ["westBoundLongitude"] = -2.5,
        ["eastBoundLongitude"] = 0.0,
        ["southBoundLatitude"] = 50.0,
        ["northBoundLatitude"] = 51.0,
    };

    private static readonly (float Lat, float Lon)[] SolentStations =
    [
        (50.5667f, -2.4667f),
        (50.7667f, -1.3f),
        (50.9167f, -0.1f),
    ];

    [Fact]
    public void Dcf8_swapped_station_positions_are_swapped_back()
    {
        var model = ReadDcf8(SolentStations.Select(s => (s.Lon, s.Lat)), SolentBox);

        Assert.Equal(AxisOrderOutcome.Swapped, model.AxisOrder!.Outcome);
        Assert.True(model.AxisOrder.Normalized);
        Assert.Equal("/Positioning/geometryValues", model.AxisOrder.Source);
        Assert.Equal(50.7667, model.Stations[1].Latitude, 3);
        Assert.Equal(-1.3, model.Stations[1].Longitude, 3);
    }

    [Fact]
    public void Dcf8_conformant_station_positions_are_verified_and_unchanged()
    {
        var model = ReadDcf8(SolentStations, SolentBox);

        Assert.Equal(AxisOrderOutcome.AsEncoded, model.AxisOrder!.Outcome);
        Assert.Equal(50.7667, model.Stations[1].Latitude, 3);
        Assert.Equal(-1.3, model.Stations[1].Longitude, 3);
    }

    [Fact]
    public void Dcf8_without_a_bounding_box_is_unverified_and_kept_as_encoded()
    {
        var model = ReadDcf8(SolentStations.Select(s => (s.Lon, s.Lat)), extra: null);

        Assert.Equal(AxisOrderOutcome.Unverified, model.AxisOrder!.Outcome);
        Assert.Equal(-1.3, model.Stations[1].Latitude, 3);
    }

    [Fact]
    public void Dcf8_metadata_extent_uses_the_normalised_positions()
    {
        var path = WriteDcf8(SolentStations.Select(s => (s.Lon, s.Lat)), SolentBox);
        try
        {
            using var file = PureHdfFile.Open(path);
            var extent = S104DatasetReader.ReadMetadata(file).Extent!;

            Assert.InRange(extent.SouthLatitude, 50.0, 51.0);
            Assert.InRange(extent.WestLongitude, -2.5, 0.0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dcf1_swapped_station_positions_are_swapped_back()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.h5");
        try
        {
            S104Dcf1FixtureBuilder.WriteFile(
                path,
                [.. SolentStations.Select(s => new S104Dcf1FixtureBuilder.Station { Latitude = s.Lon, Longitude = s.Lat })],
                [
                    new()
                    {
                        TimePoint = "20240101T000000Z",
                        Values = [.. SolentStations.Select(_ => new S104Dcf1FixtureBuilder.ValueRow { WaterLevelHeight = 1f, WaterLevelTrend = 1 })],
                    },
                ],
                extraRootAttributes: SolentBox);

            using var file = PureHdfFile.Open(path);
            var model = Assert.IsType<S104DatasetData.StationSeries>(S104DatasetReader.ReadAny(file)).Dataset;

            Assert.Equal(AxisOrderOutcome.Swapped, model.AxisOrder!.Outcome);
            Assert.Equal(50.5667, model.Stations[0].Latitude, 3);
            Assert.Equal(-2.4667, model.Stations[0].Longitude, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dcf2_grid_footprint_outside_the_box_is_reported_not_rewritten()
    {
        // Grid at lat 50, lon −1 (fixture default) against a box written
        // longitude first: the swapped reading fits, but grid georeferencing
        // is reported only.
        var lonFirstBox = new Dictionary<string, object>
        {
            ["westBoundLongitude"] = 49.9,
            ["eastBoundLongitude"] = 50.2,
            ["southBoundLatitude"] = -1.1,
            ["northBoundLatitude"] = -0.8,
        };
        var dataset = ReadGrid(lonFirstBox);

        Assert.Equal(AxisOrderOutcome.Swapped, dataset.AxisOrder!.Outcome);
        Assert.False(dataset.AxisOrder.Normalized);
        Assert.Equal(50.0, dataset.Coverages[0].OriginLatitude, 6);
    }

    [Fact]
    public void Dcf2_grid_inside_its_box_is_verified()
    {
        var box = new Dictionary<string, object>
        {
            ["westBoundLongitude"] = -1.1,
            ["eastBoundLongitude"] = -0.8,
            ["southBoundLatitude"] = 49.9,
            ["northBoundLatitude"] = 50.2,
        };

        Assert.Equal(AxisOrderOutcome.AsEncoded, ReadGrid(box).AxisOrder!.Outcome);
    }

    private static S104StationSeriesDataset ReadDcf8(
        IEnumerable<(float Lat, float Lon)> encoded,
        IReadOnlyDictionary<string, object>? extra)
    {
        var path = WriteDcf8(encoded, extra);
        try
        {
            using var file = PureHdfFile.Open(path);
            return Assert.IsType<S104DatasetData.StationSeries>(S104DatasetReader.ReadAny(file)).Dataset;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteDcf8(
        IEnumerable<(float Lat, float Lon)> encoded,
        IReadOnlyDictionary<string, object>? extra)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.h5");
        var stations = encoded.Select((p, i) => new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
        {
            Identifier = $"S{i}",
            Latitude = p.Lat,
            Longitude = p.Lon,
            StartDateTime = "20240101T000000Z",
            EndDateTime = "20240101T010000Z",
            TimeRecordInterval = 3600,
            Values =
            [
                new() { WaterLevelHeight = 1f, WaterLevelTrend = 1 },
                new() { WaterLevelHeight = 2f, WaterLevelTrend = 1 },
            ],
        }).ToList();
        S104Dcf8FixtureBuilder.WriteFile(path, stations, extraRootAttributes: extra);
        return path;
    }

    private static S104Dataset ReadGrid(IReadOnlyDictionary<string, object> box)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.h5");
        try
        {
            S104FixtureBuilder.WriteFile(
                path,
                new S104FixtureBuilder.SpecRow[4],
                numLat: 2,
                numLon: 2,
                useF64GridAttrs: true,
                useUnsignedCounts: false,
                extraRootAttributes: box);

            using var file = PureHdfFile.Open(path);
            return S104DatasetReader.Read(file);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
