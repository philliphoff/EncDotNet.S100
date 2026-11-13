using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>Where the tide tools get their station catalogue.</summary>
public interface ITideCatalogueSource
{
    /// <summary>
    /// Says where a catalogue would come from and how to get one, for the
    /// error a tool gives when there is none.
    /// </summary>
    string MissingHint { get; }

    /// <summary>Gets the catalogue.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The catalogue, or <see langword="null"/> when there is none yet.</returns>
    Task<TideStationCatalogue?> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A catalogue kept in a <see cref="TideStationCatalogueFile"/> file, such as
/// the offline cache <c>s100 tides update</c> writes. It is loaded on first
/// use and again whenever the file changes.
/// </summary>
public sealed class TideCatalogueFileSource : ITideCatalogueSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TideStationCatalogue? _catalogue;
    private DateTime _loadedWriteTimeUtc;

    /// <param name="path">The catalogue file (<c>.json</c>, or <c>.json.gz</c> for gzip).</param>
    /// <param name="missingHint">What to say when the file isn't there.</param>
    public TideCatalogueFileSource(string path, string missingHint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(missingHint);
        Path = System.IO.Path.GetFullPath(path);
        MissingHint = missingHint;
    }

    /// <summary>The catalogue file's full path.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string MissingHint { get; }

    /// <inheritdoc />
    /// <exception cref="FormatException">The file isn't a valid catalogue.</exception>
    public async Task<TideStationCatalogue?> GetAsync(CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path);
        if (!file.Exists)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalogue is null || file.LastWriteTimeUtc != _loadedWriteTimeUtc)
            {
                _catalogue = await TideStationCatalogueFile.LoadAsync(Path, cancellationToken).ConfigureAwait(false);
                _loadedWriteTimeUtc = file.LastWriteTimeUtc;
            }

            return _catalogue;
        }
        finally
        {
            _gate.Release();
        }
    }
}
