using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// "Load latest run" for a weather source (#1075, #1395): re-reads the
/// source, then downloads (when outdated) and loads the latest of the given
/// items. A storm NHC no longer lists is marked final on the way. The
/// Library panel's Load latest run, a storm advisory's notification action
/// and the <see cref="WeatherRunMonitor"/> all run it.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class LoadLatestWeatherRunWorkflow
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryDownloader _downloader;
    private readonly LibraryRows _rows;
    private readonly DownloadLibraryItemsWorkflow _download;
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly MarkFinalStormsWorkflow _markFinal;

    /// <summary>Creates the workflow.</summary>
    /// <param name="library">The Library the source is in.</param>
    /// <param name="downloader">Says whether a downloaded run is outdated.</param>
    /// <param name="rows">Says whether a run can only be loaded after a download, as the Library shows it.</param>
    /// <param name="download">Downloads and loads a newer run.</param>
    /// <param name="open">Loads a run already on disk.</param>
    /// <param name="markFinal">Marks final the storms NHC no longer lists.</param>
    public LoadLatestWeatherRunWorkflow(
        CollectionLibrary library,
        ILibraryDownloader downloader,
        LibraryRows rows,
        DownloadLibraryItemsWorkflow download,
        OpenLibraryItemsWorkflow open,
        MarkFinalStormsWorkflow markFinal)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(markFinal);
        _library = library;
        _downloader = downloader;
        _rows = rows;
        _download = download;
        _open = open;
        _markFinal = markFinal;
    }

    /// <summary>
    /// Re-reads the source <paramref name="sourceId"/>, then downloads (when
    /// outdated) and loads the latest of the items with <paramref name="keys"/>.
    /// Pinned runs are left alone.
    /// </summary>
    /// <param name="sourceId">The weather source.</param>
    /// <param name="keys">The keys of the items to bring up to date.</param>
    public async Task RunAsync(Guid sourceId, IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var storms = _markFinal.LoadedStorms(sourceId, keys);
        _library.Refresh(sourceId: sourceId);
        await _library.WhenIdle().ConfigureAwait(true);

        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source?.Index is not { } index)
            return;
        await _markFinal.RunAsync(source, index, storms).ConfigureAwait(true);
        var download = new List<LibraryItemViewModel>();
        var load = new List<LibraryItemViewModel>();
        foreach (var item in index.Items.Where(i => keys.Contains(i.Key) && !WeatherRuns.IsPinned(i)))
        {
            var latest = _rows.Create(item, source);
            (latest.CanLoadAfterDownload || _downloader.IsOutdated(item) ? download : load).Add(latest);
        }

        if (download.Count > 0)
            await _download.RunAsync([.. download.Select(row => (row.Item, row.Source))], load: true).ConfigureAwait(true);
        if (load.Count > 0)
            await _open.RunAsync([.. load.Select(row => row.State)], defer: false).ConfigureAwait(true);
    }
}
