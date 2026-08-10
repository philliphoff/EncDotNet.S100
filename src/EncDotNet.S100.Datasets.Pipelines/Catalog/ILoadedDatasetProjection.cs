namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// Implemented by an <see cref="IDatasetProcessor"/> that can yield the
/// query-tool payload for its <em>already-parsed</em> dataset, so a host can
/// project a <see cref="LoadedDataset"/> from a resident processor rather than
/// re-parsing the dataset bytes a second time.
/// </summary>
/// <remarks>
/// <para>
/// Every built-in processor parses its per-spec model once at construction and
/// holds it for its lifetime. That same model is what the catalog's
/// <see cref="LoadedDatasetData"/> variants wrap, so this seam simply exposes it
/// — <see cref="CreateLoadedData"/> allocates the matching variant over the
/// resident model without touching the source bytes again. The geographic
/// bounds, temporal coverage, and declared specification are derived by
/// <see cref="LoadedDatasetProjector.Project(DatasetId, IDatasetProcessor, System.Func{string, string?}?, EncDotNet.S100.Pipelines.ICrsTransformFactory?)"/>
/// from the returned payload, using the identical per-spec math the
/// stream-based projection uses — so a processor-projected
/// <see cref="LoadedDataset"/> matches a stream-projected one field for field.
/// </para>
/// <para>
/// Only the <see cref="LoadedDatasetData"/> payload is produced here; the
/// external-text resolver an S-101 dataset may carry (from an exchange set) is
/// woven in by the projector, which is where the resolver is known.
/// </para>
/// </remarks>
public interface ILoadedDatasetProjection
{
    /// <summary>
    /// Allocates the <see cref="LoadedDatasetData"/> variant wrapping this
    /// processor's already-parsed model. Never re-reads the source bytes.
    /// </summary>
    LoadedDatasetData CreateLoadedData();
}
