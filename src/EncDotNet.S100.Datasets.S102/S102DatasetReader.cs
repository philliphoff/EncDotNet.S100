namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// Reads an S-102 Bathymetric Surface dataset from an HDF5 file via the
/// <see cref="IHdf5File"/> abstraction.
/// </summary>
public static class S102DatasetReader
{
    /// <summary>
    /// Reads an <see cref="S102Dataset"/> from the given HDF5 file.
    /// </summary>
    public static S102Dataset Read(IHdf5File file)
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

        var coverages = ReadCoverages(root);

        return new S102Dataset
        {
            HorizontalCRS = horizontalCRS,
            Epoch = epoch,
            GeographicIdentifier = geographicIdentifier,
            IssueDate = issueDate,
            Metadata = metadata,
            Coverages = coverages,
        };
    }

    private static List<BathymetryCoverage> ReadCoverages(IHdf5Group root)
    {
        var bcGroup = root.OpenGroup("BathymetryCoverage");
        var coverages = new List<BathymetryCoverage>();

        foreach (var instanceName in bcGroup.GroupNames)
        {
            if (!instanceName.StartsWith("BathymetryCoverage.", StringComparison.Ordinal))
                continue;

            var instance = bcGroup.OpenGroup(instanceName);
            coverages.Add(ReadCoverage(instance));
        }

        return coverages;
    }

    private static BathymetryCoverage ReadCoverage(IHdf5Group instance)
    {
        double originLat = instance.ReadAttribute<double>("gridOriginLatitude");
        double originLon = instance.ReadAttribute<double>("gridOriginLongitude");
        double spacingLat = instance.ReadAttribute<double>("gridSpacingLatitudinal");
        double spacingLon = instance.ReadAttribute<double>("gridSpacingLongitudinal");
        int numLat = instance.ReadAttribute<int>("numPointsLatitudinal");
        int numLon = instance.ReadAttribute<int>("numPointsLongitudinal");

        string? startSequence = instance.AttributeExists("startSequence")
            ? instance.ReadStringAttribute("startSequence")
            : null;

        // Collect values from all sub-groups (Group_001, Group_002, etc.)
        var allValues = new List<BathymetryValue>();

        foreach (var groupName in instance.GroupNames)
        {
            if (!groupName.StartsWith("Group_", StringComparison.Ordinal))
                continue;

            var group = instance.OpenGroup(groupName);
            var values = group.ReadDataset<BathymetryValue>("values");
            allValues.AddRange(values);
        }

        return new BathymetryCoverage
        {
            OriginLatitude = originLat,
            OriginLongitude = originLon,
            SpacingLatitudinal = spacingLat,
            SpacingLongitudinal = spacingLon,
            NumPointsLatitudinal = numLat,
            NumPointsLongitudinal = numLon,
            StartSequence = startSequence,
            Values = allValues.ToArray(),
        };
    }
}
