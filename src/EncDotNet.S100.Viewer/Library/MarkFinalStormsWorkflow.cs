using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Datasets.TropicalCyclones;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Marks final the loaded NHC storms that NHC no longer lists (#1395;
/// vector-weather.md §7): NHC has issued its last advisory, so a marker is
/// written beside the storm's zips and the storm is reopened to be drawn as
/// its final advisory. It stays loaded.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class MarkFinalStormsWorkflow
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryLoader _loader;
    private readonly ILibraryDownloader _downloader;
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly TimeProvider _time;

    /// <summary>Creates the workflow.</summary>
    /// <param name="library">The Library the storm sources are in.</param>
    /// <param name="loader">Says which storms are loaded, and closes the ones it reopens.</param>
    /// <param name="downloader">Finds each storm's downloaded copy and the advisory it holds.</param>
    /// <param name="open">Reopens the storms it marks.</param>
    /// <param name="time">The clock the marker records.</param>
    public MarkFinalStormsWorkflow(
        CollectionLibrary library,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        OpenLibraryItemsWorkflow open,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(time);
        _library = library;
        _loader = loader;
        _downloader = downloader;
        _open = open;
        _time = time;
    }

    /// <summary>
    /// The loaded storms among <paramref name="keys"/> of an NHC source, as
    /// opened (their downloaded copies), with the advisory each was opened
    /// at. Empty for any other source.
    /// </summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="keys">The keys of the items to look at.</param>
    public IReadOnlyDictionary<string, (CollectionItem Local, DateTimeOffset? Run)> LoadedStorms(Guid sourceId, IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<string, (CollectionItem, DateTimeOffset?)>(StringComparer.Ordinal);
        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source is not { Definition: WeatherFeedSource { Service.Kind: WeatherServiceKind.NhcStorms }, Index: { } index })
            return result;
        foreach (var item in index.Items.Where(i => keys.Contains(i.Key) && !WeatherRuns.IsPinned(i)))
        {
            var local = _downloader.Localize(item);
            if (_loader.StateOf(local) == LibraryLoadState.Loaded)
                result[item.Key] = (local, _downloader.LocalPublishedAtOf(item));
        }

        return result;
    }

    /// <summary>
    /// Marks final each of <paramref name="storms"/> (loaded storms, by key)
    /// that a successful index of <paramref name="source"/> no longer lists,
    /// and reopens them.
    /// </summary>
    /// <param name="source">The NHC source, just re-read.</param>
    /// <param name="index">Its new index.</param>
    /// <param name="storms">The storms that were loaded before the re-read (<see cref="LoadedStorms"/>).</param>
    public async Task RunAsync(
        LibrarySource source, SourceIndex index, IReadOnlyDictionary<string, (CollectionItem Local, DateTimeOffset? Run)> storms)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(storms);
        if (storms.Count == 0 || index.Diagnostics.Any(d => d.Severity == IndexDiagnosticSeverity.Error))
            return;
        var reopen = new List<CollectionItem>();
        foreach (var (key, (local, _)) in storms)
        {
            if (index.Items.Any(i => i.Key == key) || local.Location is not LocalItemLocation { IsZip: false } location)
                continue;
            try
            {
                var folder = Path.GetDirectoryName(Path.Combine(location.RootPath, location.RelativePath)) ?? location.RootPath;
                var marker = Path.Combine(folder, NhcAdvisoryFiles.FinalMarkerName(key));
                if (File.Exists(marker))
                    continue;
                await File.WriteAllTextAsync(marker, string.Create(CultureInfo.InvariantCulture,
                    $"NHC stopped listing {key.ToUpperInvariant()} as an active storm by {_time.GetUtcNow():yyyy-MM-dd'T'HH:mm'Z'}.\n")).ConfigureAwait(true);
                reopen.Add(local);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The storm stays as it was drawn.
            }
        }

        if (reopen.Count == 0)
            return;
        _loader.Close(reopen);
        await _open.RunAsync([.. reopen.Select(item => (item, (LibrarySource?)source))], defer: false).ConfigureAwait(true);
    }
}
