namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// A single S-102 bathymetric sample at a picked location, already resolved
/// to metres (positive down) by the caller via the CRS-aware coverage
/// sampler. Fed into <see cref="BaseDepthResolver"/> as the highest-priority
/// base-depth candidate.
/// </summary>
/// <param name="DepthMeters">
/// The sampled bathymetric depth in metres (positive down).
/// </param>
/// <param name="UncertaintyMeters">
/// The co-located vertical uncertainty in metres, or <c>null</c> when the
/// dataset carries no uncertainty band.
/// </param>
/// <param name="VerticalDatumCode">
/// The S-102 dataset's declared vertical datum as an S-100 register code
/// (source identifier 996), or <c>null</c> when absent. Used downstream to
/// flag S-102/S-104 datum mismatches.
/// </param>
internal readonly record struct S102DepthSample(
    double DepthMeters,
    double? UncertaintyMeters,
    int? VerticalDatumCode);
