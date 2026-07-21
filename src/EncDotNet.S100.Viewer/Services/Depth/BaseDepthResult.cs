namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// The base (static, tide-independent) depth chosen for a picked location,
/// together with the source it came from and any source-specific detail.
/// Produced by <see cref="BaseDepthResolver"/> and consumed by the depth
/// assimilation service to compute tide-adjusted depth over time.
/// </summary>
/// <param name="DepthMeters">
/// The chosen base depth in metres (positive down), relative to the source's
/// own vertical datum.
/// </param>
/// <param name="Source">Which S-100 source supplied the value.</param>
/// <param name="UncertaintyMeters">
/// Vertical uncertainty in metres — populated only when
/// <see cref="Source"/> is <see cref="BaseDepthSource.Bathymetry"/>;
/// <c>null</c> otherwise.
/// </param>
/// <param name="VerticalDatumCode">
/// The source vertical datum as an S-100 register code, when known
/// (S-102 bathymetry only); <c>null</c> for S-101 vector sources, which
/// are on the chart's sounding datum.
/// </param>
/// <param name="SoundingDistanceMeters">
/// Planar distance in metres from the pick to the chosen sounding —
/// populated only when <see cref="Source"/> is
/// <see cref="BaseDepthSource.Sounding"/>; <c>null</c> otherwise.
/// </param>
internal sealed record BaseDepthResult(
    double DepthMeters,
    BaseDepthSource Source,
    double? UncertaintyMeters,
    int? VerticalDatumCode,
    double? SoundingDistanceMeters);
