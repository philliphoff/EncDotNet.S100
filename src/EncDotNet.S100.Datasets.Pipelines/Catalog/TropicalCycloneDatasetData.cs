using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.TropicalCyclones;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// The catalog payload of a tropical cyclone advisory
/// (<see cref="TropicalCycloneDatasetProcessor"/>): the advisory and its
/// metadata. Not for navigation.
/// </summary>
/// <param name="Advisory">The advisory.</param>
/// <param name="DatasetMetadata">Its spec, extent and time coverage.</param>
public sealed record TropicalCycloneDatasetData(TropicalCycloneAdvisory Advisory, DatasetMetadata DatasetMetadata) : ProductDatasetData
{
    /// <inheritdoc />
    public override DatasetMetadata Metadata => DatasetMetadata;
}
