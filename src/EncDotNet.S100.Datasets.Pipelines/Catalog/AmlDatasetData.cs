using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// The catalog payload of an AML cell: its AML facts and the dataset metadata
/// the catalog entry is built from. It's deliberately not the S-101 payload, so
/// the route checks and charted-depth sources, which read S-101 payloads, never
/// see AML.
/// </summary>
/// <param name="Info">The cell's AML facts.</param>
/// <param name="DatasetMetadata">The spec (<c>AML</c>) and the cell's coverage extent.</param>
public sealed record AmlDatasetData(AmlDatasetInfo Info, DatasetMetadata DatasetMetadata) : ProductDatasetData
{
    /// <inheritdoc />
    public override DatasetMetadata Metadata => DatasetMetadata;

    /// <summary>
    /// Whether SoundCharts draws the cell: whether the AML portrayal has a style
    /// for any of its classes. <see langword="false"/> while it is "recognised,
    /// not drawn".
    /// </summary>
    public bool Drawn { get; init; }
}
