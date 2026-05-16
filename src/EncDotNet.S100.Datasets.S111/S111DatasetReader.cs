using System.Globalization;
using EncDotNet.S100.Hdf5;
using S100Diag = EncDotNet.S100.Datasets.S111.Diagnostics;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Reads an S-111 Surface Currents dataset from an HDF5 file via the
/// <see cref="IHdf5File"/> abstraction. Currently supports data coding format 2
/// (regular grid) only.
/// </summary>
public static class S111DatasetReader
{
    /// <summary>
    /// Reads an <see cref="S111Dataset"/> from the given HDF5 file.
    /// </summary>
    public static S111Dataset Read(IHdf5File file)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-111");
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalDatumValue")
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

        float? surfaceCurrentDepth = root.AttributeExists("surfaceCurrentDepth")
            ? (float)root.ReadDoubleAttribute("surfaceCurrentDepth")
            : null;

        var scGroup = root.OpenGroup("SurfaceCurrent");
        const string SurfaceCurrentPath = "/SurfaceCurrent";

        // S-111 Edition 2.0.0 §12.2 — every SurfaceCurrent container
        // carries a dataCodingFormat enum that selects the per-instance
        // layout.
        int dataCodingFormat = scGroup.AttributeExists("dataCodingFormat")
            ? (int)scGroup.ReadRequiredInt64Attribute(
                "dataCodingFormat",
                product: "S-111",
                file: null,
                groupPath: SurfaceCurrentPath,
                specReference: "S-100 Part 10c §10.2.1")
            : 2;

        int? typeOfCurrentData = scGroup.AttributeExists("typeOfCurrentData")
            ? (int)scGroup.ReadInt64Attribute("typeOfCurrentData")
            : null;

        var coverages = ReadCoverages(scGroup, dataCodingFormat);

        return new S111Dataset
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
        };
    }

    private static List<SurfaceCurrentCoverage> ReadCoverages(IHdf5Group scGroup, int dataCodingFormat)
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
            ReadInstance(instance, coverages, $"/SurfaceCurrent/{instanceName}");
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

    private static void ReadInstance(IHdf5Group instance, List<SurfaceCurrentCoverage> coverages, string instancePath)
    {
        // S-100 Part 10c §10.2.1.2 — the grid-georef attributes are
        // required on every dcf2 SurfaceCurrent.NN instance group.
        const string Spec = "S-100 Part 10c §10.2.1.2";
        double originLat = instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-111", null, instancePath, Spec);
        double originLon = instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-111", null, instancePath, Spec);
        double spacingLat = instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-111", null, instancePath, Spec);
        double spacingLon = instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-111", null, instancePath, Spec);
        int numLat = (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-111", null, instancePath, Spec);
        int numLon = (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-111", null, instancePath, Spec);

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

            coverages.Add(new SurfaceCurrentCoverage
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
}
