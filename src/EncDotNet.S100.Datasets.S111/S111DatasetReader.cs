using System.Globalization;
using EncDotNet.S100.Hdf5;

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
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = root.AttributeExists("horizontalDatumValue")
            ? root.ReadAttribute<int>("horizontalDatumValue")
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
            ? root.ReadAttribute<float>("surfaceCurrentDepth")
            : null;

        var scGroup = root.OpenGroup("SurfaceCurrent");

        int dataCodingFormat = scGroup.AttributeExists("dataCodingFormat")
            ? scGroup.ReadAttribute<byte>("dataCodingFormat")
            : 2;

        int? typeOfCurrentData = scGroup.AttributeExists("typeOfCurrentData")
            ? scGroup.ReadAttribute<byte>("typeOfCurrentData")
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
        double originLat = instance.ReadAttribute<float>("gridOriginLatitude");
        double originLon = instance.ReadAttribute<float>("gridOriginLongitude");
        double spacingLat = instance.ReadAttribute<float>("gridSpacingLatitudinal");
        double spacingLon = instance.ReadAttribute<float>("gridSpacingLongitudinal");
        int numLat = instance.ReadAttribute<int>("numPointsLatitudinal");
        int numLon = instance.ReadAttribute<int>("numPointsLongitudinal");

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

            var values = group.ReadDataset<SurfaceCurrentValue>("values");

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
}
