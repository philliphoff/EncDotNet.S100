using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// The catalog payload of a WPC coded surface bulletin: the bulletin and the
/// dataset metadata the catalog entry is built from. Its features, for
/// identify and pick, are those of the analysis the processor draws
/// (<see cref="SurfaceAnalysisDatasetProcessor.DrawnAnalysis"/>).
/// </summary>
/// <param name="Bulletin">The bulletin.</param>
/// <param name="DatasetMetadata">The spec (<c>CODSUS</c>), WPC's domain and the valid times.</param>
public sealed record SurfaceAnalysisDatasetData(CodedSurfaceBulletin Bulletin, DatasetMetadata DatasetMetadata) : ProductDatasetData
{
    /// <inheritdoc />
    public override DatasetMetadata Metadata => DatasetMetadata;
}
