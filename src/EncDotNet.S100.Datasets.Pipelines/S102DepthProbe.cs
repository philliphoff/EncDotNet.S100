namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A single S-102 bathymetric sample at a geographic point, resolved to metres
/// (positive down) by <see cref="S102DatasetProcessor.SampleBaseDepth"/>. This
/// is the CRS-aware live-sampling result the viewer feeds into its depth
/// assimilation as the highest-priority base-depth candidate.
/// </summary>
/// <param name="DepthMetres">The sampled bathymetric depth in metres (positive down).</param>
/// <param name="UncertaintyMetres">
/// The co-located vertical uncertainty in metres, or <c>null</c> when the
/// dataset carries no uncertainty band (or the band cell is NoData).
/// </param>
/// <param name="VerticalDatumCode">
/// The S-102 dataset's declared vertical datum as an S-100 register code
/// (source identifier 996, S-100 Part 4a), or <c>null</c> when absent.
/// </param>
public readonly record struct S102DepthProbe(
    double DepthMetres,
    double? UncertaintyMetres,
    int? VerticalDatumCode);
