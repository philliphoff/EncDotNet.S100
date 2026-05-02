using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// Filter helpers over an <see cref="S128Dataset"/>'s
/// <see cref="S128Dataset.Entries"/> collection.
/// </summary>
/// <remarks>
/// Catalogue browsers typically need three views: by area-of-interest, by
/// product type (e.g. only S-101 ENCs), and by currency status. These
/// helpers give callers a single discoverable entry point so each consumer
/// does not re-implement the filtering logic.
/// </remarks>
public static class S128CatalogueQuery
{
    /// <summary>
    /// Returns entries whose coverage exterior ring intersects the given
    /// (lat, lon) bounding box. Geometry-less entries are excluded.
    /// </summary>
    /// <param name="entries">Source entries (typically <see cref="S128Dataset.Entries"/>).</param>
    /// <param name="minLatitude">Minimum latitude of the AOI.</param>
    /// <param name="minLongitude">Minimum longitude of the AOI.</param>
    /// <param name="maxLatitude">Maximum latitude of the AOI.</param>
    /// <param name="maxLongitude">Maximum longitude of the AOI.</param>
    public static IEnumerable<S128ProductEntry> FilterByExtent(
        IEnumerable<S128ProductEntry> entries,
        double minLatitude,
        double minLongitude,
        double maxLatitude,
        double maxLongitude)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            var ring = entry.CoverageRing;
            if (ring.IsDefaultOrEmpty) continue;

            double rMinLat = double.MaxValue, rMaxLat = double.MinValue;
            double rMinLon = double.MaxValue, rMaxLon = double.MinValue;
            foreach (var (lat, lon) in ring)
            {
                if (lat < rMinLat) rMinLat = lat;
                if (lat > rMaxLat) rMaxLat = lat;
                if (lon < rMinLon) rMinLon = lon;
                if (lon > rMaxLon) rMaxLon = lon;
            }

            // Reject only when the bounding boxes are clearly disjoint.
            if (rMaxLat < minLatitude || rMinLat > maxLatitude) continue;
            if (rMaxLon < minLongitude || rMinLon > maxLongitude) continue;
            yield return entry;
        }
    }

    /// <summary>
    /// Returns entries whose feature class matches one of <paramref name="featureTypes"/>
    /// (e.g. <c>"ElectronicProduct"</c>) — case-insensitive.
    /// </summary>
    public static IEnumerable<S128ProductEntry> FilterByProductType(
        IEnumerable<S128ProductEntry> entries,
        params string[] featureTypes)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(featureTypes);

        var allowed = new HashSet<string>(featureTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (allowed.Contains(entry.FeatureType))
                yield return entry;
        }
    }

    /// <summary>
    /// Returns entries whose product specification name matches one of
    /// <paramref name="specifications"/> (substring match, case-insensitive).
    /// </summary>
    /// <example>
    /// <c>FilterBySpecification(entries, "S-101", "S-104")</c> returns S-101 ENCs and S-104 services.
    /// </example>
    public static IEnumerable<S128ProductEntry> FilterBySpecification(
        IEnumerable<S128ProductEntry> entries,
        params string[] specifications)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(specifications);

        foreach (var entry in entries)
        {
            var name = entry.ProductSpecificationName;
            if (name is null) continue;
            foreach (var s in specifications)
            {
                if (!string.IsNullOrEmpty(s) && name.Contains(s, StringComparison.OrdinalIgnoreCase))
                {
                    yield return entry;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Returns entries whose heuristic <see cref="S128ProductEntry.Status"/>
    /// matches <paramref name="status"/>.
    /// </summary>
    public static IEnumerable<S128ProductEntry> FilterByStatus(
        IEnumerable<S128ProductEntry> entries,
        S128ProductStatus status)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            if (entry.Status == status)
                yield return entry;
        }
    }
}
