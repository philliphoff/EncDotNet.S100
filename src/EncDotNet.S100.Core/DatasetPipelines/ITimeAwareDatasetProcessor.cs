namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Capability implemented by dataset processors whose portrayal varies over a
/// discrete set of forecast / observation time steps (S-104 water levels,
/// S-111 surface currents, and S-411 sea ice). Lets callers (e.g. the CLI)
/// map a user-supplied time-step index to a concrete <see cref="DateTime"/>
/// without reflecting over concrete processor types.
/// </summary>
/// <remarks>
/// A static product may implement it with an empty <see cref="AvailableTimes"/>
/// just to report <see cref="IssueTime"/> (S-102); callers treat a dataset with
/// no time steps as untimed.
/// </remarks>
public interface ITimeAwareDatasetProcessor
{
    /// <summary>
    /// The ordered, distinct set of time steps available in the dataset.
    /// Empty when the dataset carries no temporal dimension.
    /// </summary>
    IReadOnlyList<DateTime> AvailableTimes { get; }

    /// <summary>
    /// When the producer issued the dataset (UTC), from its <c>issueDate</c> and
    /// <c>issueTime</c> (see <see cref="S100IssueTime"/>); <see langword="null"/>
    /// when the dataset does not say. For a forecast this is usually some time
    /// after the model run it carries.
    /// </summary>
    DateTime? IssueTime => null;
}
