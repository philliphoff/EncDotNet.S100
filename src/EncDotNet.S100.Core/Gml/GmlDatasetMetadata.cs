using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Core.Gml;

/// <summary>
/// Shared helper that builds a lightweight <see cref="DatasetMetadata"/> for
/// the GML-encoded S-100 products (S-122/124/125/127/128/129/131/201/411/421)
/// from their common shape — a product name, an optional declared edition, and
/// a list of <see cref="IS100Feature"/> instances (issue #460).
/// </summary>
/// <remarks>
/// <para>
/// GML is the weakest case for phased loading: S-100 Part 10b datasets carry
/// no reliable header-level minimum bounding rectangle, so the extent is
/// derived by folding together every feature's geometry — which means the GML
/// document must already be parsed. The phased win over a full load is
/// therefore not avoiding the parse but skipping the portrayal pipeline
/// (XSLT / Lua transforms, catalogue resolution, drawing-instruction
/// execution).
/// </para>
/// <para>
/// The extent is the raw geometric envelope with no portrayal padding, in
/// WGS-84 (EPSG:4326) — S-100 Part 10b geometry is always geographic — so
/// <see cref="DatasetMetadata.HorizontalCrsEpsg"/> is left <c>null</c>. When
/// the dataset carries no coordinate-bearing geometry (e.g. a catalogue of
/// container-only <c>Authority</c> features) the extent is <c>null</c> and the
/// host should fall back to a full load.
/// </para>
/// </remarks>
public static class GmlDatasetMetadata
{
    /// <summary>
    /// Builds the metadata for a GML dataset.
    /// </summary>
    /// <param name="specName">
    /// The product-specification short name (e.g. <c>"S-124"</c>).
    /// </param>
    /// <param name="declaredEdition">
    /// The dataset's declared product edition (e.g. <c>"2.0.0"</c>), or
    /// <c>null</c>; parsed into the <see cref="SpecRef.Edition"/> when present
    /// and well-formed.
    /// </param>
    /// <param name="features">The parsed feature instances.</param>
    /// <returns>The lightweight, product-agnostic dataset metadata.</returns>
    public static DatasetMetadata Create(
        string specName,
        string? declaredEdition,
        IEnumerable<IS100Feature> features)
    {
        ArgumentException.ThrowIfNullOrEmpty(specName);
        ArgumentNullException.ThrowIfNull(features);

        SpecVersion edition = !string.IsNullOrWhiteSpace(declaredEdition)
            && SpecVersion.TryParse(declaredEdition, out var parsed)
            ? parsed
            : default;

        return new DatasetMetadata
        {
            Spec = new SpecRef(specName, edition),
            Extent = ComputeExtent(features),
            HorizontalCrsEpsg = null,
            DisplayScale = null,
            TimeCoverage = null,
        };
    }

    /// <summary>
    /// Computes the raw (unpadded) WGS-84 geometric envelope of the features,
    /// or <c>null</c> when none carry coordinates.
    /// </summary>
    private static BoundingBox? ComputeExtent(IEnumerable<IS100Feature> features)
    {
        double minLat = double.MaxValue, minLon = double.MaxValue;
        double maxLat = double.MinValue, maxLon = double.MinValue;
        bool any = false;

        void Expand(GeoPosition p)
        {
            any = true;
            if (p.Latitude < minLat) minLat = p.Latitude;
            if (p.Latitude > maxLat) maxLat = p.Latitude;
            if (p.Longitude < minLon) minLon = p.Longitude;
            if (p.Longitude > maxLon) maxLon = p.Longitude;
        }

        foreach (var feature in features)
        {
            foreach (var p in feature.Points) Expand(p);
            foreach (var curve in feature.Curves)
                foreach (var p in curve) Expand(p);
            foreach (var p in feature.ExteriorRing) Expand(p);
            foreach (var ring in feature.InteriorRings)
                foreach (var p in ring) Expand(p);
        }

        return any ? new BoundingBox(minLat, minLon, maxLat, maxLon) : null;
    }
}
