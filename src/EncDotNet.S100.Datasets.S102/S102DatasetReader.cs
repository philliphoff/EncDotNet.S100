using EncDotNet.S100.Hdf5;
using S100Diag = EncDotNet.S100.Datasets.S102.Diagnostics;

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
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-102");
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
            coverages.Add(ReadCoverage(instance, $"/BathymetryCoverage/{instanceName}"));
        }

        return coverages;
    }

    private static BathymetryCoverage ReadCoverage(IHdf5Group instance, string instancePath)
    {
        // S-102 Edition 3.0.0 §12.6 — BathymetryCoverage.NN groups carry
        // the mandatory grid-georef attributes inherited from the S-100
        // gridded-coverage profile (S-100 Part 10c §10.2.1.2).
        const string Spec = "S-100 Part 10c §10.2.1.2";
        double originLat = instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-102", null, instancePath, Spec);
        double originLon = instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-102", null, instancePath, Spec);
        double spacingLat = instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-102", null, instancePath, Spec);
        double spacingLon = instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-102", null, instancePath, Spec);
        int numLat = (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-102", null, instancePath, Spec);
        int numLon = (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-102", null, instancePath, Spec);

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
