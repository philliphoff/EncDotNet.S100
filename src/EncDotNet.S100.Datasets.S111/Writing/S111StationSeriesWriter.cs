using System.Globalization;
using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Datasets.S111.Writing;

/// <summary>
/// Encodes an S-111 station-series dataset as data coding format 8 (time
/// series at fixed stations, stationwise), in the S-111 Edition 2.0.0 HDF5
/// layout <see cref="S111DatasetReader"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// The result is an HDF5 tree, independent of any HDF5 library; write it
/// with an HDF5 backend such as <c>PureHdfWriter.Write</c> from
/// <c>EncDotNet.S100.Hdf5.PureHdf</c>.
/// </para>
/// <para>
/// The file has one <c>SurfaceCurrent.01</c> instance with a
/// <c>Group_NNN</c> per station, in the order given, each with a
/// <c>values</c> table of <c>surfaceCurrentSpeed</c> (knots) and
/// <c>surfaceCurrentDirection</c> (degrees true, toward which the current
/// flows), and the station positions in the instance's
/// <c>Positioning/geometryValues</c>. When
/// <see cref="S111StationSeriesDataset.SurfaceCurrentDepth"/> is given, the
/// root carries it as a height relative to the sea surface
/// (<c>depthTypeIndex</c> 2, <c>verticalCoordinateBase</c> 1): negative
/// below the surface.
/// </para>
/// </remarks>
public static class S111StationSeriesWriter
{
    /// <summary>The <c>productSpecification</c> written when the dataset doesn't declare one.</summary>
    public const string ProductSpecification = "INT.IHO.S-111.2.0";

    /// <summary>The S-111 fill value for <c>surfaceCurrentSpeed</c> and <c>surfaceCurrentDirection</c>.</summary>
    public const float FillValue = -9999.0f;

    private const string SpecReference = "S-111 Edition 2.0.0 §10.2";

    /// <summary>
    /// Encodes a dataset. Every station needs a uniform time step of whole
    /// seconds (at most 65,535) and as many speeds and directions as times.
    /// </summary>
    /// <param name="dataset">
    /// The dataset. <see cref="S111StationSeriesDataset.DataCodingFormat"/>
    /// must be 8, <see cref="S111StationSeriesDataset.IssueDate"/> set, and
    /// <see cref="S111StationSeriesDataset.HorizontalCRS"/> 4326 or unset.
    /// <see cref="S111StationSeriesDataset.AxisOrder"/>,
    /// <see cref="S111StationSeriesDataset.MinTime"/> and
    /// <see cref="S111StationSeriesDataset.MaxTime"/> are ignored, and so are
    /// stations' <see cref="SurfaceCurrentStation.SampleTimes"/>.
    /// </param>
    /// <returns>The file's root group.</returns>
    /// <exception cref="ArgumentException">The dataset can't be encoded; the message says why.</exception>
    public static Hdf5WriteGroup Encode(S111StationSeriesDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        Validate(dataset);

        var stations = dataset.Stations;
        var (south, west, north, east) = Bounds(stations);

        var root = new Hdf5WriteGroup()
            .SetAttribute("productSpecification", dataset.DeclaredProductSpecification ?? ProductSpecification)
            .SetAttribute("issueDate", dataset.IssueDate ?? string.Empty)
            .SetAttribute("horizontalCRS", 4326)
            .SetAttribute("westBoundLongitude", west)
            .SetAttribute("eastBoundLongitude", east)
            .SetAttribute("southBoundLatitude", south)
            .SetAttribute("northBoundLatitude", north);
        SetIfPresent(root, "issueTime", dataset.IssueTime);
        SetIfPresent(root, "geographicIdentifier", dataset.GeographicIdentifier);
        SetIfPresent(root, "epoch", dataset.Epoch);
        SetIfPresent(root, "metadata", dataset.Metadata);
        if (dataset.SurfaceCurrentDepth is { } depth)
        {
            root.SetEnumAttribute("depthTypeIndex", S111DepthTypeIndex.HeightOrDepth)
                .SetAttribute("surfaceCurrentDepth", depth)
                .SetAttribute("verticalCS", 6499)
                .SetEnumAttribute("verticalCoordinateBase", S111VerticalCoordinateBase.SeaSurface);
        }

        WriteFeatureInformation(root.Group("Group_F"));

        var (minSpeed, maxSpeed) = SpeedRange(stations);
        var surfaceCurrent = root.Group("SurfaceCurrent")
            .SetEnumAttribute("dataCodingFormat", S111DataCodingFormat.StationwiseFixed)
            .SetAttribute("dimension", (byte)2)
            .SetEnumAttribute("commonPointRule", S111CommonPointRule.All)
            .SetEnumAttribute("interpolationType", S111InterpolationType.Discrete)
            .SetAttribute("horizontalPositionUncertainty", -1.0f)
            .SetAttribute("verticalUncertainty", -1.0f)
            .SetAttribute("timeUncertainty", -1.0f)
            .SetAttribute("numInstances", 1u)
            .SetAttribute("minDatasetCurrentSpeed", minSpeed)
            .SetAttribute("maxDatasetCurrentSpeed", maxSpeed);
        if (dataset.TypeOfCurrentData is { } type)
        {
            surfaceCurrent.SetEnumAttribute("typeOfCurrentData", (S111TypeOfCurrentData)type);
        }

        SetIfPresent(surfaceCurrent, "methodCurrentsProduct", dataset.MethodCurrentsProduct);

        var instance = surfaceCurrent.Group("SurfaceCurrent.01")
            .SetAttribute("numberOfStations", (uint)stations.Count)
            .SetAttribute("numGRP", (uint)stations.Count)
            .SetAttribute("numberOfTimes", (uint)stations.Max(station => station.NumberOfTimes))
            .SetAttribute("westBoundLongitude", west)
            .SetAttribute("eastBoundLongitude", east)
            .SetAttribute("southBoundLatitude", south)
            .SetAttribute("northBoundLatitude", north)
            .SetAttribute("dateTimeOfFirstRecord", FormatDateTime(stations.Min(station => station.StartTime)))
            .SetAttribute("dateTimeOfLastRecord", FormatDateTime(stations.Max(station => station.EndTime)));
        if (stations.Select(station => station.TimeRecordInterval).Distinct().ToList() is [var commonInterval])
        {
            instance.SetAttribute("timeRecordInterval", (ushort)commonInterval.TotalSeconds);
        }

        var positions = new GeometryValue[stations.Count];
        for (int index = 0; index < stations.Count; index++)
        {
            var station = stations[index];
            positions[index] = new GeometryValue { Latitude = station.Latitude, Longitude = station.Longitude };

            var group = instance.Group($"Group_{index + 1:000}")
                .SetAttribute("stationIdentification", station.Identifier)
                .SetAttribute("startDateTime", FormatDateTime(station.StartTime))
                .SetAttribute("endDateTime", FormatDateTime(station.EndTime))
                .SetAttribute("numberOfTimes", (uint)station.NumberOfTimes)
                .SetAttribute("timeRecordInterval", (ushort)station.TimeRecordInterval.TotalSeconds);
            SetIfPresent(group, "stationName", station.Name);

            var values = new Value[station.NumberOfTimes];
            for (int time = 0; time < values.Length; time++)
            {
                values[time] = new Value
                {
                    SurfaceCurrentSpeed = station.SpeedsKnots[time],
                    SurfaceCurrentDirection = station.DirectionsDegreesTrue[time],
                };
            }

            group.SetDataset("values", values);
        }

        instance.Group("Positioning").SetDataset("geometryValues", positions);
        return root;
    }

    /// <summary>
    /// Formats a time as S-100 writes date-times in HDF5 attributes:
    /// <c>yyyyMMddTHHmmssZ</c>, in UTC.
    /// </summary>
    /// <param name="utc">The time, in UTC.</param>
    /// <returns>The text, such as <c>20260115T063000Z</c>.</returns>
    public static string FormatDateTime(DateTime utc) =>
        utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static void Validate(S111StationSeriesDataset dataset)
    {
        if (dataset.DataCodingFormat != 8)
        {
            throw Invalid($"Data coding format {dataset.DataCodingFormat} can't be written; only 8 (stationwise fixed stations) can.");
        }

        if (dataset.HorizontalCRS is { } crs && crs != 4326)
        {
            throw Invalid($"Horizontal CRS EPSG:{crs} can't be written; station positions are WGS 84 (EPSG:4326).");
        }

        if (string.IsNullOrEmpty(dataset.IssueDate))
        {
            throw Invalid("The dataset has no issue date.");
        }

        if (dataset.TypeOfCurrentData is { } type && !Enum.IsDefined((S111TypeOfCurrentData)type))
        {
            throw Invalid($"Type of current data {type} isn't an S-111 value (1 to 6).");
        }

        if (dataset.SurfaceCurrentDepth is { } depth && !float.IsFinite(depth))
        {
            throw Invalid("The surface current depth isn't a finite number.");
        }

        if (dataset.Stations.Count == 0)
        {
            throw Invalid("The dataset has no stations.");
        }

        foreach (var station in dataset.Stations)
        {
            ValidateStation(station);
        }
    }

    private static void ValidateStation(SurfaceCurrentStation station)
    {
        var id = station.Identifier;
        if (station.NumberOfTimes < 1 ||
            station.SpeedsKnots.Length != station.NumberOfTimes ||
            station.DirectionsDegreesTrue.Length != station.NumberOfTimes)
        {
            throw Invalid($"Station '{id}' needs as many speeds and directions as its {station.NumberOfTimes} times.");
        }

        var interval = station.TimeRecordInterval;
        if (interval < TimeSpan.Zero ||
            interval.Ticks % TimeSpan.TicksPerSecond != 0 ||
            interval.TotalSeconds > ushort.MaxValue ||
            (interval == TimeSpan.Zero && station.NumberOfTimes > 1))
        {
            throw Invalid($"Station '{id}' has a time step of {interval}; it needs a positive whole number of seconds, at most {ushort.MaxValue}.");
        }

        if (station.StartTime.Kind == DateTimeKind.Local || station.EndTime.Kind == DateTimeKind.Local)
        {
            throw Invalid($"Station '{id}' has local times; give them in UTC.");
        }

        if (station.EndTime != station.StartTime + (interval * (station.NumberOfTimes - 1)))
        {
            throw Invalid($"Station '{id}' ends at {station.EndTime:u}, not one step per time after {station.StartTime:u}.");
        }

        for (int index = 0; index < station.NumberOfTimes; index++)
        {
            var speed = station.SpeedsKnots[index];
            var direction = station.DirectionsDegreesTrue[index];
            if (speed == FillValue && direction == FillValue)
            {
                continue;
            }

            if (!float.IsFinite(speed) || speed < 0 || !float.IsFinite(direction) || direction < 0 || direction >= 360)
            {
                throw Invalid($"Station '{id}' has a speed below 0 or a direction outside 0 to 360 degrees at time {index}.");
            }
        }

        if (!double.IsFinite(station.Latitude) || Math.Abs(station.Latitude) > 90 ||
            !double.IsFinite(station.Longitude) || Math.Abs(station.Longitude) > 180)
        {
            throw Invalid($"Station '{id}' has no valid position.");
        }
    }

    private static void WriteFeatureInformation(Hdf5WriteGroup groupF)
    {
        FeatureAttributeRow[] rows =
        [
            new()
            {
                Code = "surfaceCurrentSpeed",
                Name = "Surface Current Speed",
                UnitOfMeasure = "knot",
                FillValue = FillValue.ToString("0.00", CultureInfo.InvariantCulture),
                Datatype = "H5T_FLOAT",
                Lower = "0.00",
                Upper = string.Empty,
                Closure = "geSemiInterval",
            },
            new()
            {
                Code = "surfaceCurrentDirection",
                Name = "Surface Current Direction",
                UnitOfMeasure = "arc-degree",
                FillValue = FillValue.ToString("0.0", CultureInfo.InvariantCulture),
                Datatype = "H5T_FLOAT",
                Lower = "0.0",
                Upper = "359.9",
                Closure = "closedInterval",
            },
        ];

        groupF
            .SetDataset("featureCode", ["SurfaceCurrent"])
            .SetDataset("SurfaceCurrent", rows);
    }

    // The bounds are single precision; round them outwards so every
    // station's double-precision position stays inside them.
    private static (float South, float West, float North, float East) Bounds(IReadOnlyList<SurfaceCurrentStation> stations) => (
        Down(stations.Min(station => station.Latitude)),
        Down(stations.Min(station => station.Longitude)),
        Up(stations.Max(station => station.Latitude)),
        Up(stations.Max(station => station.Longitude)));

    private static float Down(double value) => (float)value > value ? MathF.BitDecrement((float)value) : (float)value;

    private static float Up(double value) => (float)value < value ? MathF.BitIncrement((float)value) : (float)value;

    private static (float Min, float Max) SpeedRange(IReadOnlyList<SurfaceCurrentStation> stations)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (var speed in stations.SelectMany(station => station.SpeedsKnots))
        {
            if (speed == FillValue || !float.IsFinite(speed))
            {
                continue;
            }

            min = Math.Min(min, speed);
            max = Math.Max(max, speed);
        }

        return min <= max ? (min, max) : (FillValue, FillValue);
    }

    private static void SetIfPresent(Hdf5WriteGroup group, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            group.SetAttribute(name, value);
        }
    }

    private static ArgumentException Invalid(string message) =>
        new($"{message} ({SpecReference})", "dataset");

    private struct GeometryValue
    {
        [Hdf5Member("latitude")] public double Latitude;
        [Hdf5Member("longitude")] public double Longitude;
    }

    private struct Value
    {
        [Hdf5Member("surfaceCurrentSpeed")] public float SurfaceCurrentSpeed;
        [Hdf5Member("surfaceCurrentDirection")] public float SurfaceCurrentDirection;
    }

    private struct FeatureAttributeRow
    {
        [Hdf5Member("code")] public string Code;
        [Hdf5Member("name")] public string Name;
        [Hdf5Member("uom.name")] public string UnitOfMeasure;
        [Hdf5Member("fillValue")] public string FillValue;
        [Hdf5Member("datatype")] public string Datatype;
        [Hdf5Member("lower")] public string Lower;
        [Hdf5Member("upper")] public string Upper;
        [Hdf5Member("closure")] public string Closure;
    }
}
