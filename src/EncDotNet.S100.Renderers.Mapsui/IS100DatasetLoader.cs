using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Loads datasets into an <see cref="IS100MapSession"/> from a file path,
/// building the processor with the host-supplied
/// <see cref="S100MapsuiOptions.DatasetPipelineFactory"/>.
/// </summary>
public interface IS100DatasetLoader
{
    /// <summary>
    /// Loads a single standalone dataset file or ENC base cell, registers it
    /// with the session, and renders it. The product spec is detected from the
    /// file; an ENC base cell (<c>.000</c>) also picks up sibling sequential
    /// updates (<c>.001</c>, <c>.002</c>, …).
    /// </summary>
    /// <param name="path">
    /// Path to a single dataset file (e.g. an S-101 <c>.000</c> cell, an HDF5
    /// <c>.h5</c>, or a GML file). Exchange-set folders and archives are not yet
    /// supported.
    /// </param>
    /// <param name="id">
    /// Optional stable identity for the loaded dataset. When omitted, the file
    /// name is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity of the loaded dataset.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="S100MapsuiOptions.DatasetPipelineFactory"/> was configured,
    /// or a dataset with the resolved identity is already loaded.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The file's product spec is not recognized.
    /// </exception>
    Task<MapDatasetId> LoadAsync(
        string path,
        MapDatasetId? id = null,
        CancellationToken cancellationToken = default);
}
