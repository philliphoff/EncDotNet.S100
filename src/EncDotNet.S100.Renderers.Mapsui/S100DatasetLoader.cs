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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_pipelineFactory is null)
        {
            throw new InvalidOperationException(
                $"Set {nameof(S100MapsuiOptions)}.{nameof(S100MapsuiOptions.DatasetPipelineFactory)} "
                    + "to load datasets from a path, or add a pre-built processor with AddDatasetAsync.");
        }

        var factory = _pipelineFactory;

        // Trim any trailing separator so a path ending in one does not yield an
        // empty file name (a confusing MapDatasetId error) and so the same,
        // normalized path drives both name derivation and the actual load.
        var normalizedPath = path.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(
                $"Path '{path}' does not name a dataset file.", nameof(path));
        }

        var datasetId = id ?? new MapDatasetId(fileName);

        // Fail fast on a duplicate id before the (slow) parse. AddDatasetAsync
        // remains the authoritative guard against a concurrent add.
        if (_session.GetDataset(datasetId) is not null)
        {
            throw new InvalidOperationException(
                $"A dataset with id '{datasetId.Value}' is already loaded.");
        }

        // Parsing a base cell is slow; build the processor off the calling
        // thread, mirroring the Viewer's load path.
        var processor = await Task.Run(
            () => factory.CreateProcessorWithFilesystemUpdates(normalizedPath),
            cancellationToken).ConfigureAwait(true);

        var dataset = new MapDataset(
            datasetId,
            fileName,
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
