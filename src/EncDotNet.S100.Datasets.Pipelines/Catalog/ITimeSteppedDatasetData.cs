namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// A catalog payload whose dataset has time steps of its own: a weather grid's
/// valid times, or the times a CAP file's warnings start and end. The
/// <c>list_time_steps</c> tool reads it, so a product that isn't S-104 or
/// S-111 lists its steps without a change to the tool.
/// </summary>
public interface ITimeSteppedDatasetData
{
    /// <summary>The dataset's time steps (UTC), in ascending order; empty when it has none.</summary>
    IReadOnlyList<DateTime> TimeSteps { get; }
}
