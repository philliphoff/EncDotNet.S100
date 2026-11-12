using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Datasets.S104.Writing;

/// <summary>
/// Encodes an S-104 station-series dataset as data coding format 8 (time
/// series at fixed stations, stationwise), in the S-104 Edition 2.0.0 HDF5
/// layout <see cref="S104DatasetReader"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// The result is an HDF5 tree, independent of any HDF5 library; write it
/// with an HDF5 backend such as <c>PureHdfWriter.Write</c> from
/// <c>EncDotNet.S100.Hdf5.PureHdf</c>.
/// </para>
/// <para>
/// The file has one <c>WaterLevel.01</c> instance with a <c>Group_NNN</c>
/// per station, in the order given, and the station positions in the
/// instance's <c>Positioning/geometryValues</c>. The root carries the
/// vertical datum as an S-100 vertical datum code
/// (<c>verticalCoordinateBase</c> 2, <c>verticalDatumReference</c> 1) and
/// the bounding box of the stations.
/// </para>
/// </remarks>
public static class S104StationSeriesWriter
{
    /// <summary>The <c>productSpecification</c> written when the dataset doesn't declare one.</summary>
    public const string ProductSpecification = "INT.IHO.S-104.2.0";

    /// <summary>The S-104 fill value for <c>waterLevelHeight</c>.</summary>
    public const float HeightFillValue = -9999.0f;

    private const string SpecReference = "S-104 Edition 2.0.0 §10.2";

    /// <summary>
    /// Encodes a dataset. Every station needs a uniform time step of whole
    /// seconds (at most 65,535) and as many heights and trends as times.
    /// </summary>
    /// <param name="dataset">
    /// The dataset. <see cref="S104StationSeriesDataset.DataCodingFormat"/>
    /// must be 8, <see cref="S104StationSeriesDataset.VerticalDatum"/> an
    /// S-100 vertical datum code, <see cref="S104StationSeriesDataset.IssueDate"/>
    /// set, and <see cref="S104StationSeriesDataset.HorizontalCRS"/> 4326 or
    /// unset. <see cref="S104StationSeriesDataset.AxisOrder"/>,
    /// <see cref="S104StationSeriesDataset.MinTime"/> and
    /// <see cref="S104StationSeriesDataset.MaxTime"/> are ignored.
    /// </param>
    /// <returns>The file's root group.</returns>
    /// <exception cref="ArgumentException">The dataset can't be encoded; the message says why.</exception>
    public static Hdf5WriteGroup Encode(S104StationSeriesDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        Validate(dataset);

        var stations = dataset.Stations;
        bool withUncertainty = stations.Any(station => station.Uncertainties is not null);
        var (south, west, north, east) = Bounds(stations);

        var root = new Hdf5WriteGroup()
            .SetAttribute("productSpecification", dataset.DeclaredProductSpecification ?? ProductSpecification)
            .SetAttribute("issueDate", dataset.IssueDate ?? string.Empty)
            .SetAttribute("horizontalCRS", 4326)
            .SetAttribute("verticalCS", 6499)
            .SetEnumAttribute("verticalCoordinateBase", S104VerticalCoordinateBase.VerticalDatum)
            .SetEnumAttribute("verticalDatumReference", S104VerticalDatumReference.S100VerticalDatum)
            .SetAttribute("verticalDatum", dataset.VerticalDatum ?? 0)
            .SetAttribute("westBoundLongitude", west)
            .SetAttribute("eastBoundLongitude", east)
            .SetAttribute("southBoundLatitude", south)
            .SetAttribute("northBoundLatitude", north);
        SetIfPresent(root, "issueTime", dataset.IssueTime);
        SetIfPresent(root, "geographicIdentifier", dataset.GeographicIdentifier);
        SetIfPresent(root, "epoch", dataset.Epoch);
        SetIfPresent(root, "metadata", dataset.Metadata);
        if (dataset.WaterLevelTrendThreshold is { } threshold)
        {
            root.SetAttribute("waterLevelTrendThreshold", (float)threshold);
        }

        if (dataset.TrendInterval is { } trendInterval)
        {
            root.SetAttribute("trendInterval", (uint)trendInterval);
        }

        WriteFeatureInformation(root.Group("Group_F"), withUncertainty);

        var (minHeight, maxHeight) = HeightRange(stations);
        var waterLevel = root.Group("WaterLevel")
            .SetEnumAttribute("dataCodingFormat", S104DataCodingFormat.StationwiseFixed)
            .SetAttribute("dimension", (byte)2)
            .SetEnumAttribute("commonPointRule", S104CommonPointRule.All)
            .SetEnumAttribute("interpolationType", S104InterpolationType.Discrete)
            .SetAttribute("horizontalPositionUncertainty", -1.0f)
            .SetAttribute("verticalUncertainty", -1.0f)
            .SetAttribute("timeUncertainty", -1.0f)
            .SetAttribute("numInstances", 1u)
            .SetAttribute("minDatasetHeight", minHeight)
            .SetAttribute("maxDatasetHeight", maxHeight);
        SetIfPresent(waterLevel, "methodWaterLevelProduct", dataset.MethodWaterLevelProduct);

        var instance = waterLevel.Group("WaterLevel.01")
            .SetAttribute("numberOfStations", (uint)stations.Count)
            .SetAttribute("numGRP", (uint)stations.Count)
            .SetAttribute("numberOfTimes", (uint)stations.Max(station => station.NumberOfTimes))
            .SetAttribute("westBoundLongitude", west)
            .SetAttribute("eastBoundLongitude", east)
            .SetAttribute("southBoundLatitude", south)
            .SetAttribute("northBoundLatitude", north)
            .SetAttribute("dateTimeOfFirstRecord", FormatDateTime(stations.Min(station => station.StartTime)))
            .SetAttribute("dateTimeOfLastRecord", FormatDateTime(stations.Max(station => station.EndTime)));
        if (dataset.DataDynamicity is { } dynamicity)
        {
            instance.SetEnumAttribute("dataDynamicity", (S104DataDynamicity)dynamicity);
        }

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
            WriteValues(group, station, withUncertainty);
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

    private static void Validate(S104StationSeriesDataset dataset)
    {
        if (dataset.DataCodingFormat != 8)
        {
            throw Invalid($"Data coding format {dataset.DataCodingFormat} can't be written; only 8 (stationwise fixed stations) can.");
        }

        if (dataset.VerticalDatum is not { } datum || !VerticalDatums.TryGetLabel(datum, out _))
        {
            throw Invalid(dataset.VerticalDatum is null
                ? "The dataset has no vertical datum; S-104 requires an S-100 vertical datum code."
                : $"Vertical datum {dataset.VerticalDatum} isn't an S-100 vertical datum code.");
        }

        if (dataset.HorizontalCRS is { } crs && crs != 4326)
        {
            throw Invalid($"Horizontal CRS EPSG:{crs} can't be written; station positions are WGS 84 (EPSG:4326).");
        }

        if (string.IsNullOrEmpty(dataset.IssueDate))
        {
            throw Invalid("The dataset has no issue date.");
        }

        if (dataset.DataDynamicity is { } dynamicity && !Enum.IsDefined((S104DataDynamicity)dynamicity))
        {
            throw Invalid($"Data dynamicity {dynamicity} isn't an S-104 value (1 to 5).");
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

    private static void ValidateStation(WaterLevelStation station)
    {
        var id = station.Identifier;
        if (station.NumberOfTimes < 1 ||
            station.Heights.Length != station.NumberOfTimes ||
            station.Trends.Length != station.NumberOfTimes ||
            (station.Uncertainties is { } uncertainties && uncertainties.Length != station.NumberOfTimes))
        {
            throw Invalid($"Station '{id}' needs as many heights, trends and uncertainties as its {station.NumberOfTimes} times.");
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

        if (station.Trends.Any(trend => trend > 3))
        {
            throw Invalid($"Station '{id}' has a trend code above 3.");
        }

        if (!double.IsFinite(station.Latitude) || Math.Abs(station.Latitude) > 90 ||
            !double.IsFinite(station.Longitude) || Math.Abs(station.Longitude) > 180)
        {
            throw Invalid($"Station '{id}' has no valid position.");
        }
    }

    private static void WriteFeatureInformation(Hdf5WriteGroup groupF, bool withUncertainty)
    {
        List<FeatureAttributeRow> rows =
        [
            new()
            {
                Code = "waterLevelHeight",
                Name = "Water Level Height",
                UnitOfMeasure = "metre",
                FillValue = HeightFillValue.ToString("0.00", CultureInfo.InvariantCulture),
                Datatype = "H5T_FLOAT",
                Lower = "-99.99",
                Upper = "99.99",
                Closure = "closedInterval",
            },
            new()
            {
                Code = "waterLevelTrend",
                Name = "Water Level Trend",
                UnitOfMeasure = string.Empty,
                FillValue = "0",
                Datatype = "H5T_ENUM",
                Lower = string.Empty,
                Upper = string.Empty,
                Closure = string.Empty,
            },
        ];
        if (withUncertainty)
        {
            rows.Add(new()
            {
                Code = "uncertainty",
                Name = "Uncertainty",
                UnitOfMeasure = "metre",
                FillValue = "-1.00",
                Datatype = "H5T_FLOAT",
                Lower = "0.00",
                Upper = string.Empty,
                Closure = "geSemiInterval",
            });
        }

        groupF
            .SetDataset("featureCode", ["WaterLevel"])
            .SetDataset("WaterLevel", rows.ToArray());
    }

    private static void WriteValues(Hdf5WriteGroup group, WaterLevelStation station, bool withUncertainty)
    {
        int count = station.NumberOfTimes;
        if (withUncertainty)
        {
            var rows = new ValueWithUncertainty[count];
            for (int index = 0; index < count; index++)
            {
                rows[index] = new ValueWithUncertainty
                {
                    WaterLevelHeight = station.Heights[index],
                    WaterLevelTrend = (S104WaterLevelTrend)station.Trends[index],
                    Uncertainty = station.Uncertainties?[index] ?? WaterLevelCoverage.UncertaintyFillValue,
                };
            }

            group.SetDataset("values", rows);
        }
        else
        {
            var rows = new Value[count];
            for (int index = 0; index < count; index++)
            {
                rows[index] = new Value
                {
                    WaterLevelHeight = station.Heights[index],
                    WaterLevelTrend = (S104WaterLevelTrend)station.Trends[index],
                };
            }

            group.SetDataset("values", rows);
        }
    }

    // The bounds are single precision; round them outwards so every
    // station's double-precision position stays inside them.
    private static (float South, float West, float North, float East) Bounds(IReadOnlyList<WaterLevelStation> stations) => (
        Down(stations.Min(station => station.Latitude)),
        Down(stations.Min(station => station.Longitude)),
        Up(stations.Max(station => station.Latitude)),
        Up(stations.Max(station => station.Longitude)));

    private static float Down(double value) => (float)value > value ? MathF.BitDecrement((float)value) : (float)value;

    private static float Up(double value) => (float)value < value ? MathF.BitIncrement((float)value) : (float)value;

    private static (float Min, float Max) HeightRange(IReadOnlyList<WaterLevelStation> stations)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (var height in stations.SelectMany(station => station.Heights))
        {
            if (height == HeightFillValue || !float.IsFinite(height))
            {
                continue;
            }

            min = Math.Min(min, height);
            max = Math.Max(max, height);
        }

        return min <= max ? (min, max) : (HeightFillValue, HeightFillValue);
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
        [Hdf5Member("waterLevelHeight")] public float WaterLevelHeight;
        [Hdf5Member("waterLevelTrend")] public S104WaterLevelTrend WaterLevelTrend;
    }

    private struct ValueWithUncertainty
    {
        [Hdf5Member("waterLevelHeight")] public float WaterLevelHeight;
        [Hdf5Member("waterLevelTrend")] public S104WaterLevelTrend WaterLevelTrend;
        [Hdf5Member("uncertainty")] public float Uncertainty;
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
