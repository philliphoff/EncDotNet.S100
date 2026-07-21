using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Chooses a picked location's base (tide-independent) depth from the
/// available S-100 sources, applying the depth-assimilation priority chain:
/// S-102 bathymetry → S-101 <c>DredgedArea</c> → S-101 <c>DepthArea</c> →
/// nearest S-101 <c>Sounding</c>.
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
    /// Attribute codes carrying the S-101 minimum depth of an area
    /// (<c>depthRangeMinimumValue</c>, DRVAL1) — the shoalest depth, taken as
    /// the conservative base depth for depth/dredged areas.
    /// </summary>
    private static readonly string[] MinimumDepthCodes =
    [
        "depthRangeMinimumValue",
        "DRVAL1",
    ];

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
    /// <returns>The chosen <see cref="BaseDepthResult"/>, or <c>null</c>.</returns>
    public BaseDepthResult? Resolve(
        S102DepthSample? bathymetry,
        IReadOnlyList<PickHit> hits,
        S101SoundingSample? nearestSounding)
    {
        ArgumentNullException.ThrowIfNull(hits);

        if (bathymetry is { } bathy)
        {
            return new BaseDepthResult(
                bathy.DepthMeters,
                BaseDepthSource.Bathymetry,
                bathy.UncertaintyMeters,
                bathy.VerticalDatumCode,
                SoundingDistanceMeters: null);
        }

        if (TryReadAreaMinimumDepth(hits, "DredgedArea") is { } dredged)
        {
            return new BaseDepthResult(
                dredged,
                BaseDepthSource.DredgedArea,
                UncertaintyMeters: null,
                VerticalDatumCode: null,
                SoundingDistanceMeters: null);
        }

        if (TryReadAreaMinimumDepth(hits, "DepthArea") is { } depthArea)
        {
            return new BaseDepthResult(
                depthArea,
                BaseDepthSource.DepthArea,
                UncertaintyMeters: null,
                VerticalDatumCode: null,
                SoundingDistanceMeters: null);
        }

        if (nearestSounding is { } sounding)
        {
            return new BaseDepthResult(
                sounding.DepthMeters,
                BaseDepthSource.Sounding,
                UncertaintyMeters: null,
                VerticalDatumCode: null,
                sounding.DistanceMeters);
        }

        return null;
    }

    /// <summary>
    /// Finds the shoalest declared minimum depth across every pick hit of the
    /// given S-101 feature type, or <c>null</c> when none is present.
    /// </summary>
    private static double? TryReadAreaMinimumDepth(IReadOnlyList<PickHit> hits, string featureType)
    {
        double? shoalest = null;
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
            }
        }

        return shoalest;
    }

    /// <summary>
    /// Recursively searches an attribute tree for a minimum-depth attribute
    /// and returns its canonical metres value.
    /// </summary>
    private static double? FindMinimumDepth(IReadOnlyList<PickAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            foreach (var code in MinimumDepthCodes)
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
