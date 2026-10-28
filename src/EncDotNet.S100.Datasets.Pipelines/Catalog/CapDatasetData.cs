using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Cap;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// The catalog payload of a CAP file: its warning areas, resolved and timed,
/// and the dataset metadata the catalog entry is built from.
/// </summary>
/// <param name="Warnings">The file's areas.</param>
/// <param name="DatasetMetadata">The spec (<c>CAP/1.2.0</c>), the areas' extent and the warnings' time coverage.</param>
public sealed record CapDatasetData(CapWarningSet Warnings, DatasetMetadata DatasetMetadata) : ProductDatasetData, ITimeSteppedDatasetData
{
    /// <inheritdoc />
    public override DatasetMetadata Metadata => DatasetMetadata;

    /// <inheritdoc />
    /// <remarks>
    /// When each warning starts and the tick after each ends
    /// (<see cref="CapWarningSet.Changes"/>), the steps the processor offers.
    /// </remarks>
    public IReadOnlyList<DateTime> TimeSteps => Warnings.Changes;
}
