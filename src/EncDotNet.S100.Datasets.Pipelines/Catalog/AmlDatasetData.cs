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
    /// The cell's features with the catalogue that names them, so the MCP
    /// pick tools report AML features as Object Information does (#1372);
    /// <see langword="null"/> when the payload was built without them.
    /// </summary>
    public AmlCellFeatures? Cell { get; init; }

    /// <summary>
    /// A feature's highest drawing priority in the cell's latest portrayal,
    /// by feature id, or <c>null</c> when it isn't drawn; <c>null</c> when
    /// the payload has no portrayal (every feature is then "not drawn").
    /// </summary>
    public Func<string, int?>? DrawingPriority { get; init; }
}
