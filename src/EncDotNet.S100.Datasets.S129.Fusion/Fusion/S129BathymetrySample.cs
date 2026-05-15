namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// A single bathymetric sample at a control-point position, taken from
/// the resolved S-102 dataset.
/// </summary>
/// <param name="Depth">
/// The depth at the sampled cell, in metres, positive-down per S-102.
/// </param>
/// <param name="Uncertainty">
/// The uncertainty (1-σ) at the sampled cell, in metres per S-102.
/// </param>
/// <param name="Row">The row of the sampled cell in the underlying grid.</param>
/// <param name="Column">The column of the sampled cell in the underlying grid.</param>
public sealed record S129BathymetrySample(
    float Depth,
    float Uncertainty,
    int Row,
    int Column);
