using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>A regular geographic grid laid over a local rectangle.</summary>
/// <param name="OriginLatitude">Latitude of the south-west node.</param>
/// <param name="OriginLongitude">Longitude of the south-west node.</param>
/// <param name="SpacingLatitudinal">Node spacing in degrees of latitude.</param>
/// <param name="SpacingLongitudinal">Node spacing in degrees of longitude.</param>
/// <param name="Rows">Nodes north–south.</param>
/// <param name="Columns">Nodes east–west.</param>
public sealed record GeoGrid(double OriginLatitude, double OriginLongitude, double SpacingLatitudinal, double SpacingLongitudinal, int Rows, int Columns)
{
    /// <summary>
    /// The grid whose south-west node is the rectangle's south-west corner,
    /// with nodes <paramref name="spacing"/> metres apart as far as the
    /// rectangle reaches.
    /// </summary>
    /// <param name="frame">The world frame.</param>
    /// <param name="extent">The rectangle.</param>
    /// <param name="spacing">The node spacing in metres.</param>
    /// <returns>The grid.</returns>
    public static GeoGrid Over(WorldFrame frame, LocalRect extent, double spacing)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var origin = frame.ToGeo(extent.West, extent.South);
        return new GeoGrid(
            origin.Latitude,
            origin.Longitude,
            spacing / WorldFrame.MetresPerDegreeLatitude,
            spacing / frame.MetresPerDegreeLongitude,
            (int)Math.Floor(extent.Height / spacing) + 1,
            (int)Math.Floor(extent.Width / spacing) + 1);
    }

    /// <summary>The position of a node.</summary>
    /// <param name="row">Row (south to north).</param>
    /// <param name="column">Column (west to east).</param>
    /// <returns>The position.</returns>
    public GeoPosition Node(int row, int column) =>
        new(OriginLatitude + row * SpacingLatitudinal, OriginLongitude + column * SpacingLongitudinal);

    /// <summary>The northern node row's latitude.</summary>
    public double NorthLatitude => OriginLatitude + (Rows - 1) * SpacingLatitudinal;

    /// <summary>The eastern node column's longitude.</summary>
    public double EastLongitude => OriginLongitude + (Columns - 1) * SpacingLongitudinal;
}

/// <summary>An S-102 bathymetric surface.</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="Extent">The rectangle the grid covers.</param>
/// <param name="Spacing">The node spacing in metres.</param>
/// <param name="DeclareVerticalDatum">
/// Whether the root carries <c>verticalDatum</c>. S-102 Ed 3.0.0 requires
/// it; leaving it out reproduces producers that don't (#939).
/// </param>
public sealed record S102SurfaceSpec(string DatasetName, LocalRect Extent, double Spacing, bool DeclareVerticalDatum = true)
{
    /// <summary>The file name, with the <c>.h5</c> extension.</summary>
    public string FileName => DatasetName + ".h5";
}

/// <summary>S-104 station series (dcf8) at the world's tide stations.</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="Interval">The time between records.</param>
public sealed record S104StationsSpec(string DatasetName, TimeSpan Interval)
{
    /// <summary>The file name, with the <c>.h5</c> extension.</summary>
    public string FileName => DatasetName + ".h5";
}

/// <summary>An S-104 regular grid (dcf2).</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="Extent">The rectangle the grid covers.</param>
/// <param name="Spacing">The node spacing in metres.</param>
/// <param name="Interval">The time between steps.</param>
public sealed record S104GridSpec(string DatasetName, LocalRect Extent, double Spacing, TimeSpan Interval)
{
    /// <summary>The file name, with the <c>.h5</c> extension.</summary>
    public string FileName => DatasetName + ".h5";
}

/// <summary>Samples the world into S-102 and S-104 HDF5 files.</summary>
/// <remarks>
/// <para>
/// S-102 node values are the true depth at the node (as <c>float</c>), with an
/// uncertainty of 0,25 m + 1 % of depth; nodes on land are the fill value.
/// </para>
/// <para>
/// S-104 values are the true water level at the station or node at each
/// record time (as <c>float</c>), with the trend from the tide's rate of
/// change against the dataset's <c>waterLevelTrendThreshold</c>.
/// </para>
/// </remarks>
public static class CoverageWriters
{
    /// <summary>The S-104 trend threshold written to each dataset, in metres per hour.</summary>
    public const double TrendThreshold = 0.2;

    /// <summary>The S-102 grid for a surface.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The surface.</param>
    /// <returns>The grid.</returns>
    public static GeoGrid Grid(SyntheticWorld world, S102SurfaceSpec spec) => GeoGrid.Over(world.Frame, spec.Extent, spec.Spacing);

    /// <summary>The S-104 grid for a water-level grid.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The water-level grid.</param>
    /// <returns>The grid.</returns>
    public static GeoGrid Grid(SyntheticWorld world, S104GridSpec spec) => GeoGrid.Over(world.Frame, spec.Extent, spec.Spacing);

    /// <summary>The value written at an S-102 node: the true depth, or the fill value on land.</summary>
    /// <param name="world">The world.</param>
    /// <param name="node">The node's position.</param>
    /// <returns>The depth.</returns>
    public static float S102Depth(SyntheticWorld world, GeoPosition node)
    {
        var depth = world.DepthAt(node);
        return depth <= -world.HighWaterHeight ? S102FileBuilder.FillValue : (float)depth;
    }

    /// <summary>Writes an S-102 surface.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The surface.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="issued">The issue time (UTC).</param>
    public static void WriteS102(SyntheticWorld world, S102SurfaceSpec spec, string path, DateTime issued)
    {
        var grid = Grid(world, spec);
        var values = new S102FileBuilder.BathymetryRow[grid.Rows * grid.Columns];
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var col = 0; col < grid.Columns; col++)
            {
                var depth = S102Depth(world, grid.Node(row, col));
                values[row * grid.Columns + col] = new S102FileBuilder.BathymetryRow
                {
                    Depth = depth,
                    Uncertainty = depth == S102FileBuilder.FillValue ? S102FileBuilder.FillValue : (float)Math.Round(0.25 + 0.01 * Math.Abs(depth), 2),
                };
            }
        }

        var root = new Dictionary<string, object>
        {
            ["productSpecification"] = "INT.IHO.S-102.3.0.0",
            ["issueDate"] = issued.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["issueTime"] = issued.ToString("HHmmss'Z'", CultureInfo.InvariantCulture),
            ["horizontalCRS"] = 4326,
            ["westBoundLongitude"] = grid.OriginLongitude,
            ["eastBoundLongitude"] = grid.EastLongitude,
            ["southBoundLatitude"] = grid.OriginLatitude,
            ["northBoundLatitude"] = grid.NorthLatitude,
            ["geographicIdentifier"] = $"{world.Name} (fictitious, synthetic test data)",
            ["verticalCS"] = 6498,
            ["verticalCoordinateBase"] = (byte)2,
            ["verticalDatumReference"] = (byte)1,
        };
        if (spec.DeclareVerticalDatum)
        {
            root["verticalDatum"] = world.Datums.ChartDatum;
        }

        S102FileBuilder.Write(
            path,
            new S102FileBuilder.GridGeoreference(grid.OriginLatitude, grid.OriginLongitude, grid.SpacingLatitudinal, grid.SpacingLongitudinal, grid.Rows, grid.Columns),
            values,
            root);
    }

    /// <summary>The record times from <paramref name="start"/> to <paramref name="end"/> inclusive.</summary>
    /// <param name="start">The first record (UTC).</param>
    /// <param name="end">The last record (UTC).</param>
    /// <param name="interval">The time between records.</param>
    /// <returns>The times.</returns>
    public static IReadOnlyList<DateTime> Times(DateTime start, DateTime end, TimeSpan interval)
    {
        var times = new List<DateTime>();
        for (var t = start; t <= end; t += interval)
        {
            times.Add(t);
        }

        return times;
    }

    /// <summary>Writes the S-104 station series.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The station dataset.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="issued">The issue time (UTC).</param>
    /// <param name="start">The first record (UTC).</param>
    /// <param name="end">The last record (UTC).</param>
    public static void WriteS104Stations(SyntheticWorld world, S104StationsSpec spec, string path, DateTime issued, DateTime start, DateTime end)
    {
        var times = Times(start, end, spec.Interval);
        var stations = new List<S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>>();
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        foreach (var station in world.Tide.Stations)
        {
            var position = world.Frame.ToGeo(station.X, station.Y);
            south = Math.Min(south, position.Latitude);
            north = Math.Max(north, position.Latitude);
            west = Math.Min(west, position.Longitude);
            east = Math.Max(east, position.Longitude);
            stations.Add(new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
            {
                Identifier = station.Identifier,
                Latitude = (float)position.Latitude,
                Longitude = (float)position.Longitude,
                StartDateTime = TimePoint(start),
                EndDateTime = TimePoint(end),
                TimeRecordInterval = (long)spec.Interval.TotalSeconds,
                Values = times.Select(t => new S104Dcf8FixtureBuilder.SpecValueRow
                {
                    WaterLevelHeight = (float)world.Tide.HeightAt(station.X, station.Y, t),
                    WaterLevelTrend = Trend(world.Tide.RateAt(station.X, station.Y, t)),
                }).ToArray(),
            });
        }

        // Pad the declared extent by a float's worth so the stations'
        // single-precision positions stay inside it.
        const double pad = 1e-4;
        var root = S104Root(world, issued, west - pad, south - pad, east + pad, north + pad);
        S104Dcf8FixtureBuilder.WriteFile(
            path,
            stations,
            extraRootAttributes: root);
    }

    /// <summary>Writes an S-104 grid.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The grid dataset.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="issued">The issue time (UTC).</param>
    /// <param name="start">The first step (UTC).</param>
    /// <param name="end">The last step (UTC).</param>
    public static void WriteS104Grid(SyntheticWorld world, S104GridSpec spec, string path, DateTime issued, DateTime start, DateTime end)
    {
        var grid = Grid(world, spec);
        var steps = new List<(string, S104FixtureBuilder.SpecRow[])>();
        foreach (var t in Times(start, end, spec.Interval))
        {
            var values = new S104FixtureBuilder.SpecRow[grid.Rows * grid.Columns];
            for (var row = 0; row < grid.Rows; row++)
            {
                for (var col = 0; col < grid.Columns; col++)
                {
                    var node = grid.Node(row, col);
                    var (x, y) = world.Frame.ToLocal(node.Latitude, node.Longitude);
                    values[row * grid.Columns + col] = new S104FixtureBuilder.SpecRow
                    {
                        WaterLevelHeight = (float)world.Tide.HeightAt(x, y, t),
                        WaterLevelTrend = Trend(world.Tide.RateAt(x, y, t)),
                    };
                }
            }

            steps.Add((TimePoint(t), values));
        }

        S104FixtureBuilder.WriteTimeSeries(
            path,
            new S104FixtureBuilder.GridGeoreference(grid.OriginLatitude, grid.OriginLongitude, grid.SpacingLatitudinal, grid.SpacingLongitudinal, grid.Rows, grid.Columns),
            steps,
            S104Root(world, issued, grid.OriginLongitude, grid.OriginLatitude, grid.EastLongitude, grid.NorthLatitude),
            featureAttributes: new Dictionary<string, object>
            {
                ["methodWaterLevelProduct"] = "Synthetic harmonic prediction (M2, S2)",
            },
            instanceAttributes: new Dictionary<string, object>
            {
                ["dateTimeOfFirstRecord"] = TimePoint(start),
                ["dateTimeOfLastRecord"] = TimePoint(end),
                ["timeRecordInterval"] = (long)spec.Interval.TotalSeconds,
            });
    }

    /// <summary>The S-104 time format, such as <c>20260301T000000Z</c>.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The text.</returns>
    public static string TimePoint(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // S-104 Ed 2.0.0 waterLevelTrend: 1 decreasing, 2 increasing, 3 steady.
    private static byte Trend(double metresPerHour) =>
        metresPerHour > TrendThreshold ? (byte)2 : metresPerHour < -TrendThreshold ? (byte)1 : (byte)3;

    private static Dictionary<string, object> S104Root(SyntheticWorld world, DateTime issued, double west, double south, double east, double north) => new()
    {
        ["productSpecification"] = "INT.IHO.S-104.2.0",
        ["issueDate"] = issued.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        ["issueTime"] = issued.ToString("HHmmss'Z'", CultureInfo.InvariantCulture),
        ["horizontalCRS"] = 4326,
        ["westBoundLongitude"] = west,
        ["eastBoundLongitude"] = east,
        ["southBoundLatitude"] = south,
        ["northBoundLatitude"] = north,
        ["geographicIdentifier"] = $"{world.Name} (fictitious, synthetic test data)",
        ["verticalCS"] = 6499,
        ["verticalCoordinateBase"] = (byte)2,
        ["verticalDatumReference"] = (byte)1,
        ["verticalDatum"] = world.Datums.WaterLevelDatum,
        ["waterLevelTrendThreshold"] = TrendThreshold,
    };
}
