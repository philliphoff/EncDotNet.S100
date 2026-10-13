using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Chooses a picked location's base (tide-independent) depth from the
/// available S-100 sources, applying the depth-assimilation priority chain:
/// S-102 bathymetry → S-101 <c>DredgedArea</c> → S-101 <c>DepthArea</c> →
/// nearest S-101 <c>Sounding</c>. The chain and the minimum-depth attribute
/// codes are <see cref="ChartedDepthResolver"/>'s, shared with the passage
/// check so the pick report and Check passage agree.
/// </summary>
/// <remarks>
/// The S-102 sample and the nearest sounding are produced by the caller (via
/// the CRS-aware coverage sampler and <see cref="S101SoundingSampler"/>) and
/// injected as candidates, keeping the fallthrough logic free of live
/// coverage/CRS infrastructure and independently unit-testable. The vector
/// area fallbacks are read directly from the already-resolved pick hits so no
/// additional dataset query is required.
/// </remarks>
internal sealed class BaseDepthResolver
{
    /// <summary>
    /// Resolves the base depth for a pick, returning <c>null</c> when no
    /// source can supply a depth.
    /// </summary>
    /// <param name="bathymetry">
    /// The S-102 bathymetric sample at the pick, or <c>null</c> when no S-102
    /// coverage overlaps the point.
    /// </param>
    /// <param name="hits">
    /// The already-resolved pick hits, searched for S-101 <c>DredgedArea</c>
    /// and <c>DepthArea</c> minimum depths.
    /// </param>
    /// <param name="nearestSounding">
    /// The nearest S-101 charted sounding, or <c>null</c> when none is
    /// available.
    /// </param>
    /// <param name="bathymetryDatasetId">
    /// Display name of the S-102 dataset that supplied <paramref name="bathymetry"/>,
    /// or <c>null</c>.
    /// </param>
    /// <param name="soundingDatasetId">
    /// Display name of the S-101 dataset that supplied
    /// <paramref name="nearestSounding"/>, or <c>null</c>.
    /// </param>
    /// <returns>The chosen <see cref="BaseDepthResult"/>, or <c>null</c>.</returns>
    public BaseDepthResult? Resolve(
        S102DepthSample? bathymetry,
        IReadOnlyList<PickHit> hits,
        S101SoundingSample? nearestSounding,
        string? bathymetryDatasetId = null,
        string? soundingDatasetId = null)
    {
        ArgumentNullException.ThrowIfNull(hits);

        var chosen = ChartedDepthResolver.Resolve(
            bathymetry is { } bathy
                ? new ChartedDepth(bathy.DepthMeters, ChartedDepthKind.Bathymetry, bathy.UncertaintyMeters, bathy.VerticalDatumCode, bathymetryDatasetId)
                : null,
            TryReadAreaMinimumDepth(hits, "DredgedArea") is { } dredged
                ? new ChartedDepth(dredged.Depth, ChartedDepthKind.DredgedArea, DatasetId: dredged.DatasetId)
                : null,
            TryReadAreaMinimumDepth(hits, "DepthArea") is { } area
                ? new ChartedDepth(area.Depth, ChartedDepthKind.DepthArea, DatasetId: area.DatasetId)
                : null,
            nearestSounding is { } sounding
                ? new ChartedDepth(sounding.DepthMeters, ChartedDepthKind.Sounding, DatasetId: soundingDatasetId, SoundingDistanceMeters: sounding.DistanceMeters)
                : null);

        return chosen is null
            ? null
            : new BaseDepthResult(
                chosen.DepthMeters,
                chosen.Kind switch
                {
                    ChartedDepthKind.Bathymetry => BaseDepthSource.Bathymetry,
                    ChartedDepthKind.DredgedArea => BaseDepthSource.DredgedArea,
                    ChartedDepthKind.DepthArea => BaseDepthSource.DepthArea,
                    _ => BaseDepthSource.Sounding,
                },
                chosen.UncertaintyMeters,
                chosen.VerticalDatum,
                chosen.SoundingDistanceMeters,
                chosen.DatasetId);
    }

    /// <summary>
    /// Finds the shoalest declared minimum depth across every pick hit of the
    /// given S-101 feature type together with the dataset it came from, or
    /// <c>null</c> when none is present.
    /// </summary>
    private static (double Depth, string? DatasetId)? TryReadAreaMinimumDepth(
        IReadOnlyList<PickHit> hits, string featureType)
    {
        double? shoalest = null;
        string? datasetId = null;
        foreach (var hit in hits)
        {
            if (!string.Equals(hit.FeatureType, featureType, StringComparison.Ordinal))
            {
                continue;
            }

            var depth = FindMinimumDepth(hit.Attributes);
            if (depth is { } value && (shoalest is null || value < shoalest))
            {
                shoalest = value;
                datasetId = hit.DatasetFileName;
            }
        }

        return shoalest is { } result ? (result, datasetId) : null;
    }

    /// <summary>
    /// Recursively searches an attribute tree for a minimum-depth attribute
    /// and returns its canonical metres value.
    /// </summary>
    private static double? FindMinimumDepth(IReadOnlyList<PickAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            foreach (var code in ChartedDepthResolver.MinimumDepthAttributeCodes)
            {
                if (string.Equals(attribute.Code, code, StringComparison.Ordinal)
                    && attribute.DepthMetresValue is { } metres)
                {
                    return metres;
                }
            }

            if (attribute.Children.Count > 0
                && FindMinimumDepth(attribute.Children) is { } childDepth)
            {
                return childDepth;
            }
        }

        return null;
    }
}
