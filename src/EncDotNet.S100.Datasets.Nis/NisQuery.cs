namespace EncDotNet.S100.Datasets.Nis;

/// <summary>Which features <see cref="INisSource.GetFeatures"/> returns.</summary>
public sealed class NisQuery
{
    /// <summary>Every feature that isn't deleted.</summary>
    public static NisQuery All { get; } = new();

    /// <summary>
    /// Only features from this cell (their <see cref="NisFeature.DatasetName"/>),
    /// compared ignoring case; <see langword="null"/> for every cell.
    /// </summary>
    public string? DatasetName { get; init; }

    /// <summary>
    /// Only features whose extent overlaps this box, and features with no
    /// geometry; <see langword="null"/> for no limit.
    /// </summary>
    public NisBounds? Bounds { get; init; }

    /// <summary>Whether to include features marked deleted.</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Whether a feature meets the query.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Matches(NisFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.IsDeleted && !IncludeDeleted) return false;
        if (DatasetName is not null && !string.Equals(feature.DatasetName, DatasetName, StringComparison.OrdinalIgnoreCase)) return false;
        if (Bounds is { } bounds && feature.Geometry is { } geometry && !bounds.Intersects(geometry)) return false;
        return true;
    }
}
