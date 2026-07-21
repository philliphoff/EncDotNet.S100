using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Pipelines;
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

        int? verticalDatum = root.AttributeExists("verticalDatum")
            ? (int)root.ReadInt64Attribute("verticalDatum")
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

        // S-100 Part 10c §10.2.1 — the root carries the productSpecification
        // string (e.g. "INT.IHO.S-104.2.0"). This reader implements the
        // Edition 2.0.0 layout; a dataset that declares a different edition
        // may legitimately encode attributes differently, so we surface the
        // declared edition when a schema failure occurs below.
        string? productSpecification = root.AttributeExists("productSpecification")
            ? root.ReadStringAttribute("productSpecification")
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
            var stations = ReadStationSeriesGuarded(root, wlGroup, productSpecification);
            DateTime? minTime = null, maxTime = null;
            foreach (var s in stations)
            {
                if (minTime is null || s.StartTime < minTime) minTime = s.StartTime;
                if (maxTime is null || s.EndTime > maxTime) maxTime = s.EndTime;
            }

            return new S104DatasetData.StationSeries(new S104StationSeriesDataset
            {
                HorizontalCRS = horizontalCRS,
                VerticalDatum = verticalDatum,
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
            })
            {
                DeclaredProductSpecification = productSpecification,
            };
        }

        var coverages = ReadCoveragesGuarded(wlGroup, dataCodingFormat, productSpecification);

        return new S104DatasetData.GriddedCoverage(new S104Dataset
        {
            HorizontalCRS = horizontalCRS,
            VerticalDatum = verticalDatum,
            Epoch = epoch,
            GeographicIdentifier = geographicIdentifier,
            IssueDate = issueDate,
            Metadata = metadata,
            DataCodingFormat = dataCodingFormat,
            MethodWaterLevelProduct = methodWaterLevelProduct,
            Coverages = coverages,
        })
        {
            DeclaredProductSpecification = productSpecification,
        };
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for the S-104
    /// dataset — its declared specification, horizontal CRS, geographic
    /// extent, and temporal coverage — for phased / deferred loading (issue
    /// #460).
    /// </summary>
    /// <remarks>
    /// For data coding format 2 (regularly-gridded) the extent is the union
    /// of the <c>WaterLevel.NN</c> grid footprints and the time coverage
    /// spans the <c>timePoint</c> attributes of every <c>Group_NNN</c> step,
    /// read <em>without</em> touching the per-step <c>values</c> arrays. For
    /// data coding format 8 (time series at fixed stations) the comparatively
    /// small station series is read in full and the extent / time span are
    /// derived from the station positions and record windows.
    /// </remarks>
    public static DatasetMetadata ReadMetadata(IHdf5File file)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.readmetadata");
        __activity?.SetTag("s100.product", "S-104");
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalCRS")
            ? (int)root.ReadInt64Attribute("horizontalCRS")
            : null;

        string? productSpecification = root.AttributeExists("productSpecification")
            ? root.ReadStringAttribute("productSpecification")
            : null;

        var wlGroup = root.OpenGroup("WaterLevel");
        const string WaterLevelPath = "/WaterLevel";

        int dataCodingFormat = wlGroup.AttributeExists("dataCodingFormat")
            ? (int)wlGroup.ReadRequiredInt64Attribute(
                "dataCodingFormat", "S-104", null, WaterLevelPath, "S-100 Part 10c §10.2.1")
            : 2;

        BoundingBox? extent;
        TimeCoverage? time;

        if (dataCodingFormat == 8)
        {
            var stations = ReadStationSeriesGuarded(root, wlGroup, productSpecification);
            extent = StationExtent(stations);
            time = StationTimeCoverage(stations);
        }
        else
        {
            (extent, time) = ReadGriddedExtentAndTime(wlGroup);
        }

        return new DatasetMetadata
        {
            Spec = HdfDeclaredSpec.Resolve(productSpecification, "S-104"),
            Extent = extent,
            HorizontalCrsEpsg = horizontalCRS,
            TimeCoverage = time,
        };
    }

    /// <summary>
    /// Computes the union grid extent and time-step span of a dcf2 dataset
    /// from georef and <c>timePoint</c> attributes alone (no <c>values</c>
    /// read), so the result matches a full read's extent and available times.
    /// </summary>
    private static (BoundingBox? Extent, TimeCoverage? Time) ReadGriddedExtentAndTime(IHdf5Group wlGroup)
    {
        double south = double.MaxValue, west = double.MaxValue;
        double north = double.MinValue, east = double.MinValue;
        DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
        bool anyBounds = false, anyTime = false;

        foreach (var instanceName in wlGroup.GroupNames)
        {
            if (!instanceName.StartsWith("WaterLevel.", StringComparison.Ordinal))
                continue;

            var instance = wlGroup.OpenGroup(instanceName);
            var instancePath = $"/WaterLevel/{instanceName}";
            const string Spec = "S-100 Part 10c §10.2.1.2";

            double originLat = instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-104", null, instancePath, Spec);
            double originLon = instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-104", null, instancePath, Spec);
            double spacingLat = instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-104", null, instancePath, Spec);
            double spacingLon = instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-104", null, instancePath, Spec);
            int numLat = (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-104", null, instancePath, Spec);
            int numLon = (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-104", null, instancePath, Spec);

            double latA = originLat, latB = originLat + spacingLat * numLat;
            double lonA = originLon, lonB = originLon + spacingLon * numLon;
            south = Math.Min(south, Math.Min(latA, latB));
            north = Math.Max(north, Math.Max(latA, latB));
            west = Math.Min(west, Math.Min(lonA, lonB));
            east = Math.Max(east, Math.Max(lonA, lonB));
            anyBounds = true;

            foreach (var groupName in instance.GroupNames)
            {
                if (!groupName.StartsWith("Group_", StringComparison.Ordinal))
                    continue;

                var group = instance.OpenGroup(groupName);
                var t = ParseTimePoint(group.ReadStringAttribute("timePoint"));
                if (t < min) min = t;
                if (t > max) max = t;
                anyTime = true;
            }
        }

        BoundingBox? extent = anyBounds ? new BoundingBox(south, west, north, east) : null;
        TimeCoverage? time = anyTime ? new TimeCoverage(min, max) : null;
        return (extent, time);
    }

    private static BoundingBox? StationExtent(IReadOnlyList<WaterLevelStation> stations)
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

    private static TimeCoverage? StationTimeCoverage(IReadOnlyList<WaterLevelStation> stations)
    {
        DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
        bool any = false;
        foreach (var s in stations)
        {
            if (s.StartTime is { } start) { if (start < min) min = start; any = true; }
            if (s.EndTime is { } end) { if (end > max) max = end; any = true; }
        }

        return any ? new TimeCoverage(min, max) : null;
    }

    /// <summary>
    /// Parses an S-104 <c>timePoint</c> attribute (S-100 Part 10c) accepting
    /// both the compact <c>yyyyMMddTHHmmssZ</c> and extended
    /// <c>yyyy-MM-ddTHH:mm:ssZ</c> ISO-8601 forms seen in production files.
    /// </summary>
    private static DateTime ParseTimePoint(string timePointStr) =>
        DateTime.ParseExact(
            timePointStr,
            ["yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>
    /// Runs <see cref="ReadCoverages"/>, enriching any
    /// <see cref="S100DatasetSchemaException"/> with a note about the
    /// dataset's declared product-specification edition when that edition
    /// is not the Edition 2.0.0 this reader implements (see
    /// <see cref="DeclaredEditionMismatchNote"/>).
    /// </summary>
    private static List<WaterLevelCoverage> ReadCoveragesGuarded(
        IHdf5Group wlGroup, int dataCodingFormat, string? productSpecification)
    {
        try
        {
            return ReadCoverages(wlGroup, dataCodingFormat);
        }
        catch (S100DatasetSchemaException ex) when (DeclaredEditionMismatchNote(productSpecification) is { } note)
        {
            throw ex.WithAdditionalContext(note);
        }
    }

    /// <summary>
    /// Runs <see cref="ReadStationSeries"/>, enriching any
    /// <see cref="S100DatasetSchemaException"/> with the declared-edition
    /// note when the dataset targets an edition other than 2.0.0.
    /// </summary>
    private static IReadOnlyList<WaterLevelStation> ReadStationSeriesGuarded(
        IHdf5Group root, IHdf5Group wlGroup, string? productSpecification)
    {
        try
        {
            return ReadStationSeries(root, wlGroup);
        }
        catch (S100DatasetSchemaException ex) when (DeclaredEditionMismatchNote(productSpecification) is { } note)
        {
            throw ex.WithAdditionalContext(note);
        }
    }

    /// <summary>
    /// Returns a human-readable note when <paramref name="productSpecification"/>
    /// declares an S-104 edition other than the 2.0.0 layout this reader
    /// implements (e.g. the draft <c>INT.IHO.S-104.0.8</c>), or
    /// <c>null</c> when the declared edition is absent, unparseable, or
    /// already 2.x. The note explains that the expected attribute layout
    /// may differ for the declared edition.
    /// </summary>
    private static string? DeclaredEditionMismatchNote(string? productSpecification)
    {
        if (string.IsNullOrWhiteSpace(productSpecification))
            return null;

        if (!TryParseEditionMajor(productSpecification, out int major))
            return null;

        if (major == 2)
            return null;

        return $"The dataset declares productSpecification '{productSpecification}', " +
            "a different edition than the S-104 Edition 2.0.0 layout this build implements, " +
            "which may explain the unexpected attribute layout.";
    }

    /// <summary>
    /// Extracts the leading numeric edition component from a
    /// productSpecification string of the form
    /// <c>INT.IHO.S-104.&lt;major&gt;[.&lt;minor&gt;]</c> (S-100 Part 10c
    /// §10.2.1). Returns <c>false</c> when no numeric edition can be found.
    /// </summary>
    private static bool TryParseEditionMajor(string productSpecification, out int major)
    {
        major = 0;
        // The edition trails the product code, e.g. "INT.IHO.S-104.0.8".
        var tokens = productSpecification.Split('.');
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            // Walk back to the first numeric token; its predecessor chain
            // up to the product code holds the edition. The first numeric
            // token after the product code ("S-104") is the major version.
            if (int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;

            // tokens[i] is the last non-numeric token (the product code);
            // the next token is the major edition.
            if (i + 1 < tokens.Length &&
                int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out major))
            {
                return true;
            }

            return false;
        }

        return false;
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

            DateTime timePoint = ParseTimePoint(group.ReadStringAttribute("timePoint"));

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
                GroupPath = instancePath,
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
    /// <c>waterLevelTrend</c> is spec-encoded as a uint8 enumeration (S-100
    /// Part 10c permits small-integer storage), but producers vary: UKHO dcf2
    /// files store it as <c>f32</c>, and IC-ENC NL feeds store it as
    /// <c>Int16</c>. All small-integer and floating widths are decoded via
    /// <see cref="DecodeTrend"/>; an unrecognised trend width is dropped to
    /// <c>0</c> (nodata) rather than failing the whole read, since the trend is
    /// auxiliary to the renderable water-level height (issue #254).
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

            byte trend = trendMember is null ? (byte)0 : DecodeTrend(trendMember, record);

            result[i] = new WaterLevelValue(height, trend);
        }

        return result;
    }

    /// <summary>
    /// Decodes a single <c>waterLevelTrend</c> compound member into the byte
    /// trend enumeration (0 nodata / 1 decreasing / 2 increasing / 3 steady).
    /// </summary>
    /// <remarks>
    /// S-104 Edition 2.0.0 §10.2.6 / S-100 Part 10c §10.2.1 — the trend is an
    /// enumerated indicator that producers may store in any small numeric
    /// width. All signed/unsigned 8/16/32-bit integer kinds and both float
    /// widths are accepted (clamped to <c>0..255</c>). Any other kind is
    /// dropped to <c>0</c> (nodata) rather than throwing, so a renderable
    /// water-level height grid is never lost to an unrepresentable trend
    /// (issue #254).
    /// </remarks>
    private static byte DecodeTrend(CompoundMemberInfo trendMember, ReadOnlySpan<byte> record)
    {
        var field = record.Slice(trendMember.Offset, trendMember.Size);
        return trendMember.Kind switch
        {
            CompoundMemberKind.UInt8 or CompoundMemberKind.Int8 => field[0],
            CompoundMemberKind.Int16 => (byte)Math.Clamp(
                (int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(field), 0, 255),
            CompoundMemberKind.UInt16 => (byte)Math.Clamp(
                (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(field), 0, 255),
            CompoundMemberKind.Int32 => (byte)Math.Clamp(
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(field), 0, 255),
            CompoundMemberKind.UInt32 => (byte)Math.Clamp(
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field), 0u, 255u),
            // UKHO dcf2 stores trend as f32 — round to nearest valid enum byte.
            CompoundMemberKind.Float32 => (byte)Math.Clamp(
                (int)Math.Round(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(field)), 0, 255),
            CompoundMemberKind.Float64 => (byte)Math.Clamp(
                (int)Math.Round(System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(field)), 0, 255),
            // Drop an unrepresentable trend rather than failing the whole read.
            _ => (byte)0,
        };
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

    private static List<GeoPosition> ReadStationPositions(IHdf5Group root)
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
                $"S-104 Positioning member '{member.Name}' has unsupported kind {member.Kind}."),
        };

    private static void ReadStationInstance(
        IHdf5Group instance,
        string instancePath,
        IReadOnlyList<GeoPosition> positions,
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

            trends[i] = trendMember is null ? (byte)0 : DecodeTrend(trendMember, record);
        }

        return (heights, trends);
    }
}
