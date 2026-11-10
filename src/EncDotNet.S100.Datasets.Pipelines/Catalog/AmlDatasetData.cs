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

    /// <summary>
    /// The cell's features with the catalogue that names them, so the MCP
    /// pick tools report AML features as Object Information does (#1372);
    /// <see langword="null"/> when the payload was built without them.
    /// </summary>
    public AmlCellFeatures? Cell { get; init; }

    /// <summary>
    /// A feature's highest drawing priority in the cell's latest portrayal,
    /// by feature id, or <c>null</c> when it isn't drawn; <c>null</c> when
    /// nothing in the cell is drawn. A payload read without a processor works
    /// it out by running the AML portrayal once, when first asked.
    /// </summary>
    public Func<string, int?>? DrawingPriority { get; init; }
}
