using System.Globalization;
using EncDotNet.S100.Hdf5;
using S100Diag = EncDotNet.S100.Datasets.S104.Diagnostics;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// Reads an S-104 Water Level dataset from an HDF5 file via the
/// <see cref="IHdf5File"/> abstraction. Currently supports data coding format 2
/// (regular grid) only.
/// </summary>
public static class S104DatasetReader
{
    /// <summary>
    /// Reads an <see cref="S104Dataset"/> from the given HDF5 file.
    /// </summary>
    public static S104Dataset Read(IHdf5File file)
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

        var wlGroup = root.OpenGroup("WaterLevel");

        int dataCodingFormat = wlGroup.AttributeExists("dataCodingFormat")
            ? (int)wlGroup.ReadInt64Attribute("dataCodingFormat")
            : 2;

        string? methodWaterLevelProduct = wlGroup.AttributeExists("methodWaterLevelProduct")
            ? wlGroup.ReadStringAttribute("methodWaterLevelProduct")
            : null;

        var coverages = ReadCoverages(wlGroup, dataCodingFormat);

        return new S104Dataset
        {
            HorizontalCRS = horizontalCRS,
            Epoch = epoch,
            GeographicIdentifier = geographicIdentifier,
            IssueDate = issueDate,
            Metadata = metadata,
            DataCodingFormat = dataCodingFormat,
            MethodWaterLevelProduct = methodWaterLevelProduct,
            Coverages = coverages,
        };
    }

    private static List<WaterLevelCoverage> ReadCoverages(IHdf5Group wlGroup, int dataCodingFormat)
    {
        if (dataCodingFormat != 2)
        {
            throw new NotSupportedException(
                $"Data coding format {dataCodingFormat} is not yet supported. Only format 2 (regular grid) is implemented.");
        }

        var coverages = new List<WaterLevelCoverage>();

        foreach (var instanceName in wlGroup.GroupNames)
        {
            if (!instanceName.StartsWith("WaterLevel.", StringComparison.Ordinal))
                continue;

            var instance = wlGroup.OpenGroup(instanceName);
            ReadInstance(instance, coverages);
        }

        return coverages;
    }

    private static void ReadInstance(IHdf5Group instance, List<WaterLevelCoverage> coverages)
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
}
