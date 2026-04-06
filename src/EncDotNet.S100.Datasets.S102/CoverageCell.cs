namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// A single cell in a coverage layer, combining the bathymetric measurement
/// with a shading index derived from portrayal rules.
/// </summary>
/// <param name="Depth">Depth in metres.</param>
/// <param name="Uncertainty">Uncertainty of the depth measurement in metres.</param>
/// <param name="ShadingIndex">
/// Index into the <see cref="DepthShading"/> list used to build the layer,
/// or <c>-1</c> if no shading matched this depth.
/// </param>
public readonly record struct CoverageCell(float Depth, float Uncertainty, int ShadingIndex);
