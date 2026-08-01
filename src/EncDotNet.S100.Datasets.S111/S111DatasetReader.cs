using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Pipelines;
using S100Diag = EncDotNet.S100.Datasets.S111.Diagnostics;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Reads an S-111 Surface Currents dataset from an HDF5 file via the
/// <see cref="IHdf5File"/> abstraction. Supports data coding format 2
/// (regular grid), data coding format 3 (ungeorectified grid with
/// explicit per-node positioning; S-100 Part 10c §10.2.1), and data
/// coding format 8 (time series at fixed stations; S-111 Edition 2.0.0
/// §10.2.3 / §10.2.7).
/// </summary>
public static class S111DatasetReader
{
    /// <summary>
    /// Reads an <see cref="S111Dataset"/> from the given HDF5 file. Throws
    /// <see cref="S100DatasetNotSupportedException"/> if the dataset is
    /// not dcf2 (regularly-gridded). Use <see cref="ReadAny"/> to handle
    /// dcf1, dcf2, dcf3, and dcf8.
    /// </summary>
    public static S111Dataset Read(IHdf5File file)
    {
        var any = ReadAny(file);
        return any switch
        {
            S111DatasetData.GriddedCoverage g => g.Dataset,
            S111DatasetData.StationSeries stationSeries => throw new S100DatasetNotSupportedException(
                product: "S-111",
                file: null,
                feature: $"data coding format {stationSeries.Dataset.DataCodingFormat} " +
                    $"({DataCodingFormatName(stationSeries.Dataset.DataCodingFormat)})",
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatNotSupported(
                    "S-111", null,
                    $"data coding format {stationSeries.Dataset.DataCodingFormat} " +
                        $"({DataCodingFormatName(stationSeries.Dataset.DataCodingFormat)})",
                    "S-100 Part 10c §10.2.1",
                    "Use S111DatasetReader.ReadAny to handle station series.")),
            _ => throw new InvalidOperationException("Unhandled S111DatasetData variant."),
        };
    }

    /// <summary>
    /// Reads either a dcf2 <see cref="S111Dataset"/> or a dcf1/dcf3/dcf8
    /// <see cref="S111StationSeriesDataset"/> from the given HDF5 file,
    /// dispatching on the <c>/SurfaceCurrent/dataCodingFormat</c> attribute
    /// (S-100 Part 10c §10.2.1). Other data coding formats raise
    /// <see cref="S100DatasetNotSupportedException"/>.
    /// </summary>
    public static S111DatasetData ReadAny(IHdf5File file) => ReadAny(file, options: null);

    /// <summary>
    /// Reads either a dcf2 <see cref="S111Dataset"/> or a dcf1/dcf3/dcf8
    /// <see cref="S111StationSeriesDataset"/> from the given HDF5 file,
    /// dispatching on the <c>/SurfaceCurrent/dataCodingFormat</c> attribute
    /// (S-100 Part 10c §10.2.1). Other data coding formats raise
    /// <see cref="S100DatasetNotSupportedException"/>.
    /// </summary>
    /// <param name="file">The HDF5 file to read.</param>
    /// <param name="options">
    /// Optional read options. When
    /// <see cref="S111ReadOptions.DeferValueReads"/> is set, dcf2
    /// (regular-grid) per-time-step <c>values</c> datasets are read lazily
    /// on first access rather than up front; the caller must keep
    /// <paramref name="file"/> open for the lifetime of the returned
    /// dataset. dcf1/dcf3/dcf8 (station-series) datasets always materialize
    /// fully and ignore this flag.
    /// </param>
    public static S111DatasetData ReadAny(IHdf5File file, S111ReadOptions? options)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-111");
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalCRS")
            ? (int)root.ReadInt64Attribute("horizontalCRS")
            : root.AttributeExists("horizontalDatumValue")
                ? (int)root.ReadInt64Attribute("horizontalDatumValue")
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

        // S-100 Part 10c §10.2.1 — the root carries the productSpecification
        // string (e.g. "INT.IHO.S-111.2.0.0"); surfaced so the pipeline can
        // report the declared edition and warn on a version mismatch.
        string? productSpecification = root.AttributeExists("productSpecification")
            ? root.ReadStringAttribute("productSpecification")
            : null;

        float? surfaceCurrentDepth = root.AttributeExists("surfaceCurrentDepth")
            ? (float)root.ReadDoubleAttribute("surfaceCurrentDepth")
            : null;

        var scGroup = root.OpenGroup("SurfaceCurrent");
        const string surfaceCurrentPath = "/SurfaceCurrent";

        // S-111 Edition 2.0.0 §12.2 — every SurfaceCurrent container
        // carries a dataCodingFormat enum that selects the per-instance
        // layout.
        int dataCodingFormat = scGroup.AttributeExists("dataCodingFormat")
            ? (int)scGroup.ReadRequiredInt64Attribute(
                "dataCodingFormat",
                product: "S-111",
                file: null,
                groupPath: surfaceCurrentPath,
                specReference: "S-100 Part 10c §10.2.1")
            : 2;

        int? typeOfCurrentData = scGroup.AttributeExists("typeOfCurrentData")
            ? (int)scGroup.ReadInt64Attribute("typeOfCurrentData")
            : null;

        if (dataCodingFormat is 1 or 3 or 8)
        {
            var stations = dataCodingFormat == 8
                ? ReadStationSeries(root, scGroup)
                : ReadTimeMajorStationSeries(scGroup, dataCodingFormat);
            DateTime? minTime = null, maxTime = null;
            foreach (var s in stations)
            {
                if (minTime is null || s.StartTime < minTime) minTime = s.StartTime;
                if (maxTime is null || s.EndTime > maxTime) maxTime = s.EndTime;
            }

            return new S111DatasetData.StationSeries(new S111StationSeriesDataset
            {
                HorizontalCRS = horizontalCRS,
                Epoch = epoch,
                GeographicIdentifier = geographicIdentifier,
                IssueDate = issueDate,
                Metadata = metadata,
                SurfaceCurrentDepth = surfaceCurrentDepth,
                DataCodingFormat = dataCodingFormat,
                TypeOfCurrentData = typeOfCurrentData,
                Stations = stations,
                MinTime = minTime,
                MaxTime = maxTime,
            })
            {
                DeclaredProductSpecification = productSpecification,
            };
        }

        var coverages = ReadCoverages(scGroup, dataCodingFormat, options);

        return new S111DatasetData.GriddedCoverage(new S111Dataset
        {
            HorizontalCRS = horizontalCRS,
            Epoch = epoch,
            GeographicIdentifier = geographicIdentifier,
            IssueDate = issueDate,
            Metadata = metadata,
            SurfaceCurrentDepth = surfaceCurrentDepth,
            DataCodingFormat = dataCodingFormat,
            TypeOfCurrentData = typeOfCurrentData,
            Coverages = coverages,
        })
        {
            DeclaredProductSpecification = productSpecification,
        };
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for the S-111
    /// dataset — its declared specification, horizontal CRS, geographic
    /// extent, and temporal coverage — for phased / deferred loading (issue
    /// #460).
    /// </summary>
    /// <remarks>
    /// For data coding format 2 (regularly-gridded) the extent is the union
    /// of the <c>SurfaceCurrent.NN</c> grid footprints and the time coverage
    /// spans the <c>timePoint</c> attributes of every <c>Group_NNN</c> step,
    /// read <em>without</em> touching the per-step <c>values</c> arrays. For
    /// data coding formats 3 (ungeorectified grid) and 8 (time series at
    /// fixed stations) the comparatively small station series is read in full
    /// and the extent / time span are derived from the station positions and
    /// record windows.
    /// </remarks>
    public static DatasetMetadata ReadMetadata(IHdf5File file)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.readmetadata");
        __activity?.SetTag("s100.product", "S-111");
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalCRS")
            ? (int)root.ReadInt64Attribute("horizontalCRS")
            : root.AttributeExists("horizontalDatumValue")
                ? (int)root.ReadInt64Attribute("horizontalDatumValue")
                : null;

        string? productSpecification = root.AttributeExists("productSpecification")
            ? root.ReadStringAttribute("productSpecification")
            : null;

        var scGroup = root.OpenGroup("SurfaceCurrent");
        const string surfaceCurrentPath = "/SurfaceCurrent";

        int dataCodingFormat = scGroup.AttributeExists("dataCodingFormat")
            ? (int)scGroup.ReadRequiredInt64Attribute(
                "dataCodingFormat", "S-111", null, surfaceCurrentPath, "S-100 Part 10c §10.2.1")
            : 2;

        BoundingBox? extent;
        TimeCoverage? time;

        if (dataCodingFormat is 1 or 3 or 8)
        {
            var stations = dataCodingFormat == 8
                ? ReadStationSeries(root, scGroup)
                : ReadTimeMajorStationSeries(scGroup, dataCodingFormat);
            extent = StationExtent(stations);
            time = StationTimeCoverage(stations);
        }
        else
        {
            (extent, time) = ReadGriddedExtentAndTime(scGroup);
        }

        return new DatasetMetadata
        {
            Spec = HdfDeclaredSpec.Resolve(productSpecification, "S-111"),
            Extent = extent,
            HorizontalCrsEpsg = horizontalCRS,
            TimeCoverage = time,
        };
    }

    /// <summary>
    /// Computes the union grid extent and time-step span of a dcf2 dataset
    /// from georef and time attributes alone (no <c>values</c> read), so the
    /// result matches a full read's extent and available times. The time span
    /// prefers each instance's arithmetic cadence
    /// (<c>dateTimeOfFirstRecord</c> + <c>i × timeRecordInterval</c>) when
    /// present, falling back to the per-step <c>timePoint</c> attributes.
    /// </summary>
    private static (BoundingBox? Extent, TimeCoverage? Time) ReadGriddedExtentAndTime(IHdf5Group scGroup)
    {
        double south = double.MaxValue, west = double.MaxValue;
        double north = double.MinValue, east = double.MinValue;
        DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
        bool anyBounds = false, anyTime = false;

        foreach (var instanceName in scGroup.GroupNames)
        {
            if (!instanceName.StartsWith("SurfaceCurrent.", StringComparison.Ordinal))
                continue;

            var instance = scGroup.OpenGroup(instanceName);
            var instancePath = $"/SurfaceCurrent/{instanceName}";
            const string spec = "S-100 Part 10c §10.2.1.2";

            double originLat = instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-111", null, instancePath, spec);
            double originLon = instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-111", null, instancePath, spec);
            double spacingLat = instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-111", null, instancePath, spec);
            double spacingLon = instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-111", null, instancePath, spec);
            int numLat = (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-111", null, instancePath, spec);
            int numLon = (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-111", null, instancePath, spec);

            double latA = originLat, latB = originLat + spacingLat * numLat;
            double lonA = originLon, lonB = originLon + spacingLon * numLon;
            south = Math.Min(south, Math.Min(latA, latB));
            north = Math.Max(north, Math.Max(latA, latB));
            west = Math.Min(west, Math.Min(lonA, lonB));
            east = Math.Max(east, Math.Max(lonA, lonB));
            anyBounds = true;

            // Prefer the arithmetic cadence (dateTimeOfFirstRecord + i ×
            // timeRecordInterval) when present so we avoid opening every
            // Group_NNN; otherwise fall back to per-step timePoint attributes.
            var groupNames = instance.GroupNames
                .Where(n => n.StartsWith("Group_", StringComparison.Ordinal))
                .ToList();

            if (groupNames.Count > 0
                && instance.AttributeExists("dateTimeOfFirstRecord")
                && instance.AttributeExists("timeRecordInterval"))
            {
                DateTime first = ParseTimestamp(instance.ReadStringAttribute("dateTimeOfFirstRecord"));
                double intervalSeconds = ReadIntervalSeconds(instance);
                DateTime last = first.AddSeconds(intervalSeconds * (groupNames.Count - 1));
                if (first < min) min = first;
                if (last > max) max = last;
                anyTime = true;
            }
            else
            {
                foreach (var groupName in groupNames)
                {
                    var group = instance.OpenGroup(groupName);
                    var t = ParseTimestamp(group.ReadStringAttribute("timePoint"));
                    if (t < min) min = t;
                    if (t > max) max = t;
                    anyTime = true;
                }
            }
        }

        BoundingBox? extent = anyBounds ? new BoundingBox(south, west, north, east) : null;
        TimeCoverage? time = anyTime ? new TimeCoverage(min, max) : null;
        return (extent, time);
    }

    private static BoundingBox? StationExtent(IReadOnlyList<SurfaceCurrentStation> stations)
    {
        if (stations.Count == 0) return null;

        double south = double.MaxValue, west = double.MaxValue;
        double north = double.MinValue, east = double.MinValue;
        foreach (var s in stations)
        {
            south = Math.Min(south, s.Latitude);
            north = Math.Max(north, s.Latitude);
            west = Math.Min(west, s.Longitude);
            east = Math.Max(east, s.Longitude);
        }

        return new BoundingBox(south, west, north, east);
    }

    private static TimeCoverage? StationTimeCoverage(IReadOnlyList<SurfaceCurrentStation> stations)
    {
        if (stations.Count == 0) return null;

        DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
        foreach (var s in stations)
        {
            if (s.StartTime < min) min = s.StartTime;
            if (s.EndTime > max) max = s.EndTime;
        }

        return new TimeCoverage(min, max);
    }

    private static List<SurfaceCurrentCoverage> ReadCoverages(IHdf5Group scGroup, int dataCodingFormat, S111ReadOptions? options)
    {
        if (dataCodingFormat != 2)
        {
            string feature = $"data coding format {dataCodingFormat} ({DataCodingFormatName(dataCodingFormat)})";
            throw new S100DatasetNotSupportedException(
                product: "S-111",
                file: null,
                feature: feature,
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatNotSupported(
                    "S-111", null, feature, "S-100 Part 10c §10.2.1",
                    "Only format 2 (regular grid) is currently implemented."));
        }

        var coverages = new List<SurfaceCurrentCoverage>();

        foreach (var instanceName in scGroup.GroupNames)
        {
            if (!instanceName.StartsWith("SurfaceCurrent.", StringComparison.Ordinal))
                continue;

            var instance = scGroup.OpenGroup(instanceName);
            ReadInstance(instance, coverages, $"/SurfaceCurrent/{instanceName}", options);
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
        1 => "time series at fixed stations",
        2 => "regularly-gridded arrays",
        3 => "ungeorectified grid",
        4 => "moving platform",
        5 => "irregular grid",
        6 => "variable cell size",
        7 => "TIN",
        8 => "stationwise time series at fixed stations",
        9 => "stationwise arrays",
        _ => "unknown",
    };

    private static void ReadInstance(IHdf5Group instance, List<SurfaceCurrentCoverage> coverages, string instancePath, S111ReadOptions? options)
    {
        // S-100 Part 10c §10.2.1.2 — the grid-georef attributes are
        // required on every dcf2 SurfaceCurrent.NN instance group.
        const string spec = "S-100 Part 10c §10.2.1.2";
        double originLat = instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-111", null, instancePath, spec);
        double originLon = instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-111", null, instancePath, spec);
        double spacingLat = instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-111", null, instancePath, spec);
        double spacingLon = instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-111", null, instancePath, spec);
        int numLat = (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-111", null, instancePath, spec);
        int numLon = (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-111", null, instancePath, spec);

        string? startSequence = instance.AttributeExists("startSequence")
            ? instance.ReadStringAttribute("startSequence")
            : null;

        // Deferred path (S-111 Edition 2.0.0 §10.2.6): when value reads are
        // deferred we avoid opening every Group_NNN at load time — instead
        // the per-step time points are derived arithmetically from
        // dateTimeOfFirstRecord + i × timeRecordInterval (the regular
        // time-series cadence; matches the s111-surface-currents skill's
        // "times derived from dateTimeOfFirstRecord + timeRecordInterval"
        // item), and each step's values compound is read lazily on first
        // access. This keeps opening a 700+ time-step dataset cheap.
        if (options?.DeferValueReads == true
            && instance.AttributeExists("dateTimeOfFirstRecord")
            && instance.AttributeExists("timeRecordInterval"))
        {
            DateTime first = ParseTimestamp(instance.ReadStringAttribute("dateTimeOfFirstRecord"));
            double intervalSeconds = ReadIntervalSeconds(instance);

            var groupNames = instance.GroupNames
                .Where(n => n.StartsWith("Group_", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            object gate = options.HdfSyncRoot ?? new object();

            for (int i = 0; i < groupNames.Count; i++)
            {
                string groupName = groupNames[i];
                DateTime timePoint = first.AddSeconds(intervalSeconds * i);

                coverages.Add(new SurfaceCurrentCoverage
                {
                    OriginLatitude = originLat,
                    OriginLongitude = originLon,
                    SpacingLatitudinal = spacingLat,
                    SpacingLongitudinal = spacingLon,
                    NumPointsLatitudinal = numLat,
                    NumPointsLongitudinal = numLon,
                    StartSequence = startSequence,
                    GroupPath = instancePath,
                    TimePoint = timePoint,
                    ValuesFactory = () =>
                    {
                        lock (gate)
                        {
                            return ReadValues(instance.OpenGroup(groupName));
                        }
                    },
                });
            }

            return;
        }

        // Eager path — each Group_NNN is a time step with its own timePoint
        // attribute and values dataset.
        foreach (var groupName in instance.GroupNames)
        {
            if (!groupName.StartsWith("Group_", StringComparison.Ordinal))
                continue;

            var group = instance.OpenGroup(groupName);

            DateTime timePoint = ParseTimestamp(group.ReadStringAttribute("timePoint"));

            var values = ReadValues(group);

            coverages.Add(new SurfaceCurrentCoverage
            {
                OriginLatitude = originLat,
                OriginLongitude = originLon,
                SpacingLatitudinal = spacingLat,
                SpacingLongitudinal = spacingLon,
                NumPointsLatitudinal = numLat,
                NumPointsLongitudinal = numLon,
                StartSequence = startSequence,
                GroupPath = instancePath,
                TimePoint = timePoint,
                Values = values,
            });
        }
    }

    /// <summary>
    /// Reads the instance <c>timeRecordInterval</c> (seconds; S-111
    /// Edition 2.0.0 §10.2.6) tolerating either an integer or floating
    /// attribute encoding.
    /// </summary>
    private static double ReadIntervalSeconds(IHdf5Group instance)
    {
        try
        {
            return instance.ReadInt64Attribute("timeRecordInterval");
        }
        catch
        {
            return instance.ReadDoubleAttribute("timeRecordInterval");
        }
    }

    /// <summary>
    /// Reads the per-time-step <c>values</c> compound dataset and projects it
    /// into <see cref="SurfaceCurrentValue"/>s, tolerating producer variation
    /// in member naming and numeric width.
    /// </summary>
    /// <remarks>
    /// The S-111 Feature Catalogue names the compound members
    /// <c>surfaceCurrentSpeed</c> and <c>surfaceCurrentDirection</c>; some
    /// in-tree synthetic fixtures use the C# field names <c>Speed</c> and
    /// <c>Direction</c>. Both are accepted (case-insensitive).
    /// </remarks>
    private static SurfaceCurrentValue[] ReadValues(IHdf5Group group)
    {
        var raw = group.ReadRawCompoundDataset("values");

        var speedMember = raw.FindMember("surfaceCurrentSpeed", "Speed")
            ?? throw new InvalidOperationException(
                "S-111 'values' compound is missing a speed member " +
                "(expected 'surfaceCurrentSpeed' or 'Speed').");

        var directionMember = raw.FindMember("surfaceCurrentDirection", "Direction")
            ?? throw new InvalidOperationException(
                "S-111 'values' compound is missing a direction member " +
                "(expected 'surfaceCurrentDirection' or 'Direction').");

        var result = new SurfaceCurrentValue[raw.RecordCount];
        var span = raw.Data.AsSpan();

        for (int i = 0; i < raw.RecordCount; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);

            float speed = ReadFloat(record, speedMember);
            float direction = ReadFloat(record, directionMember);

            result[i] = new SurfaceCurrentValue(speed, direction);
        }

        return result;
    }

    private static float ReadFloat(ReadOnlySpan<byte> record, CompoundMemberInfo member) => member.Kind switch
    {
        CompoundMemberKind.Float32 => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
            record.Slice(member.Offset, 4)),
        CompoundMemberKind.Float64 => (float)System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(
            record.Slice(member.Offset, 8)),
        _ => throw new NotSupportedException(
            $"S-111 member '{member.Name}' has unsupported kind {member.Kind}."),
    };

    // -------------------------------------------------------------------
    // dcf1/dcf3 — time-major positioned values (S-111 Ed 2.0.0 §10.2.2.7)
    // -------------------------------------------------------------------

    /// <summary>
    /// Reads an S-111 time-major DCF1 fixed-station or DCF3 ungeorectified
    /// dataset. Each position has an explicit coordinate from
    /// <c>Positioning/geometryValues</c>, and each <c>Group_NNN</c> is a time
    /// step with one <c>(speed, direction)</c> record per position.
    /// </summary>
    /// <remarks>
    /// S-111 Edition 2.0.0 Tables 10-4 and 10-5 define DCF1 and DCF3 with the
    /// same time-major array shape. This method transposes either shape into
    /// <see cref="SurfaceCurrentStation"/> instances while retaining every
    /// explicit values-group timestamp.
    /// </remarks>
    private static IReadOnlyList<SurfaceCurrentStation> ReadTimeMajorStationSeries(
        IHdf5Group scGroup,
        int dataCodingFormat)
    {
        var allStations = new List<SurfaceCurrentStation>();

        foreach (var instanceName in scGroup.GroupNames)
        {
            if (!instanceName.StartsWith("SurfaceCurrent.", StringComparison.Ordinal))
                continue;

            var instance = scGroup.OpenGroup(instanceName);
            var instancePath = $"/SurfaceCurrent/{instanceName}";
            ReadTimeMajorInstance(
                instance,
                instanceName,
                instancePath,
                dataCodingFormat,
                allStations);
        }

        return allStations;
    }

    private static void ReadTimeMajorInstance(
        IHdf5Group instance,
        string instanceName,
        string instancePath,
        int dataCodingFormat,
        List<SurfaceCurrentStation> stations)
    {
        const string spec = "S-111 Edition 2.0.0 §10.2.2.6–10.2.2.9";

        // Read per-node positions from Positioning/geometryValues under this instance.
        var positions = ReadInstancePositions(instance, instancePath);
        int nodeCount = positions.Count;

        // Collect time-step groups in ascending order.
        var timeGroupNames = instance.GroupNames
            .Where(n => n.StartsWith("Group_", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (timeGroupNames.Count == 0)
            return;

        int numberOfTimes = timeGroupNames.Count;
        var sampleTimes = new DateTime[numberOfTimes];

        // Read all time steps: speeds[t][node], directions[t][node].
        var allSpeeds = new float[numberOfTimes][];
        var allDirections = new float[numberOfTimes][];

        for (int t = 0; t < numberOfTimes; t++)
        {
            var group = instance.OpenGroup(timeGroupNames[t]);
            var groupPath = $"{instancePath}/{timeGroupNames[t]}";
            sampleTimes[t] = ParseTimestamp(group.ReadRequiredStringAttribute(
                "timePoint", "S-111", null, groupPath, spec));
            if (t > 0 && sampleTimes[t] <= sampleTimes[t - 1])
            {
                throw new S100DatasetSchemaException(
                    product: "S-111",
                    file: null,
                    groupPath: groupPath,
                    attributeOrDataset: "timePoint",
                    specReference: spec,
                    message: ExceptionMessageFormatter.FormatSchema(
                        "S-111", null, groupPath, "timePoint", spec)
                    + " Values-group timestamps must be strictly increasing.");
            }

            var values = ReadValues(group);
            if (values.Length != nodeCount)
            {
                throw new S100DatasetSchemaException(
                    product: "S-111",
                    file: null,
                    groupPath: groupPath,
                    attributeOrDataset: "values",
                    specReference: spec,
                    message: ExceptionMessageFormatter.FormatSchema(
                        "S-111", null, groupPath, "values", spec)
                    + $" Expected {nodeCount} records but found {values.Length}.");
            }

            var speeds = new float[nodeCount];
            var directions = new float[nodeCount];
            for (int n = 0; n < nodeCount; n++)
            {
                speeds[n] = values[n].Speed;
                directions[n] = values[n].Direction;
            }

            allSpeeds[t] = speeds;
            allDirections[t] = directions;
        }

        var interval = DeriveUniformInterval(sampleTimes);

        // Transpose to per-node station series.
        for (int n = 0; n < nodeCount; n++)
        {
            var nodeSpeeds = new float[numberOfTimes];
            var nodeDirections = new float[numberOfTimes];
            for (int t = 0; t < numberOfTimes; t++)
            {
                nodeSpeeds[t] = allSpeeds[t][n];
                nodeDirections[t] = allDirections[t][n];
            }

            var (lat, lon) = positions[n];

            stations.Add(new SurfaceCurrentStation
            {
                Identifier = dataCodingFormat == 1
                    ? $"{instanceName}:Station_{n + 1:D3}"
                    : $"Node_{n + 1:D3}",
                Latitude = lat,
                Longitude = lon,
                StartTime = sampleTimes[0],
                EndTime = sampleTimes[^1],
                TimeRecordInterval = interval,
                SampleTimes = sampleTimes,
                NumberOfTimes = numberOfTimes,
                SpeedsMetresPerSecond = nodeSpeeds,
                DirectionsDegreesTrue = nodeDirections,
            });
        }
    }

    private static TimeSpan DeriveUniformInterval(IReadOnlyList<DateTime> sampleTimes)
    {
        if (sampleTimes.Count < 2)
            return TimeSpan.Zero;

        var interval = sampleTimes[1] - sampleTimes[0];
        for (int i = 2; i < sampleTimes.Count; i++)
        {
            if (sampleTimes[i] - sampleTimes[i - 1] != interval)
                return TimeSpan.Zero;
        }

        return interval;
    }

    /// <summary>
    /// Reads node positions from <c>Positioning/geometryValues</c> under
    /// a specific <c>SurfaceCurrent.NN</c> instance group (DCF 3 layout;
    /// S-100 Part 10c §10.2.1).
    /// </summary>
    private static List<GeoPosition> ReadInstancePositions(IHdf5Group instance, string instancePath)
    {
        if (!instance.GroupNames.Contains("Positioning"))
        {
            throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: $"{instancePath}/Positioning",
                attributeOrDataset: "Positioning/geometryValues",
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, $"{instancePath}/Positioning", "Positioning/geometryValues",
                    "S-100 Part 10c §10.2.1"));
        }

        var posGroup = instance.OpenGroup("Positioning");

        RawCompoundDataset raw;
        try
        {
            raw = posGroup.ReadRawCompoundDataset("geometryValues");
        }
        catch (Exception ex)
        {
            throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: $"{instancePath}/Positioning",
                attributeOrDataset: "geometryValues",
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, $"{instancePath}/Positioning", "geometryValues",
                    "S-100 Part 10c §10.2.1"),
                innerException: ex);
        }

        var latMember = raw.FindMember("latitude", "Latitude", "lat", "Lat")
            ?? throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: $"{instancePath}/Positioning/geometryValues",
                attributeOrDataset: "latitude",
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, $"{instancePath}/Positioning/geometryValues", "latitude",
                    "S-100 Part 10c §10.2.1"));

        var lonMember = raw.FindMember("longitude", "Longitude", "long", "Long", "lon", "Lon")
            ?? throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: $"{instancePath}/Positioning/geometryValues",
                attributeOrDataset: "longitude",
                specReference: "S-100 Part 10c §10.2.1",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, $"{instancePath}/Positioning/geometryValues", "longitude",
                    "S-100 Part 10c §10.2.1"));

        var positions = new List<GeoPosition>(raw.RecordCount);
        var span = raw.Data.AsSpan();
        for (int i = 0; i < raw.RecordCount; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);
            double lat = ReadFloatingPointMember(record, latMember);
            double lon = ReadFloatingPointMember(record, lonMember);
            positions.Add(new GeoPosition(lat, lon));
        }
        return positions;
    }

    // -------------------------------------------------------------------
    // dcf8 — time series at fixed stations (S-111 Edition 2.0.0 §10.2.3 / §10.2.7)
    // -------------------------------------------------------------------

    /// <summary>
    /// Reads every <c>SurfaceCurrent.NN</c> instance group's per-station
    /// time-series payload and joins it against
    /// <c>/Positioning/geometryValues</c> (S-111 Edition 2.0.0 §10.2.3,
    /// mirroring S-104 §10.2.3 / S-100 Part 10c §10.2.6), returning one
    /// <see cref="SurfaceCurrentStation"/> per <c>Group_NNN</c>.
    /// </summary>
    /// <remarks>
    /// Per spec §10.2.3 the i-th row of <c>geometryValues</c> is the
    /// position of the i-th station (Group_001 → row 0). Positions are
    /// shared across every <c>SurfaceCurrent.NN</c> instance under
    /// <c>/SurfaceCurrent/</c>.
    /// </remarks>
    private static IReadOnlyList<SurfaceCurrentStation> ReadStationSeries(IHdf5Group root, IHdf5Group scGroup)
    {
        var positions = ReadStationPositions(root);

        var stations = new List<SurfaceCurrentStation>();

        foreach (var instanceName in scGroup.GroupNames)
        {
            if (!instanceName.StartsWith("SurfaceCurrent.", StringComparison.Ordinal))
                continue;

            var instance = scGroup.OpenGroup(instanceName);
            var instancePath = $"/SurfaceCurrent/{instanceName}";
            ReadStationInstance(instance, instancePath, positions, stations);
        }

        return stations;
    }

    private static List<GeoPosition> ReadStationPositions(IHdf5Group root)
    {
        // S-111 Edition 2.0.0 §10.2.3 — station positions live in a
        // /Positioning group containing a compound 'geometryValues'
        // dataset with members 'latitude' and 'longitude'. Some legacy
        // tooling places the group under /SurfaceCurrent/SurfaceCurrent.NN;
        // accept either.
        IHdf5Group? positioningGroup = null;
        if (root.GroupNames.Contains("Positioning"))
        {
            positioningGroup = root.OpenGroup("Positioning");
        }
        else if (root.GroupNames.Contains("SurfaceCurrent"))
        {
            var sc = root.OpenGroup("SurfaceCurrent");
            foreach (var name in sc.GroupNames)
            {
                if (!name.StartsWith("SurfaceCurrent.", StringComparison.Ordinal)) continue;
                var inst = sc.OpenGroup(name);
                if (inst.GroupNames.Contains("Positioning"))
                {
                    positioningGroup = inst.OpenGroup("Positioning");
                    break;
                }
            }
        }

        if (positioningGroup is null)
        {
            throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: "/Positioning",
                attributeOrDataset: "Positioning/geometryValues",
                specReference: "S-111 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, "/Positioning", "Positioning/geometryValues",
                    "S-111 Edition 2.0.0 §10.2.3"));
        }

        RawCompoundDataset raw;
        try
        {
            raw = positioningGroup.ReadRawCompoundDataset("geometryValues");
        }
        catch (Exception ex)
        {
            throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: "/Positioning",
                attributeOrDataset: "Positioning/geometryValues",
                specReference: "S-111 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, "/Positioning", "Positioning/geometryValues",
                    "S-111 Edition 2.0.0 §10.2.3"),
                innerException: ex);
        }

        var latMember = raw.FindMember("latitude", "Latitude", "lat", "Lat")
            ?? throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: "/Positioning/geometryValues",
                attributeOrDataset: "latitude",
                specReference: "S-111 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, "/Positioning/geometryValues", "latitude",
                    "S-111 Edition 2.0.0 §10.2.3"));

        var lonMember = raw.FindMember("longitude", "Longitude", "long", "Long", "lon", "Lon")
            ?? throw new S100DatasetSchemaException(
                product: "S-111",
                file: null,
                groupPath: "/Positioning/geometryValues",
                attributeOrDataset: "longitude",
                specReference: "S-111 Edition 2.0.0 §10.2.3",
                message: ExceptionMessageFormatter.FormatSchema(
                    "S-111", null, "/Positioning/geometryValues", "longitude",
                    "S-111 Edition 2.0.0 §10.2.3"));

        var positions = new List<GeoPosition>(raw.RecordCount);
        var span = raw.Data.AsSpan();
        for (int i = 0; i < raw.RecordCount; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);
            double lat = ReadFloatingPointMember(record, latMember);
            double lon = ReadFloatingPointMember(record, lonMember);
            positions.Add(new GeoPosition(lat, lon));
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
                $"S-111 Positioning member '{member.Name}' has unsupported kind {member.Kind}."),
        };

    private static void ReadStationInstance(
        IHdf5Group instance,
        string instancePath,
        IReadOnlyList<GeoPosition> positions,
        List<SurfaceCurrentStation> stations)
    {
        const string spec = "S-111 Edition 2.0.0 §10.2.7";

        int numberOfStations = instance.AttributeExists("numberOfStations")
            ? (int)instance.ReadInt64Attribute("numberOfStations")
            : 0;

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
                "startDateTime", "S-111", null, groupPath, spec);
            string endStr = group.ReadRequiredStringAttribute(
                "endDateTime", "S-111", null, groupPath, spec);

            DateTime startTime = ParseTimestamp(startStr);
            DateTime endTime = ParseTimestamp(endStr);

            int numberOfTimes = (int)group.ReadRequiredInt64Attribute(
                "numberOfTimes", "S-111", null, groupPath, spec);
            long intervalSeconds = group.ReadRequiredInt64Attribute(
                "timeRecordInterval", "S-111", null, groupPath, spec);
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            var (speeds, directions) = ReadStationValues(group, numberOfTimes);

            if (stationIndex >= positions.Count)
            {
                throw new S100DatasetSchemaException(
                    product: "S-111",
                    file: null,
                    groupPath: "/Positioning/geometryValues",
                    attributeOrDataset: "Positioning/geometryValues",
                    specReference: "S-111 Edition 2.0.0 §10.2.3",
                    message: ExceptionMessageFormatter.FormatSchema(
                        "S-111", null, "/Positioning/geometryValues", "Positioning/geometryValues",
                        "S-111 Edition 2.0.0 §10.2.3")
                    + $" Position row {stationIndex} missing for station '{stationId}'.");
            }
            var (lat, lon) = positions[stationIndex];

            stations.Add(new SurfaceCurrentStation
            {
                Identifier = stationId,
                Latitude = lat,
                Longitude = lon,
                StartTime = startTime,
                EndTime = endTime,
                TimeRecordInterval = interval,
                NumberOfTimes = numberOfTimes,
                SpeedsMetresPerSecond = speeds,
                DirectionsDegreesTrue = directions,
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
            [
                "yyyyMMdd'T'HHmmss'Z'",
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                "yyyyMMdd'T'HH:mm:ss'Z'",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static (float[] Speeds, float[] Directions) ReadStationValues(IHdf5Group group, int numberOfTimes)
    {
        var raw = group.ReadRawCompoundDataset("values");

        var speedMember = raw.FindMember("surfaceCurrentSpeed", "speed", "Speed")
            ?? throw new InvalidOperationException(
                "S-111 dcf8 station 'values' compound is missing a speed member " +
                "(expected 'surfaceCurrentSpeed', 'speed', or 'Speed').");

        var directionMember = raw.FindMember("surfaceCurrentDirection", "direction", "Direction")
            ?? throw new InvalidOperationException(
                "S-111 dcf8 station 'values' compound is missing a direction member " +
                "(expected 'surfaceCurrentDirection', 'direction', or 'Direction').");

        int count = Math.Min(raw.RecordCount, numberOfTimes);
        var speeds = new float[count];
        var directions = new float[count];
        var span = raw.Data.AsSpan();

        for (int i = 0; i < count; i++)
        {
            var record = span.Slice(i * raw.RecordSize, raw.RecordSize);
            speeds[i] = ReadFloat(record, speedMember);
            directions[i] = ReadFloat(record, directionMember);
        }

        return (speeds, directions);
    }
}
