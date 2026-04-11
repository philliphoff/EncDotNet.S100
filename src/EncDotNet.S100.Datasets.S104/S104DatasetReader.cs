using System.Globalization;
using EncDotNet.S100.Hdf5;

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
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalCRS")
            ? root.ReadAttribute<int>("horizontalCRS")
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
            ? wlGroup.ReadAttribute<byte>("dataCodingFormat")
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
        double originLat = instance.ReadAttribute<double>("gridOriginLatitude");
        double originLon = instance.ReadAttribute<double>("gridOriginLongitude");
        double spacingLat = instance.ReadAttribute<double>("gridSpacingLatitudinal");
        double spacingLon = instance.ReadAttribute<double>("gridSpacingLongitudinal");
        int numLat = (int)instance.ReadAttribute<uint>("numPointsLatitudinal");
        int numLon = (int)instance.ReadAttribute<uint>("numPointsLongitudinal");

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

            var values = group.ReadDataset<WaterLevelValue>("values");

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
}
