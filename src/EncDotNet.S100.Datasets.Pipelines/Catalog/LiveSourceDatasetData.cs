using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// The catalog payload of a live source (data source design note D9, slice
/// 11b): the <see cref="DynamicSourceProcessor"/> itself, whose features change
/// as the source delivers them. <see cref="Spec.FeatureAccessor"/> reads its
/// current features on each query, so every query tool sees the latest.
/// </summary>
/// <param name="Processor">The live source's processor.</param>
/// <remarks>
/// The catalog entry has no extent of its own (the source's area moves with
/// the view), so it's treated as covering the world, and bounds prefilters
/// never leave it out.
/// </remarks>
public sealed record LiveSourceDatasetData(DynamicSourceProcessor Processor) : ProductDatasetData
{
    /// <inheritdoc />
    public override DatasetMetadata Metadata => new() { Spec = Processor.Spec };
}
