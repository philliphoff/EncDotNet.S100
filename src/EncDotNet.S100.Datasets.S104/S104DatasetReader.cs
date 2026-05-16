using System.Globalization;
using EncDotNet.S100.Hdf5;
using S100Diag = EncDotNet.S100.Datasets.S104.Diagnostics;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// Reads an S-104 Water Level dataset from an HDF5 file via the
/// <see cref="IHdf5File"/> abstraction. Supports data coding format 2
/// (regularly-gridded coverage) and data coding format 8 (time series at
/// fixed stations).
/// </summary>
public static class S104DatasetReader
{
    /// <summary>
    /// Reads an <see cref="S104Dataset"/> from the given HDF5 file. Throws
    /// <see cref="S100DatasetNotSupportedException"/> if the dataset is
    /// not dcf2 (regularly-gridded). Use <see cref="ReadAny"/> to handle
    /// both dcf2 and dcf8.
    /// </summary>
    public static S104Dataset Read(IHdf5File file)
    {
        var any = ReadAny(file);
        return any switch
        {
            S104DatasetData.GriddedCoverage g => g.Dataset,
            S104DatasetData.StationSeries => throw new S100DatasetNotSupportedException(
                product: "S-104",
                file: null,
                feature: "data coding format 8 (time series at fixed stations)",
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatNotSupported(
                    "S-104", null,
                    "data coding format 8 (time series at fixed stations)",
                    "S-100 Part 10c §10.2.1",
                    "Use S104DatasetReader.ReadAny to handle dcf8 station series.")),
            _ => throw new InvalidOperationException("Unhandled S104DatasetData variant."),
        };
    }

    /// <summary>
    /// Reads either a dcf2 <see cref="S104Dataset"/> or a dcf8
    /// <see cref="S104StationSeriesDataset"/> from the given HDF5 file,
    /// dispatching on the <c>/WaterLevel/dataCodingFormat</c> attribute
    /// (S-100 Part 10c §10.2.1). Other data coding formats raise
    /// <see cref="S100DatasetNotSupportedException"/>.
    /// </summary>
    public static S104DatasetData ReadAny(IHdf5File file)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-104");
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalCRS")
            ? (int)root.ReadInt64Attribute("horizontalCRS")
            : null;

        string? epoch = root.AttributeExists("epoch")
            ? root.ReadStringAttribute("epoch")
            : null;

        string? geographicIdentifier = root.AttributeExists("geographicIdentifier")
            ? root.ReadStringAttribute("geographicIdentifier")
            : null;

        string? issueDate = root.AttributeExists("issueDate")
            ? root.ReadStringAttribute("issueDate")
            : null;

        string? metadata = root.AttributeExists("metadata")
            ? root.ReadStringAttribute("metadata")
            : null;

        double? waterLevelTrendThreshold = root.AttributeExists("waterLevelTrendThreshold")
            ? root.ReadDoubleAttribute("waterLevelTrendThreshold")
            : null;

        var wlGroup = root.OpenGroup("WaterLevel");
        const string WaterLevelPath = "/WaterLevel";

        // S-104 Edition 2.0.0 §10.2 — every WaterLevel container carries
        // a dataCodingFormat enum that selects the per-instance layout.
        int dataCodingFormat = wlGroup.AttributeExists("dataCodingFormat")
            ? (int)wlGroup.ReadRequiredInt64Attribute(
                "dataCodingFormat",
                product: "S-104",
                file: null,
                groupPath: WaterLevelPath,
                specReference: "S-100 Part 10c §10.2.1")
            : 2;

        string? methodWaterLevelProduct = wlGroup.AttributeExists("methodWaterLevelProduct")
            ? wlGroup.ReadStringAttribute("methodWaterLevelProduct")
            : null;

        if (dataCodingFormat == 8)
        {
            var stations = ReadStationSeries(root, wlGroup);
            DateTime? minTime = null, maxTime = null;
            foreach (var s in stations)
            {
                if (minTime is null || s.StartTime < minTime) minTime = s.StartTime;
                if (maxTime is null || s.EndTime > maxTime) maxTime = s.EndTime;
            }

            return new S104DatasetData.StationSeries(new S104StationSeriesDataset
            {
                HorizontalCRS = horizontalCRS,
                Epoch = epoch,
                GeographicIdentifier = geographicIdentifier,
                IssueDate = issueDate,
                Metadata = metadata,
                DataCodingFormat = 8,
                MethodWaterLevelProduct = methodWaterLevelProduct,
                WaterLevelTrendThreshold = waterLevelTrendThreshold,
                Stations = stations,
                MinTime = minTime,
                MaxTime = maxTime,
            });
        }

        var coverages = ReadCoverages(wlGroup, dataCodingFormat);

        return new S104DatasetData.GriddedCoverage(new S104Dataset
        {
            HorizontalCRS = horizontalCRS,
            Epoch = epoch,
            GeographicIdentifier = geographicIdentifier,
            IssueDate = issueDate,
            Metadata = metadata,
            DataCodingFormat = dataCodingFormat,
            MethodWaterLevelProduct = methodWaterLevelProduct,
            Coverages = coverages,
        });
    }

    private static List<WaterLevelCoverage> ReadCoverages(IHdf5Group wlGroup, int dataCodingFormat)
    {
        if (dataCodingFormat != 2)
        {
            string feature = $"data coding format {dataCodingFormat} ({DataCodingFormatName(dataCodingFormat)})";
            throw new S100DatasetNotSupportedException(
                product: "S-104",
                file: null,
                feature: feature,
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatNotSupported(
                    "S-104", null, feature, "S-100 Part 10c §10.2.1",
                    "Only formats 2 (regular grid) and 8 (time series at fixed stations) are currently implemented."));
        }

        var coverages = new List<WaterLevelCoverage>();

        foreach (var instanceName in wlGroup.GroupNames)
        {
            if (!instanceName.StartsWith("WaterLevel.", StringComparison.Ordinal))
                continue;

            var instance = wlGroup.OpenGroup(instanceName);
            ReadInstance(instance, coverages, $"/WaterLevel/{instanceName}");
        }

        return coverages;
    }

    /// <summary>
    /// Human-readable label for the S-100 data coding format enumeration
    /// (S-100 Part 10c §10.2.1 Table). Used only in error messages, not
    /// in dispatch logic.
    /// </summary>
    private static string DataCodingFormatName(int dcf) => dcf switch
    {
        1 => "time series at fixed stations (irregular)",
        2 => "regularly-gridded arrays",
        3 => "ungeorectified grid",
        4 => "moving platform",
        5 => "irregular grid",
        6 => "variable cell size",
        7 => "TIN",
        8 => "time series at fixed stations",
        9 => "stationwise arrays",
        _ => "unknown",
    };

    private static void ReadInstance(IHdf5Group instance, List<WaterLevelCoverage> coverages, string instancePath)
    {
        // S-100 Part 10c §10.2.1.2 — the grid-georef attributes are
        // required on every dcf2 WaterLevel.NN instance group.
        const string Spec = "S-100 Part 10c §10.2.1.2";
        double originLat = instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-104", null, instancePath, Spec);
        double originLon = instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-104", null, instancePath, Spec);
        double spacingLat = instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-104", null, instancePath, Spec);
        double spacingLon = instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-104", null, instancePath, Spec);
        int numLat = (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-104", null, instancePath, Spec);
        int numLon = (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-104", null, instancePath, Spec);

        string? startSequence = instance.AttributeExists("startSequence")
            ? instance.ReadStringAttribute("startSequence")
            : null;

        // Each Group_NNN is a time step with its own timePoint attribute and values dataset.
        foreach (var groupName in instance.GroupNames)
        {
            if (!groupName.StartsWith("Group_", StringComparison.Ordinal))
                continue;

            var group = instance.OpenGroup(groupName);

            string timePointStr = group.ReadStringAttribute("timePoint");
            DateTime timePoint = DateTime.ParseExact(
                timePointStr,
                ["yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            var values = ReadValues(group);

            coverages.Add(new WaterLevelCoverage
            {
                OriginLatitude = originLat,
                OriginLongitude = originLon,
                SpacingLatitudinal = spacingLat,
                SpacingLongitudinal = spacingLon,
                NumPointsLatitudinal = numLat,
                NumPointsLongitudinal = numLon,
                StartSequence = startSequence,
                TimePoint = timePoint,
                Values = values,
            });
        }
    }

    /// <summary>
    /// Reads the per-time-step <c>values</c> compound dataset and projects it
    /// into <see cref="WaterLevelValue"/>s, tolerating producer variation in
    /// member naming and trend encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The S-104 Feature Catalogue names the compound members
    /// <c>waterLevelHeight</c> and <c>waterLevelTrend</c>; observed UKHO
    /// production files use <c>surfaceHeight</c> and <c>trend</c>; some
    /// in-tree synthetic fixtures use the C# field names <c>Height</c> and
    /// <c>Trend</c>. All three are accepted (case-insensitive).
    /// </para>
    /// <para>
    /// <c>waterLevelTrend</c> is spec-encoded as a uint8 enumeration but UKHO
    /// dcf2 files store it as <c>f32</c>; both are decoded.
    /// </para>
    /// </remarks>
    private static WaterLevelValue[] ReadValues(IHdf5Group group)
    {
        var raw = group.ReadRawCompoundDataset("values");

        var heightMember = raw.FindMember("waterLevelHeight", "surfaceHeight", "Height")
            ?? throw new InvalidOperationException(
                "S-104 'values' compound is missing a height member " +
                "(expected 'waterLevelHeight', 'surfaceHeight', or 'Height').");

        var trendMember = raw.FindMember("waterLevelTrend", "trend", "Trend");

        var result = new WaterLevelValue[raw.RecordCount];
        var span = raw.Data.AsSpan();

        for (int i = 0; i < raw.RecordCount; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);

            float height = heightMember.Kind switch
            {
                CompoundMemberKind.Float32 => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                    record.Slice(heightMember.Offset, 4)),
                CompoundMemberKind.Float64 => (float)System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(
                    record.Slice(heightMember.Offset, 8)),
                _ => throw new NotSupportedException(
                    $"S-104 height member '{heightMember.Name}' has unsupported kind {heightMember.Kind}."),
            };

            byte trend = 0;
            if (trendMember is not null)
            {
                trend = trendMember.Kind switch
                {
                    CompoundMemberKind.UInt8 or CompoundMemberKind.Int8 =>
                        record[trendMember.Offset],
                    // UKHO dcf2 stores trend as f32 — round to nearest valid enum byte.
                    CompoundMemberKind.Float32 => (byte)Math.Clamp(
                        (int)Math.Round(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                            record.Slice(trendMember.Offset, 4))),
                        0, 255),
                    CompoundMemberKind.Float64 => (byte)Math.Clamp(
                        (int)Math.Round(System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(
                            record.Slice(trendMember.Offset, 8))),
                        0, 255),
                    CompoundMemberKind.UInt16 => (byte)Math.Clamp(
                        (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                            record.Slice(trendMember.Offset, 2)),
                        0, 255),
                    _ => throw new NotSupportedException(
                        $"S-104 trend member '{trendMember.Name}' has unsupported kind {trendMember.Kind}."),
                };
            }

            result[i] = new WaterLevelValue(height, trend);
        }

        return result;
    }

    // -------------------------------------------------------------------
    // dcf8 — time series at fixed stations (S-104 Edition 2.0.0 §10.2.3 / §10.2.7)
    // -------------------------------------------------------------------

    /// <summary>
    /// Reads every <c>WaterLevel.NN</c> instance group's per-station
    /// time-series payload and joins it against
    /// <c>/Positioning/geometryValues</c> (S-104 Edition 2.0.0 §10.2.3),
    /// returning one <see cref="WaterLevelStation"/> per <c>Group_NNN</c>.
    /// </summary>
    /// <remarks>
    /// Per spec §10.2.3 the i-th row of <c>geometryValues</c> is the
    /// position of the i-th station (Group_001 → row 0). Positions are
    /// shared across every <c>WaterLevel.NN</c> instance under
    /// <c>/WaterLevel/</c>.
    /// </remarks>
    private static IReadOnlyList<WaterLevelStation> ReadStationSeries(IHdf5Group root, IHdf5Group wlGroup)
    {
        var positions = ReadStationPositions(root);

        var stations = new List<WaterLevelStation>();

        foreach (var instanceName in wlGroup.GroupNames)
        {
            if (!instanceName.StartsWith("WaterLevel.", StringComparison.Ordinal))
                continue;

            var instance = wlGroup.OpenGroup(instanceName);
            var instancePath = $"/WaterLevel/{instanceName}";
            ReadStationInstance(instance, instancePath, positions, stations);
        }

        return stations;
    }

    private static List<(double Lat, double Lon)> ReadStationPositions(IHdf5Group root)
    {
        // S-104 Edition 2.0.0 §10.2.3 — station positions live in a
        // /Positioning group containing a compound 'geometryValues'
        // dataset with members 'latitude' and 'longitude'. Some legacy
        // tooling places the group under /WaterLevel/Positioning;
        // accept either.
        IHdf5Group? positioningGroup = null;
        if (root.GroupNames.Contains("Positioning"))
        {
            positioningGroup = root.OpenGroup("Positioning");
        }
        else
        {
            // Look one level deeper, under any WaterLevel.NN instance.
            // (Strictly out-of-spec but worth a tolerant fallback.)
            if (root.GroupNames.Contains("WaterLevel"))
            {
                var wl = root.OpenGroup("WaterLevel");
                foreach (var name in wl.GroupNames)
                {
                    if (!name.StartsWith("WaterLevel.", StringComparison.Ordinal)) continue;
                    var inst = wl.OpenGroup(name);
                    if (inst.GroupNames.Contains("Positioning"))
                    {
                        positioningGroup = inst.OpenGroup("Positioning");
                        break;
                    }
                }
            }
        }

        if (positioningGroup is null)
        {
            throw new S100DatasetSchemaException(
                product: "S-104",
                file: null,
                groupPath: "/Positioning",
                attributeOrDataset: "Positioning/geometryValues",
                specReference: "S-104 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-104", null, "/Positioning", "Positioning/geometryValues",
                    "S-104 Edition 2.0.0 §10.2.3"));
        }

        RawCompoundDataset raw;
        try
        {
            raw = positioningGroup.ReadRawCompoundDataset("geometryValues");
        }
        catch (Exception ex)
        {
            throw new S100DatasetSchemaException(
                product: "S-104",
                file: null,
                groupPath: "/Positioning",
                attributeOrDataset: "Positioning/geometryValues",
                specReference: "S-104 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-104", null, "/Positioning", "Positioning/geometryValues",
                    "S-104 Edition 2.0.0 §10.2.3"),
                innerException: ex);
        }

        var latMember = raw.FindMember("latitude", "Latitude", "lat", "Lat")
            ?? throw new S100DatasetSchemaException(
                product: "S-104",
                file: null,
                groupPath: "/Positioning/geometryValues",
                attributeOrDataset: "latitude",
                specReference: "S-104 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-104", null, "/Positioning/geometryValues", "latitude",
                    "S-104 Edition 2.0.0 §10.2.3"));

        var lonMember = raw.FindMember("longitude", "Longitude", "long", "Long", "lon", "Lon")
            ?? throw new S100DatasetSchemaException(
                product: "S-104",
                file: null,
                groupPath: "/Positioning/geometryValues",
                attributeOrDataset: "longitude",
                specReference: "S-104 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-104", null, "/Positioning/geometryValues", "longitude",
                    "S-104 Edition 2.0.0 §10.2.3"));

        var positions = new List<(double Lat, double Lon)>(raw.RecordCount);
        var span = raw.Data.AsSpan();
        for (int i = 0; i < raw.RecordCount; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);
            double lat = ReadFloatingPointMember(record, latMember);
            double lon = ReadFloatingPointMember(record, lonMember);
            positions.Add((lat, lon));
        }
        return positions;
    }

    private static double ReadFloatingPointMember(ReadOnlySpan<byte> record, CompoundMemberInfo member) =>
        member.Kind switch
        {
            CompoundMemberKind.Float32 => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                record.Slice(member.Offset, 4)),
            CompoundMemberKind.Float64 => System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(
                record.Slice(member.Offset, 8)),
            _ => throw new NotSupportedException(
                $"S-104 Positioning member '{member.Name}' has unsupported kind {member.Kind}."),
        };

    private static void ReadStationInstance(
        IHdf5Group instance,
        string instancePath,
        IReadOnlyList<(double Lat, double Lon)> positions,
        List<WaterLevelStation> stations)
    {
        const string Spec = "S-104 Edition 2.0.0 §10.2.7";

        int numberOfStations = instance.AttributeExists("numberOfStations")
            ? (int)instance.ReadInt64Attribute("numberOfStations")
            : 0;

        // Walk each Group_NNN station group in declaration order — the
        // i-th group's position is positions[i].
        int stationIndex = 0;
        foreach (var groupName in instance.GroupNames)
        {
            if (!groupName.StartsWith("Group_", StringComparison.Ordinal))
                continue;

            var groupPath = $"{instancePath}/{groupName}";
            var group = instance.OpenGroup(groupName);

            string stationId = group.AttributeExists("stationIdentification")
                ? group.ReadStringAttribute("stationIdentification")
                : groupName;

            string startStr = group.ReadRequiredStringAttribute(
                "startDateTime", "S-104", null, groupPath, Spec);
            string endStr = group.ReadRequiredStringAttribute(
                "endDateTime", "S-104", null, groupPath, Spec);

            DateTime startTime = ParseTimestamp(startStr);
            DateTime endTime = ParseTimestamp(endStr);

            int numberOfTimes = (int)group.ReadRequiredInt64Attribute(
                "numberOfTimes", "S-104", null, groupPath, Spec);
            long intervalSeconds = group.ReadRequiredInt64Attribute(
                "timeRecordInterval", "S-104", null, groupPath, Spec);
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            var (heights, trends) = ReadStationValues(group, numberOfTimes);

            if (stationIndex >= positions.Count)
            {
                throw new S100DatasetSchemaException(
                    product: "S-104",
                    file: null,
                    groupPath: "/Positioning/geometryValues",
                    attributeOrDataset: "Positioning/geometryValues",
                    specReference: "S-104 Edition 2.0.0 §10.2.3",
                    message: ExceptionMessageFormatter.FormatSchema(
                        "S-104", null, "/Positioning/geometryValues", "Positioning/geometryValues",
                        "S-104 Edition 2.0.0 §10.2.3")
                    + $" Position row {stationIndex} missing for station '{stationId}'.");
            }
            var (lat, lon) = positions[stationIndex];

            stations.Add(new WaterLevelStation
            {
                Identifier = stationId,
                Latitude = lat,
                Longitude = lon,
                StartTime = startTime,
                EndTime = endTime,
                TimeRecordInterval = interval,
                NumberOfTimes = numberOfTimes,
                Heights = heights,
                Trends = trends,
            });

            stationIndex++;
        }

        // numberOfStations is an authoritative spec-declared count; if a
        // file claims more than it actually delivers, we tolerate the
        // shortfall (consistent with the spec's allowance for trailing
        // empty groups), but we don't try to invent stations.
        _ = numberOfStations;
    }

    private static DateTime ParseTimestamp(string s)
    {
        return DateTime.ParseExact(
            s,
            ["yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static (float[] Heights, byte[] Trends) ReadStationValues(IHdf5Group group, int numberOfTimes)
    {
        var raw = group.ReadRawCompoundDataset("values");

        var heightMember = raw.FindMember("waterLevelHeight", "surfaceHeight", "Height")
            ?? throw new InvalidOperationException(
                "S-104 dcf8 station 'values' compound is missing a height member " +
                "(expected 'waterLevelHeight', 'surfaceHeight', or 'Height').");

        var trendMember = raw.FindMember("waterLevelTrend", "trend", "Trend");

        int count = Math.Min(raw.RecordCount, numberOfTimes);
        var heights = new float[count];
        var trends = new byte[count];
        var span = raw.Data.AsSpan();

        for (int i = 0; i < count; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);

            heights[i] = heightMember.Kind switch
            {
                CompoundMemberKind.Float32 => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                    record.Slice(heightMember.Offset, 4)),
                CompoundMemberKind.Float64 => (float)System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(
                    record.Slice(heightMember.Offset, 8)),
                _ => throw new NotSupportedException(
                    $"S-104 height member '{heightMember.Name}' has unsupported kind {heightMember.Kind}."),
            };

            trends[i] = trendMember is null ? (byte)0 : trendMember.Kind switch
            {
                CompoundMemberKind.UInt8 or CompoundMemberKind.Int8 => record[trendMember.Offset],
                CompoundMemberKind.Float32 => (byte)Math.Clamp(
                    (int)Math.Round(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                        record.Slice(trendMember.Offset, 4))),
                    0, 255),
                CompoundMemberKind.Float64 => (byte)Math.Clamp(
                    (int)Math.Round(System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(
                        record.Slice(trendMember.Offset, 8))),
                    0, 255),
                CompoundMemberKind.UInt16 => (byte)Math.Clamp(
                    (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                        record.Slice(trendMember.Offset, 2)),
                    0, 255),
                _ => throw new NotSupportedException(
                    $"S-104 trend member '{trendMember.Name}' has unsupported kind {trendMember.Kind}."),
            };
        }

        return (heights, trends);
    }
}
