using System.Diagnostics;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's MCP <c>library_action</c> (#715, #1394): picks the rows an
/// action applies to as the Library panel would offer it, then opens or
/// downloads them through the workflows the panel uses.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class ViewerLibraryActions
{
    private readonly LibraryRows _rows;
    private readonly ViewerLibraryController _reader;
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly DownloadLibraryItemsWorkflow _download;
    private readonly ILibraryDownloader _downloader;
    private readonly LibraryActivityTracker _activity;

    /// <summary>Creates the actions.</summary>
    /// <param name="rows">The rows an action's item ids name.</param>
    /// <param name="reader">Finds the rows an action's filters match.</param>
    /// <param name="open">Opens rows.</param>
    /// <param name="download">Downloads rows.</param>
    /// <param name="downloader">Says which downloads are running, and cancels them.</param>
    /// <param name="activity">Tracks the opens and downloads, for <c>await_library_idle</c> (#790).</param>
    public ViewerLibraryActions(
        LibraryRows rows,
        ViewerLibraryController reader,
        OpenLibraryItemsWorkflow open,
        DownloadLibraryItemsWorkflow download,
        ILibraryDownloader downloader,
        LibraryActivityTracker activity)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(activity);
        _rows = rows;
        _reader = reader;
        _open = open;
        _download = download;
        _downloader = downloader;
        _activity = activity;
    }

    /// <summary>The Library's activity now: indexing, downloads, and the opens and downloads still running.</summary>
    /// <param name="library">The Library.</param>
    public LibraryActivitySnapshot CaptureActivity(CollectionLibrary library) =>
        LibraryActivitySnapshot.Capture(library, _downloader.Progress, _activity);

    /// <summary>Cancels every running download.</summary>
    public void CancelAll() => _downloader.CancelAll();

    /// <summary>Runs <paramref name="action"/> on the rows <paramref name="request"/> names.</summary>
    /// <param name="action">The action, normalised (one of <see cref="LibraryActionTool.Actions"/>).</param>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the action.</param>
    public async Task<LibraryEditOutcome<LibraryActionResult>> RunAsync(string action, LibraryActionRequest request, CancellationToken ct)
    {
        List<LibraryItemViewModel> rows;
        if (request.ItemIds is { Count: > 0 } ids)
        {
            rows = [];
            var unknown = new List<string>();
            foreach (var id in ids)
            {
                if (_rows.Find(id) is { } row)
                    rows.Add(row);
                else
                    unknown.Add(id);
            }
            if (unknown.Count > 0)
                return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("itemIds", $"unknown item id(s): {string.Join(", ", unknown)}"));
        }
        else if (_reader.FindRows(request.Filter!) is { } found)
        {
            rows = found;
        }
        else
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("sourceId", "no such collection or source; call list_library_sources"));
        }

        Func<LibraryItemViewModel, bool> eligible = action switch
        {
            "load" or "load_as_you_pan" => row => row.CanLoad,
            "download" or "download_only" => row => row.CanDownload,
            "update" => row => row.CanDownload && row.Availability == LibraryAvailability.Outdated,
            _ => row => _downloader.StatusOf(row.Item) is { State: LibraryDownloadItemState.Queued or LibraryDownloadItemState.Running },
        };
        var chosen = rows.Where(eligible).ToList();
        var skipped = rows.Where(row => !eligible(row))
            .GroupBy(row => LibraryAvailabilityNames.Of(row.Availability), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var downloads = action is "download" or "download_only" or "update";
        var sizes = chosen.Select(row => (row.Item.Location as RemoteItemLocation)?.SizeBytes).ToArray();
        var bytes = downloads ? sizes.Sum(size => size ?? 0) : 0;
        var unknownSizes = downloads ? sizes.Count(size => size is null) : 0;
        var names = chosen.Take(LibraryActionTool.ListedNames).Select(row => row.Item.Name).ToArray();

        LibraryActionResult Result(int? opened, bool started) =>
            new(action, request.DryRun, chosen.Count, skipped, bytes, unknownSizes, names, opened, started);

        if (request.DryRun)
            return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, false));
        if (chosen.Count == 0)
            return LibraryEditOutcome<LibraryActionResult>.Ok(Result(action.StartsWith("load", StringComparison.Ordinal) ? 0 : null, false));
        if (downloads && request.MaxBytes is { } max && bytes > max)
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new LibraryChangeRejected(
                $"the download is {LibraryItemViewModel.FormatBytes(bytes)}, more than maxBytes ({LibraryItemViewModel.FormatBytes(max)})"));
        }

        switch (action)
        {
            case "load" or "load_as_you_pan":
                // Tracked so a concurrent await_library_idle waits for the opens (#790).
                using (_activity.Begin(action == "load" ? chosen.Count : 0))
                {
                    var loaded = await _open.RunAsync([.. chosen.Select(row => row.State)], defer: action == "load_as_you_pan", ct).ConfigureAwait(true);
                    return LibraryEditOutcome<LibraryActionResult>.Ok(Result(loaded.Opened, false));
                }
            case "cancel":
                foreach (var row in chosen)
                    row.CancelDownloadCommand.Execute(null);
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, false));
            default:
                // Downloads run on in the background, as from the panel; await_library_idle waits for
                // them and for what follows (re-indexing, opening) until RunDownloadAsync ends (#790).
                var load = action == "download";
                _ = RunDownloadAsync(chosen, load, _activity.Begin(load ? chosen.Count : 0));
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, true));
        }
    }

    private async Task RunDownloadAsync(IReadOnlyList<LibraryItemViewModel> rows, bool load, IDisposable activity)
    {
        using var scope = activity;
        try
        {
            await _download.RunAsync([.. rows.Select(row => (row.Item, row.Source))], load).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Failures show on the rows (Failed · Retry) and in await_library_idle's counts.
            Debug.WriteLine($"library_action download failed: {ex}");
        }
    }
}
