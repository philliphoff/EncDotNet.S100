using EncDotNet.S100.Core;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Pipelines;
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

        int? horizontalCRS = ResolveHorizontalCrs(root);

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

        // S-100 Part 10c §10.2.1 — the root carries the productSpecification
        // string (e.g. "INT.IHO.S-102.3.0.0"); surfaced so the pipeline can
        // report the declared edition and warn on a version mismatch.
        string? productSpecification = root.AttributeExists("productSpecification")
            ? root.ReadStringAttribute("productSpecification")
            : null;

        var coverages = ReadCoverages(root);

        return new S102Dataset
        {
            HorizontalCRS = horizontalCRS,
            VerticalDatum = verticalDatum,
            Epoch = epoch,
            GeographicIdentifier = geographicIdentifier,
            IssueDate = issueDate,
            Metadata = metadata,
            DeclaredProductSpecification = productSpecification,
            Coverages = coverages,
        };
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for the S-102
    /// dataset — its declared specification, horizontal CRS, and geographic
    /// extent — <em>without</em> reading the (potentially very large)
    /// <c>values</c> depth/uncertainty arrays. Used by hosts that need to
    /// place the dataset on a map and decide whether to load it in full
    /// (phased / deferred loading; issue #460).
    /// </summary>
    /// <remarks>
    /// The extent is the union of every <c>BathymetryCoverage.NN</c>
    /// instance's grid footprint, computed from the mandatory grid-georef
    /// attributes (S-100 Part 10c §10.2.1.2) exactly as
    /// <c>S102CoverageSource.Metadata.Extent</c> derives it — so
    /// <see cref="DatasetMetadata.Extent"/> matches the full-load extent. The
    /// edge values are in the dataset's native CRS
    /// (<see cref="DatasetMetadata.HorizontalCrsEpsg"/>), which may be a
    /// projected UTM system rather than WGS-84. S-102 is static and carries
    /// no display-scale window, so <see cref="DatasetMetadata.DisplayScale"/>
    /// and <see cref="DatasetMetadata.TimeCoverage"/> are always <c>null</c>.
    /// </remarks>
    public static DatasetMetadata ReadMetadata(IHdf5File file)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.readmetadata");
        __activity?.SetTag("s100.product", "S-102");
        ArgumentNullException.ThrowIfNull(file);

        var root = file.Root;

        int? horizontalCRS = ResolveHorizontalCrs(root);

        string? productSpecification = root.AttributeExists("productSpecification")
            ? root.ReadStringAttribute("productSpecification")
            : null;

        BoundingBox? extent = ReadUnionExtent(root);

        return new DatasetMetadata
        {
            Spec = HdfDeclaredSpec.Resolve(productSpecification, "S-102"),
            Extent = extent,
            HorizontalCrsEpsg = horizontalCRS,
        };
    }

    /// <summary>
    /// Resolves the horizontal CRS EPSG code from the root group. S-102
    /// editions disagree on the attribute name: Edition 3.0.0 uses
    /// <c>horizontalCRS</c>, whereas the older Edition 2.1 (and the S-100
    /// Part 10c gridded-coverage profile it built on) uses
    /// <c>horizontalDatumValue</c> alongside <c>horizontalDatumReference</c>
    /// (= "EPSG"). Prefer the newer name when both exist. (See issue #239.)
    /// </summary>
    private static int? ResolveHorizontalCrs(IHdf5Group root) =>
        root.AttributeExists("horizontalCRS") ? (int)root.ReadInt64Attribute("horizontalCRS")
        : root.AttributeExists("horizontalDatumValue") ? (int)root.ReadInt64Attribute("horizontalDatumValue")
        : null;

    /// <summary>
    /// Computes the union grid footprint of every <c>BathymetryCoverage.NN</c>
    /// instance from georef attributes alone (no <c>values</c> read), or
    /// <c>null</c> when the dataset carries no coverage instances.
    /// </summary>
    private static BoundingBox? ReadUnionExtent(IHdf5Group root)
    {
        var bcGroup = root.OpenGroup("BathymetryCoverage");

        double south = double.MaxValue, west = double.MaxValue;
        double north = double.MinValue, east = double.MinValue;
        bool any = false;

        foreach (var instanceName in bcGroup.GroupNames)
        {
            if (!instanceName.StartsWith("BathymetryCoverage.", StringComparison.Ordinal))
                continue;

            var instance = bcGroup.OpenGroup(instanceName);
            var g = ReadCoverageGeoref(instance, $"/BathymetryCoverage/{instanceName}");

            // Grid origin is the node position of the first grid point; the far
            // edge is origin + spacing * count. Take min/max so a negative
            // spacing (origin at the north/east edge) still yields a correct box.
            double latA = g.OriginLatitude;
            double latB = g.OriginLatitude + g.SpacingLatitudinal * g.NumPointsLatitudinal;
            double lonA = g.OriginLongitude;
            double lonB = g.OriginLongitude + g.SpacingLongitudinal * g.NumPointsLongitudinal;

            south = Math.Min(south, Math.Min(latA, latB));
            north = Math.Max(north, Math.Max(latA, latB));
            west = Math.Min(west, Math.Min(lonA, lonB));
            east = Math.Max(east, Math.Max(lonA, lonB));
            any = true;
        }

        return any ? new BoundingBox(south, west, north, east) : null;
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
        var g = ReadCoverageGeoref(instance, instancePath);

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
            OriginLatitude = g.OriginLatitude,
            OriginLongitude = g.OriginLongitude,
            SpacingLatitudinal = g.SpacingLatitudinal,
            SpacingLongitudinal = g.SpacingLongitudinal,
            NumPointsLatitudinal = g.NumPointsLatitudinal,
            NumPointsLongitudinal = g.NumPointsLongitudinal,
            StartSequence = startSequence,
            GroupPath = instancePath,
            Values = allValues.ToArray(),
        };
    }

    /// <summary>
    /// Reads the mandatory grid-georef attributes of a
    /// <c>BathymetryCoverage.NN</c> instance (S-100 Part 10c §10.2.1.2)
    /// without touching its <c>values</c> arrays. Shared by <see cref="Read"/>
    /// (which then reads the values) and <see cref="ReadMetadata"/> (which
    /// does not), so the two paths compute an identical extent.
    /// </summary>
    private static CoverageGeoref ReadCoverageGeoref(IHdf5Group instance, string instancePath)
    {
        const string Spec = "S-100 Part 10c §10.2.1.2";
        return new CoverageGeoref(
            instance.ReadRequiredDoubleAttribute("gridOriginLatitude", "S-102", null, instancePath, Spec),
            instance.ReadRequiredDoubleAttribute("gridOriginLongitude", "S-102", null, instancePath, Spec),
            instance.ReadRequiredDoubleAttribute("gridSpacingLatitudinal", "S-102", null, instancePath, Spec),
            instance.ReadRequiredDoubleAttribute("gridSpacingLongitudinal", "S-102", null, instancePath, Spec),
            (int)instance.ReadRequiredInt64Attribute("numPointsLatitudinal", "S-102", null, instancePath, Spec),
            (int)instance.ReadRequiredInt64Attribute("numPointsLongitudinal", "S-102", null, instancePath, Spec));
    }

    /// <summary>Grid-georef of a single bathymetry coverage instance.</summary>
    private readonly record struct CoverageGeoref(
        double OriginLatitude,
        double OriginLongitude,
        double SpacingLatitudinal,
        double SpacingLongitudinal,
        int NumPointsLatitudinal,
        int NumPointsLongitudinal);
}
