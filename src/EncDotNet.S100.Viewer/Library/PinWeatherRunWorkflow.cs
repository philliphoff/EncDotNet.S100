using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Pins a weather run, so the next run doesn't replace its downloaded copy,
/// or deletes a pinned run (#1075, #1395). The source re-indexes after
/// either, so the Library lists the pinned copy (or stops listing it).
/// </summary>
internal sealed class PinWeatherRunWorkflow
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryDownloader _downloader;

    /// <summary>Creates the workflow.</summary>
    /// <param name="library">The Library the run's source is in.</param>
    /// <param name="downloader">Keeps and deletes pinned copies.</param>
    public PinWeatherRunWorkflow(CollectionLibrary library, ILibraryDownloader downloader)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloader);
        _library = library;
        _downloader = downloader;
    }

    /// <summary>Keeps <paramref name="item"/>'s downloaded copy beside the latest run.</summary>
    /// <param name="item">The run, as indexed.</param>
    /// <param name="sourceId">Its source.</param>
    /// <returns>True when a copy was pinned; false when there is none.</returns>
    public bool Pin(CollectionItem item, Guid sourceId)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_downloader.Pin(item) is null)
            return false;
        _library.Refresh(sourceId: sourceId);
        return true;
    }

    /// <summary>Deletes the pinned run <paramref name="pinned"/>.</summary>
    /// <param name="pinned">The pinned run, as indexed.</param>
    /// <param name="sourceId">Its source.</param>
    /// <returns>True when it was deleted.</returns>
    public bool Unpin(CollectionItem pinned, Guid sourceId)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        if (!_downloader.Unpin(pinned))
            return false;
        _library.Refresh(sourceId: sourceId);
        return true;
    }
}
