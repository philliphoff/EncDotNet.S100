using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Default <see cref="IS100DatasetLoader"/> implementation. Builds a processor
/// from a path with the host-supplied <see cref="DatasetPipelineFactory"/> and
/// hands it to the owning session's
/// <see cref="IS100MapSession.AddDatasetAsync"/>.
/// </summary>
internal sealed class S100DatasetLoader : IS100DatasetLoader
{
    private readonly IS100MapSession _session;
    private readonly DatasetPipelineFactory? _pipelineFactory;

    internal S100DatasetLoader(
        IS100MapSession session,
        DatasetPipelineFactory? pipelineFactory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _pipelineFactory = pipelineFactory;
    }

    /// <inheritdoc />
    public async Task<MapDatasetId> LoadAsync(
        string path,
        MapDatasetId? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (_pipelineFactory is null)
        {
            throw new InvalidOperationException(
                $"Set {nameof(S100MapsuiOptions)}.{nameof(S100MapsuiOptions.DatasetPipelineFactory)} "
                    + "to load datasets from a path, or add a pre-built processor with AddDatasetAsync.");
        }

        var factory = _pipelineFactory;
        var datasetId = id ?? new MapDatasetId(Path.GetFileName(path));

        // Parsing a base cell is slow; build the processor off the calling
        // thread, mirroring the Viewer's load path.
        var processor = await Task.Run(
            () => factory.CreateProcessorWithFilesystemUpdates(path),
            cancellationToken).ConfigureAwait(true);

        var dataset = new MapDataset(
            datasetId,
            Path.GetFileName(path),
            processor.Metadata,
            versionAssessment: processor.VersionAssessment);

        // AddDatasetAsync takes ownership on success and disposes the processor
        // itself if it throws; only a false (duplicate identity) return leaves
        // ownership with us.
        var added = await _session.AddDatasetAsync(
            dataset, processor, cancellationToken: cancellationToken).ConfigureAwait(true);
        if (!added)
        {
            (processor as IDisposable)?.Dispose();
            throw new InvalidOperationException(
                $"A dataset with id '{datasetId.Value}' is already loaded.");
        }

        return datasetId;
    }
}
