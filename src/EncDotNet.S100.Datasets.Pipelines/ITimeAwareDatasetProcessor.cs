using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Capability implemented by dataset processors whose portrayal varies over a
/// discrete set of forecast / observation time steps (S-104 water levels,
/// S-111 surface currents, and S-411 sea ice). Lets callers (e.g. the CLI)
/// map a user-supplied time-step index to a concrete <see cref="DateTime"/>
/// without reflecting over concrete processor types.
/// </summary>
public interface ITimeAwareDatasetProcessor
{
    /// <summary>
    /// The ordered, distinct set of time steps available in the dataset.
    /// Empty when the dataset carries no temporal dimension.
    /// </summary>
    IReadOnlyList<DateTime> AvailableTimes { get; }
}
