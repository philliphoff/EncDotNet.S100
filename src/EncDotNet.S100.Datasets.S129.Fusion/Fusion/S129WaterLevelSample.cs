namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// A single water-level sample at a control-point position and time,
/// taken from the resolved S-104 dataset.
/// </summary>
/// <param name="Height">
/// The water-level height at the sampled cell, in metres above the
/// vertical datum, per S-104.
/// </param>
/// <param name="Trend">
/// The water-level trend flag at the sampled cell (S-104 listed value).
/// </param>
/// <param name="TimeSelected">
/// The actual time slice selected from the dataset's
/// <c>AvailableTimes</c>; this may differ from the requested time when
/// the dataset has no exact match (nearest-time semantics).
/// </param>
/// <param name="Row">The row of the sampled cell in the selected time slice's grid.</param>
/// <param name="Column">The column of the sampled cell in the selected time slice's grid.</param>
public sealed record S129WaterLevelSample(
    float Height,
    float Trend,
    DateTime TimeSelected,
    int Row,
    int Column);
