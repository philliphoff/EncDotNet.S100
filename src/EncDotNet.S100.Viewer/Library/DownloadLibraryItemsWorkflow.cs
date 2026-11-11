using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Downloads Library items, then optionally opens them (#1394): the Library
/// panel's Download commands, row Retry, the Timeline's Get and MCP
/// <c>library_action</c> all download through it.
/// </summary>
/// <remarks>
/// <para>
/// The steps, as <see cref="LibraryOperations.DownloadAsync"/> takes them for a
/// host without view models: close the open copies the download replaces,
/// download, re-index the downloaded items' sources, then either wait for an
/// unpacked package's datasets to be listed or open the downloaded items, and
/// finally reopen what was closed. The viewer's own loader and downloader
/// add the Datasets-row labels and progress notifications that
/// <see cref="LibraryOperations"/> doesn't have, so this runs the same steps
/// over them.
/// </para>
/// <para>Runs on the UI thread.</para>
/// </remarks>
internal sealed class DownloadLibraryItemsWorkflow
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryDownloader _downloader;
    private readonly ILibraryLoader _loader;
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly INotificationService _notifications;

    /// <summary>Creates the workflow.</summary>
    /// <param name="library">The Library the items come from (their sources re-index after a download).</param>
    /// <param name="downloader">Downloads the items.</param>
    /// <param name="loader">Knows which replaced copies are open, and closes them.</param>
    /// <param name="open">Opens the downloaded items, and reopens the closed copies.</param>
    /// <param name="notifications">Says how many datasets an unpacked package held.</param>
    public DownloadLibraryItemsWorkflow(
        CollectionLibrary library,
        ILibraryDownloader downloader,
        ILibraryLoader loader,
        OpenLibraryItemsWorkflow open,
        INotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(notifications);
        _library = library;
        _downloader = downloader;
        _loader = loader;
        _open = open;
        _notifications = notifications;
    }

    /// <summary>
    /// Raised before a download starts, with the items it downloads (the
    /// Library tree's status lines say which sources are downloading).
    /// </summary>
    public event EventHandler<IReadOnlyList<(CollectionItem Item, LibrarySource Source)>>? Starting;

    /// <summary>
    /// Raised once the Library lists the datasets of downloaded packages, with
    /// each package's source and name (the Library list opens their groups).
    /// </summary>
    public event EventHandler<IReadOnlyList<(Guid Source, string Package)>>? PackagesUnpacked;

    /// <summary>
    /// Downloads the downloadable items among <paramref name="items"/>, then
    /// with <paramref name="load"/> opens them. A downloaded package isn't
    /// opened: its datasets are listed instead, and announced.
    /// </summary>
    /// <param name="items">The items as indexed (not their local copies), each with its source.</param>
    /// <param name="load">True to open the downloaded items.</param>
    /// <param name="cancellationToken">Cancels the download (and what follows).</param>
    /// <returns>What the download, and any load, did.</returns>
    public async Task<LibraryDownloadOutcome> RunAsync(
        IReadOnlyList<(CollectionItem Item, LibrarySource Source)> items, bool load, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        Starting?.Invoke(this, items);

        // An open copy holds its files open, and Windows won't replace them
        // (#1075); closing also lets the new copy open rather than the old one
        // staying on the map.
        var replaced = LibraryReplacedCopies.Close(
            items.Where(i => _downloader.CanDownload(i.Item)).Select(i => (i.Item, (LibrarySource?)i.Source)),
            _downloader,
            _loader.StateOf,
            _loader.Close);
        try
        {
            return await DownloadAndOpenAsync(items, load, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            // The new copy, or the old one when the download failed, unless already open.
            await replaced.ReopenAsync((reopen, defer) => _open.RunAsync(reopen, defer, CancellationToken.None)).ConfigureAwait(true);
        }
    }

    private async Task<LibraryDownloadOutcome> DownloadAndOpenAsync(
        IReadOnlyList<(CollectionItem Item, LibrarySource Source)> items, bool load, CancellationToken cancellationToken)
    {
        var download = await _downloader.DownloadAsync([.. items.Select(i => i.Item)], cancellationToken).ConfigureAwait(true);
        if (download.Downloaded == 0)
            return new LibraryDownloadOutcome(download, [], null);

        // What an index takes from a downloaded copy (a package's datasets, a
        // SECOM object's bounds and signature) appears once its source
        // re-indexes; one whose index does not read its copies is confirmed
        // unchanged by its fingerprint.
        foreach (var source in items.Where(i => i.Item.Location is RemoteItemLocation).Select(i => i.Source.Id).Distinct())
            _library.Refresh(sourceId: source);

        // A package lists its datasets only after that; items with a stated
        // layout (S-100 feeds) are listed as themselves already.
        if (items.Any(i => i.Item.Location is RemoteItemLocation { Package: not null, Layout: null }))
        {
            var reindexed = await AnnouncePackagesAsync(items).ConfigureAwait(true);
            return new LibraryDownloadOutcome(download, reindexed, null);
        }

        if (!load)
            return new LibraryDownloadOutcome(download, [], null);

        var opened = await _open.RunAsync(
            [.. items.Select(i => (_downloader.Localize(i.Item), (LibrarySource?)i.Source))], defer: false, cancellationToken).ConfigureAwait(true);
        return new LibraryDownloadOutcome(download, [], opened);
    }

    /// <summary>
    /// Once the re-index lists the unpacked packages' datasets, raises
    /// <see cref="PackagesUnpacked"/> and says how many datasets they held.
    /// </summary>
    /// <returns>The sources of the unpacked packages.</returns>
    private async Task<IReadOnlyList<Guid>> AnnouncePackagesAsync(IReadOnlyList<(CollectionItem Item, LibrarySource Source)> items)
    {
        var packages = items
            .Where(i => LibraryItemText.IsPackageEntry(i.Item))
            .Select(i => (Source: i.Source.Id, Package: ((RemoteItemLocation)i.Item.Location).Package!))
            .Distinct()
            .ToArray();
        if (packages.Length == 0)
            return [];

        await _library.WhenIdle().ConfigureAwait(true);

        var datasets = packages.Sum(p => _library.Collections
            .SelectMany(c => c.Sources)
            .Where(s => s.Id == p.Source)
            .SelectMany(s => s.Index?.Items ?? [])
            .Count(i => i.Properties.TryGetValue("package", out var package) && package == p.Package && !LibraryItemText.IsPackageEntry(i)));
        PackagesUnpacked?.Invoke(this, packages);
        if (datasets > 0)
        {
            _notifications.Create(Strings.Toast_LibraryUnpackedTitle)
                .WithSeverity(NotificationSeverity.Success)
                .WithContent(packages.Length == 1
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryUnpackedFormat, datasets)
                    : string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryUnpackedManyFormat, packages.Length, datasets))
                .Show();
        }

        return [.. packages.Select(p => p.Source).Distinct()];
    }
}
