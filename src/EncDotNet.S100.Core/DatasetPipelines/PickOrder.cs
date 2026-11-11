using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The pick report's order (S-98 15.1; S-164 FeatureInformation2): features
/// with the highest drawing priority in the portrayal first, and at equal
/// priority points, then curves, then surfaces. A feature with no drawing
/// priority (not drawn at the current settings, or from a dataset with no
/// portrayal) comes after every drawn one.
/// </summary>
public static class PickOrder
{
    /// <summary>
    /// Compares two drawing priorities: higher first, and a priority before
    /// none. Two features with no priority compare equal.
    /// </summary>
    /// <param name="a">The first feature's drawing priority, or <c>null</c>.</param>
    /// <param name="b">The second feature's drawing priority, or <c>null</c>.</param>
    /// <returns>Negative when <paramref name="a"/> comes first, positive when <paramref name="b"/> does, else 0.</returns>
    public static int ComparePriority(int? a, int? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => b.Value.CompareTo(a.Value),
    };

    /// <summary>
    /// The rank of a geometry at equal drawing priority: points 0, curves 1,
    /// surfaces 2, no geometry 3.
    /// </summary>
    /// <param name="geometry">The feature's geometry.</param>
    /// <returns>The rank, lowest first.</returns>
    public static int GeometryRank(S100GeometryType geometry) => geometry switch
    {
        S100GeometryType.Point => 0,
        S100GeometryType.Curve => 1,
        S100GeometryType.Surface => 2,
        _ => 3,
    };

    /// <summary>
    /// Compares two features by drawing priority, then geometry.
    /// </summary>
    /// <param name="priorityA">The first feature's drawing priority, or <c>null</c>.</param>
    /// <param name="geometryA">The first feature's geometry.</param>
    /// <param name="priorityB">The second feature's drawing priority, or <c>null</c>.</param>
    /// <param name="geometryB">The second feature's geometry.</param>
    /// <returns>Negative when the first comes first, positive when the second does, else 0.</returns>
    public static int Compare(int? priorityA, S100GeometryType geometryA, int? priorityB, S100GeometryType geometryB)
    {
        var c = ComparePriority(priorityA, priorityB);
        return c != 0 ? c : GeometryRank(geometryA).CompareTo(GeometryRank(geometryB));
    }
}
