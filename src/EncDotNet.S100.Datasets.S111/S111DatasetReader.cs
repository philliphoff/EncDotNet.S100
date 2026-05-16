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

        int dataCodingFormat = scGroup.AttributeExists("dataCodingFormat")
            ? (int)scGroup.ReadInt64Attribute("dataCodingFormat")
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
            throw new NotSupportedException(
                $"Data coding format {dataCodingFormat} is not yet supported. Only format 2 (regular grid) is implemented.");
        }

        var coverages = new List<SurfaceCurrentCoverage>();

        foreach (var instanceName in scGroup.GroupNames)
        {
            if (!instanceName.StartsWith("SurfaceCurrent.", StringComparison.Ordinal))
                continue;

            var instance = scGroup.OpenGroup(instanceName);
            ReadInstance(instance, coverages);
        }

        return coverages;
    }

    private static void ReadInstance(IHdf5Group instance, List<SurfaceCurrentCoverage> coverages)
    {
        double originLat = instance.ReadDoubleAttribute("gridOriginLatitude");
        double originLon = instance.ReadDoubleAttribute("gridOriginLongitude");
        double spacingLat = instance.ReadDoubleAttribute("gridSpacingLatitudinal");
        double spacingLon = instance.ReadDoubleAttribute("gridSpacingLongitudinal");
        int numLat = (int)instance.ReadInt64Attribute("numPointsLatitudinal");
        int numLon = (int)instance.ReadInt64Attribute("numPointsLongitudinal");

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
