using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// A single dataset currently loaded into an <see cref="IDatasetCatalog"/>.
/// </summary>
/// <param name="Id">Stable identifier for the dataset within the catalog session.</param>
/// <param name="Spec">The product specification (and edition) the dataset declares.</param>
/// <param name="Bounds">Geographic extent of the dataset.</param>
/// <param name="TimeRange">Time interval covered by the dataset, or <c>null</c> for static products.</param>
/// <param name="Data">Typed payload — see <see cref="LoadedDatasetData"/> variants.</param>
public sealed record LoadedDataset(
    DatasetId Id,
    SpecRef Spec,
    BoundingBox Bounds,
    TimeRange? TimeRange,
    LoadedDatasetData Data);
