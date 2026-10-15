using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Reads the extent an S-100 HDF5 dataset declares, for the axis-order check
/// (<see cref="EncDotNet.S100.DataModel.AxisOrderCheck"/>, issue #650).
/// </summary>
public static class Hdf5DeclaredExtent
{
    /// <summary>
    /// Reads the bounding-box attributes (<c>westBoundLongitude</c>,
    /// <c>eastBoundLongitude</c>, <c>southBoundLatitude</c>,
    /// <c>northBoundLatitude</c>; S-100 Part 10c §10.2.1) of
    /// <paramref name="group"/>, or returns <c>null</c> unless all four are
    /// present and readable.
    /// </summary>
    public static BoundingBox? Read(IHdf5Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (TryRead(group, "southBoundLatitude") is not { } south
            || TryRead(group, "westBoundLongitude") is not { } west
            || TryRead(group, "northBoundLatitude") is not { } north
            || TryRead(group, "eastBoundLongitude") is not { } east)
        {
            return null;
        }

        return new BoundingBox(south, west, north, east);
    }

    /// <summary>
    /// Reads the dataset's declared extent: the root bounding box, or, when
    /// the root declares none, the union of the bounding boxes of the feature
    /// instance groups (<paramref name="instancePrefix"/><c>NN</c>) under
    /// <paramref name="featureContainer"/>.
    /// </summary>
    /// <param name="root">The file's root group.</param>
    /// <param name="featureContainer">The feature container group (e.g. <c>/WaterLevel</c>), or <c>null</c>.</param>
    /// <param name="instancePrefix">The instance group prefix (e.g. <c>"WaterLevel."</c>).</param>
    public static BoundingBox? ReadDatasetExtent(IHdf5Group root, IHdf5Group? featureContainer, string instancePrefix)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Read(root) is { } rootExtent)
            return rootExtent;
        if (featureContainer is null)
            return null;

        BoundingBox? union = null;
        foreach (var name in featureContainer.GroupNames)
        {
            if (!name.StartsWith(instancePrefix, StringComparison.Ordinal))
                continue;
            if (Read(featureContainer.OpenGroup(name)) is not { } box)
                continue;

            union = union is null
                ? box
                : new BoundingBox(
                    Math.Min(union.SouthLatitude, box.SouthLatitude),
                    Math.Min(union.WestLongitude, box.WestLongitude),
                    Math.Max(union.NorthLatitude, box.NorthLatitude),
                    Math.Max(union.EastLongitude, box.EastLongitude));
        }

        return union;
    }

    private static double? TryRead(IHdf5Group group, string name)
    {
        if (!group.AttributeExists(name))
            return null;

        try
        {
            return group.ReadDoubleAttribute(name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An unreadable bounding box is no anchor; the check reports
            // the dataset as unverified rather than failing the read.
            return null;
        }
    }
}
