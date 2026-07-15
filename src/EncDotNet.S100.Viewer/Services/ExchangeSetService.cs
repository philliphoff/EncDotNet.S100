using System.Collections.Specialized;
using System.Diagnostics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IExchangeSetService"/> implementation. Detects
/// folder-vs-ZIP from <paramref name="folderOrZipPath"/>, opens the
/// matching <see cref="IAssetSource"/>, parses <c>CATALOG.XML</c>,
/// and dispatches every catalogued dataset through
/// <see cref="DatasetsViewModel.AddFromExchangeSet"/> +
/// <see cref="DatasetsViewModel.RequestLoad"/>.
/// </summary>
/// <remarks>
/// Lifetime: this service keeps each opened <see cref="ExchangeSet"/>
/// (and its underlying <see cref="IAssetSource"/>) alive for as long
/// as any of the dispatched <see cref="DatasetEntry"/>s remains in
/// <see cref="DatasetsViewModel.Entries"/>. When the last entry from
/// a given exchange set is removed, the set is disposed. Disposing
/// the service eagerly disposes any still-tracked sets.
/// </remarks>
internal sealed class ExchangeSetService : IExchangeSetService, IDisposable
{
    private readonly DatasetsViewModel _datasets;
    private readonly INotificationService _notifications;
    private readonly List<TrackedExchangeSet> _tracked = new();
    private bool _subscribed;
    private bool _disposed;

    public ExchangeSetService(DatasetsViewModel datasets, INotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(notifications);
        _datasets = datasets;
        _notifications = notifications;
    }

    /// <summary>
    /// Drives the caller-supplied progress notification to a terminal state
    /// (clearing the progress bar and any actions and scheduling auto-dismiss),
    /// or surfaces a fresh notification when no handle was supplied.
    /// </summary>
    private void Terminal(
        INotificationHandle? notification,
        NotificationSeverity severity,
        string title,
        string message)
    {
        if (notification is not null && !notification.IsDismissed)
        {
            notification.ClearProgress();
            notification.SetActions();
            notification.Update(title: title, message: message, severity: severity);
            notification.ScheduleAutoDismiss(NotificationService.DefaultDelayFor(severity));
        }
        else
        {
            _notifications.Create(title)
                .WithSeverity(severity)
                .WithContent(message)
                .Show();
        }
    }

    public async Task<ExchangeSetOpenResult> OpenAsync(
        string folderOrZipPath,
        IProgress<ExchangeSetProgress>? progress = null,
        CancellationToken cancellationToken = default,
        INotificationHandle? notification = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderOrZipPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureCollectionSubscription();

        // An S-57 / S-63 exchange set (CATALOG.031) is structurally different
        // from an S-100 one (CATALOG.XML) and is directory-rooted, so it has its
        // own loader. S-100 sets fall through to the logic below.
        if (ExchangeSetDetection.LooksLikeS57ExchangeSet(folderOrZipPath))
        {
            return await OpenS57Async(folderOrZipPath, progress, cancellationToken, notification)
                .ConfigureAwait(true);
        }

        // s100.exchangeset.open child span sits under whatever
        // s100.viewer.command span the caller (MainWindow) opened.
        using var activity = Telemetry.ActivitySource.StartActivity(
            "s100.exchangeset.open", System.Diagnostics.ActivityKind.Internal);
        var sourceKind = ResolveSourceKind(folderOrZipPath);
        activity?.SetTag("s100.exchangeset.source.kind", sourceKind);
        activity?.SetTag("s100.exchangeset.source.path", folderOrZipPath);

        IAssetSource? source = null;
        ExchangeSet? exchangeSet = null;
        try
        {
            source = OpenSource(folderOrZipPath);
            try
            {
                exchangeSet = await ExchangeSet.OpenAsync(source, "CATALOG.XML", cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (FileNotFoundException)
            {
                var msg = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath);
                Terminal(notification, NotificationSeverity.Warning, Strings.Toast_ExchangeSetFailed, msg);
                source.Dispose();
                activity?.SetStatus(ActivityStatusCode.Error, "catalogue not found");
                return new ExchangeSetOpenResult
                {
                    SourcePath = folderOrZipPath,
                    CatalogueNotFound = true,
                    FailureMessage = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath),
                };
            }

            var datasets = exchangeSet.Catalogue.DatasetDiscoveryMetadata;
            activity?.SetTag("s100.exchangeset.dataset.count", datasets.Count);
            activity?.SetTag(
                "s100.exchangeset.producer",
                exchangeSet.Catalogue.Contact?.Organization);
            activity?.SetTag(
                "s100.exchangeset.product",
                exchangeSet.Catalogue.ProductSpecification?.ProductIdentifier);

            if (datasets.Count == 0)
            {
                var emptyMsg = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath);
                Terminal(notification, NotificationSeverity.Warning, Strings.Toast_ExchangeSetFailed, emptyMsg);
                exchangeSet.Dispose();
                exchangeSet = null;
                activity?.SetStatus(ActivityStatusCode.Error, "empty catalogue");
                return new ExchangeSetOpenResult
                {
                    SourcePath = folderOrZipPath,
                    CatalogueNotFound = true,
                    FailureMessage = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath),
                };
            }

            // Group S-101 base cells with their in-set sequential updates
            // (….001/.002/…) so each cell loads as a single up-to-date
            // dataset rather than one entry per file. Non-S-101 datasets
            // and S-101 cells with no in-set updates are unaffected.
            // S-101 / S-100 Part 10a.
            var plan = S101ExchangeSetUpdatePlan.Build(datasets);
            activity?.SetTag("s100.exchangeset.plan.count", plan.Count);

            progress?.Report(new ExchangeSetProgress(folderOrZipPath, plan.Count, 0, 0, null));

            var assetSource = exchangeSet.Source;
            var catalogue = exchangeSet.Catalogue;
            var tracked = new TrackedExchangeSet(
                folderOrZipPath,
                assetSource,
                owner: exchangeSet,
                verifier: ct => new ExchangeSetVerifier().VerifyAsync(
                    assetSource,
                    catalogue,
                    new TrustAnchorOptions { AllowUntrustedCertificates = true },
                    ct));
            _tracked.Add(tracked);
            // From this point on, lifetime ownership transfers to the tracked
            // entry — do not dispose `exchangeSet` / `source` directly below.
            var producer = catalogue.Contact?.Organization;
            var issueDate = ResolveLatestIssueDate(datasets);

            tracked.Header = _datasets.RegisterExchangeSetHeader(
                assetSource,
                folderOrZipPath,
                producer,
                issueDate,
                plan.Count,
                closeAction: CloseExchangeSetFromHeader);
            exchangeSet = null;
            source = null;

            var dispatched = 0;
            var skipped = 0;
            var completedLoads = 0;
            var skipMessages = new List<string>();
            var cancelled = false;
            var loadTasks = new List<Task>();

            foreach (var item in plan)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var metadata = item.Base;
                var relativePath = metadata.RelativePath;

                // An S-101 update with no base cell in this exchange set
                // cannot be applied on its own. Per the best-effort policy
                // we skip it with a warning rather than failing the load.
                if (item.Kind == S101LoadItemKind.OrphanUpdate)
                {
                    var orphanMsg = string.Format(Strings.Status_ExchangeSetOrphanUpdate, relativePath);
                    _notifications.Create(Strings.Toast_Warning)
                        .WithSeverity(NotificationSeverity.Warning)
                        .WithContent(orphanMsg)
                        .Show();
                    skipMessages.Add(orphanMsg);
                    skipped++;
                    progress?.Report(new ExchangeSetProgress(
                        folderOrZipPath, plan.Count, completedLoads + skipped, skipped, relativePath));
                    continue;
                }

                var spec = DatasetPipelineFactory.MapProductSpecificationToSpec(
                    metadata.ProductSpecification);
                if (spec is null)
                {
                    var msg = string.Format(
                        Strings.Status_ExchangeSetUnsupportedSpec,
                        relativePath,
                        metadata.ProductSpecification?.ProductIdentifier
                            ?? metadata.ProductSpecification?.Name
                            ?? string.Empty);
                    _notifications.Create(Strings.Toast_Warning)
                        .WithSeverity(NotificationSeverity.Warning)
                        .WithContent(msg)
                        .Show();
                    skipMessages.Add(msg);
                    skipped++;
                    progress?.Report(new ExchangeSetProgress(
                        folderOrZipPath, plan.Count, completedLoads + skipped, skipped, relativePath));
                    continue;
                }

                var updateRelativePaths = item.Updates.Count == 0
                    ? (IReadOnlyList<string>)Array.Empty<string>()
                    : item.Updates.Select(u => u.RelativePath).ToList();

                var entry = _datasets.AddFromExchangeSet(
                    tracked.Source,
                    relativePath,
                    spec,
                    displayName: Path.GetFileName(relativePath),
                    updateRelativePaths: updateRelativePaths);
                tracked.Entries.Add(entry);
                loadTasks.Add(_datasets.RequestLoadAsync(entry));
                dispatched++;
            }

            // Report progress on actual load completions rather than dispatch:
            // the loop above queues every cell almost instantly, so advancing the
            // bar there would race it to 100% before any cell had parsed. Each
            // cell load runs concurrently (the loader offloads parse/render to the
            // thread pool); as one finishes we bump the determinate fraction.
            foreach (var loadTask in loadTasks)
            {
                _ = loadTask.ContinueWith(
                    _ =>
                    {
                        var done = Interlocked.Increment(ref completedLoads);
                        progress?.Report(new ExchangeSetProgress(
                            folderOrZipPath, plan.Count, done + skipped, skipped, null));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            // Await the dispatched per-cell loads so the aggregate "loaded"
            // outcome reflects datasets that have actually parsed and added
            // their layers — not merely been queued. The loader surfaces any
            // per-cell failure on its own and never throws, so this completes
            // even when an individual cell fails.
            await Task.WhenAll(loadTasks).ConfigureAwait(true);

            ExchangeSetTerminalInfo? pendingTerminal = null;
            var sourceLabel = Notifications.NotificationFormat.ShortenPath(folderOrZipPath);
            if (cancelled)
            {
                var cancelledMsg = string.Format(
                    Strings.Status_ExchangeSetCancelled,
                    dispatched, plan.Count, sourceLabel);
                Terminal(notification, NotificationSeverity.Info, Strings.Toast_Info, cancelledMsg);
            }
            else if (skipped == 0)
            {
                var loadedMsg = string.Format(
                    Strings.Status_ExchangeSetLoaded, dispatched, sourceLabel);
                pendingTerminal = new ExchangeSetTerminalInfo(
                    NotificationSeverity.Success, Strings.Toast_ExchangeSetLoaded, loadedMsg);
            }
            else
            {
                var partialMsg = string.Format(
                    Strings.Status_ExchangeSetLoadedWithErrors,
                    dispatched, plan.Count, sourceLabel, skipped);
                pendingTerminal = new ExchangeSetTerminalInfo(
                    NotificationSeverity.Warning, Strings.Toast_ExchangeSetLoaded, partialMsg);
            }

            activity?.SetTag("s100.exchangeset.dataset.loaded", dispatched);
            activity?.SetTag("s100.exchangeset.dataset.skipped", skipped);
            activity?.SetTag("s100.exchangeset.cancelled", cancelled);
            activity?.SetStatus(
                cancelled ? ActivityStatusCode.Error : ActivityStatusCode.Ok,
                cancelled ? "cancelled" : null);

            // Update the header now that we know the loaded/unsupported
            // split; the header was registered with conservative defaults
            // before the dispatch loop ran so progress UI had something
            // to show.
            if (tracked.Header is { } trackedHeader)
            {
                trackedHeader.LoadedCount = dispatched;
                trackedHeader.UnsupportedCount = skipped;
            }

            // If every dataset was skipped (unsupported product specs),
            // there will be no entries to keep the set alive — release it
            // immediately so the file handle / archive is not leaked.
            if (tracked.Entries.Count == 0)
            {
                if (tracked.Header is { } orphanHeader)
                {
                    _datasets.RemoveExchangeSetHeader(orphanHeader);
                    tracked.Header = null;
                }
                tracked.Owner.Dispose();
                _tracked.Remove(tracked);
            }
            else
            {
                // Fire-and-forget: verify exchange set signatures in the
                // background. The result is surfaced as a non-blocking badge
                // on the header — we never refuse to load unsigned data.
                _ = VerifySignaturesAsync(tracked);
            }

            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrZipPath,
                Total = plan.Count,
                Loaded = dispatched,
                SkippedUnsupported = skipped,
                Cancelled = cancelled,
                SkipMessages = skipMessages,
                UnionBoundingBox = ComputeUnionBoundingBox(datasets),
                PendingTerminal = pendingTerminal,
            };
        }
        catch (OperationCanceledException)
        {
            exchangeSet?.Dispose();
            source?.Dispose();
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrZipPath,
                Cancelled = true,
            };
        }
        catch (Exception ex)
        {
            var failedMsg = string.Format(Strings.Status_ExchangeSetFailed, folderOrZipPath, ex.Message);
            Terminal(notification, NotificationSeverity.Error, Strings.Toast_ExchangeSetFailed, failedMsg);
            exchangeSet?.Dispose();
            source?.Dispose();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrZipPath,
                FailureMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Opens an S-57 / S-63 exchange set (<c>CATALOG.031</c>). Unlike the S-100
    /// path, the S-57 model is directory-rooted: cells are enumerated from the
    /// catalogue via <see cref="S57ExchangeSetCatalog"/>, a
    /// <see cref="FileSystemAssetSource"/> is rooted at the exchange-set
    /// directory, and each base cell (with its in-set sequential updates) is
    /// dispatched as an <c>"S-57"</c> entry — flowing through the same
    /// <c>S57DatasetProcessor</c> as a single dropped <c>.000</c> file.
    /// Integrity/signature status reuses the shared header badge, driven by the
    /// PR #265 <see cref="S57ExchangeSetVerification"/> adapter.
    /// </summary>
    private async Task<ExchangeSetOpenResult> OpenS57Async(
        string folderOrCataloguePath,
        IProgress<ExchangeSetProgress>? progress,
        CancellationToken cancellationToken,
        INotificationHandle? notification)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(
            "s57.exchangeset.open", System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag("s57.exchangeset.source.path", folderOrCataloguePath);

        IAssetSource? source = null;
        try
        {
            string root;
            IReadOnlyList<S57ExchangeSetCell> cells;
            try
            {
                root = ExchangeSetDetection.ResolveS57Root(folderOrCataloguePath);
                cells = S57ExchangeSetCatalog.ReadBaseCells(root);
            }
            catch (FileNotFoundException)
            {
                var msg = string.Format(
                    Strings.Status_ExchangeSetCatalogNotFound, folderOrCataloguePath);
                Terminal(notification, NotificationSeverity.Warning, Strings.Toast_ExchangeSetFailed, msg);
                activity?.SetStatus(ActivityStatusCode.Error, "catalogue not found");
                return new ExchangeSetOpenResult
                {
                    SourcePath = folderOrCataloguePath,
                    CatalogueNotFound = true,
                    FailureMessage = msg,
                };
            }

            activity?.SetTag("s57.exchangeset.cell.count", cells.Count);

            if (cells.Count == 0)
            {
                var emptyMsg = string.Format(
                    Strings.Status_S57ExchangeSetNoCells, folderOrCataloguePath);
                Terminal(notification, NotificationSeverity.Warning, Strings.Toast_ExchangeSetFailed, emptyMsg);
                activity?.SetStatus(ActivityStatusCode.Error, "no cells");
                return new ExchangeSetOpenResult
                {
                    SourcePath = folderOrCataloguePath,
                    CatalogueNotFound = true,
                    FailureMessage = emptyMsg,
                };
            }

            progress?.Report(new ExchangeSetProgress(folderOrCataloguePath, cells.Count, 0, 0, null));

            source = FileSystemAssetSource.Create(root);
            var tracked = new TrackedExchangeSet(
                folderOrCataloguePath,
                source,
                owner: source,
                verifier: ct => S57ExchangeSetVerification.VerifyAsync(
                    root, allowUntrustedCertificates: true, ct));
            _tracked.Add(tracked);
            // Lifetime ownership has transferred to the tracked entry; do not
            // dispose `source` directly below.
            source = null;

            tracked.Header = _datasets.RegisterExchangeSetHeader(
                tracked.Source,
                // Use the resolved root directory (not a dropped CATALOG.031
                // file path) so the header's display name is the set folder.
                root,
                producer: null,
                issueDate: null,
                cells.Count,
                closeAction: CloseExchangeSetFromHeader);

            var dispatched = 0;
            var completedLoads = 0;
            var loadTasks = new List<Task>();
            foreach (var cell in cells)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<string> updateRelativePaths = cell.UpdateRelativePaths.IsDefaultOrEmpty
                    ? Array.Empty<string>()
                    : cell.UpdateRelativePaths;

                var entry = _datasets.AddFromExchangeSet(
                    tracked.Source,
                    cell.RelativePath,
                    "S-57",
                    displayName: cell.CellName,
                    updateRelativePaths: updateRelativePaths);
                tracked.Entries.Add(entry);
                loadTasks.Add(_datasets.RequestLoadAsync(entry));
                dispatched++;
            }

            // Report progress on actual load completions rather than dispatch
            // (see the S-100 path for rationale): the loop queues every cell
            // almost instantly, so the bar stays indeterminate until a cell
            // truly finishes, then fills as each one lands.
            foreach (var loadTask in loadTasks)
            {
                _ = loadTask.ContinueWith(
                    _ =>
                    {
                        var done = Interlocked.Increment(ref completedLoads);
                        progress?.Report(new ExchangeSetProgress(
                            folderOrCataloguePath, cells.Count, done, 0, null));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            // Await the dispatched per-cell loads so "loaded" reflects cells
            // that have actually parsed and added their layers (see the S-100
            // path for rationale). The loader never throws.
            await Task.WhenAll(loadTasks).ConfigureAwait(true);

            var loadedMsg = string.Format(
                Strings.Status_ExchangeSetLoaded, dispatched,
                Notifications.NotificationFormat.ShortenPath(folderOrCataloguePath));
            var pendingTerminal = new ExchangeSetTerminalInfo(
                NotificationSeverity.Success, Strings.Toast_ExchangeSetLoaded, loadedMsg);

            activity?.SetTag("s57.exchangeset.dataset.loaded", dispatched);
            activity?.SetStatus(ActivityStatusCode.Ok);

            if (tracked.Header is { } trackedHeader)
            {
                trackedHeader.LoadedCount = dispatched;
                trackedHeader.UnsupportedCount = 0;
            }

            // Fire-and-forget integrity/signature verification — surfaced as a
            // non-blocking header badge; we never refuse to load unsigned data.
            _ = VerifySignaturesAsync(tracked);

            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrCataloguePath,
                Total = cells.Count,
                Loaded = dispatched,
                SkippedUnsupported = 0,
                Cancelled = false,
                SkipMessages = Array.Empty<string>(),
                UnionBoundingBox = S57ExchangeSetCatalog.UnionBoundingBox(cells),
                PendingTerminal = pendingTerminal,
            };
        }
        catch (OperationCanceledException)
        {
            source?.Dispose();
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrCataloguePath,
                Cancelled = true,
            };
        }
        catch (Exception ex)
        {
            var failedMsg = string.Format(Strings.Status_ExchangeSetFailed, folderOrCataloguePath, ex.Message);
            Terminal(notification, NotificationSeverity.Error, Strings.Toast_ExchangeSetFailed, failedMsg);
            source?.Dispose();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrCataloguePath,
                FailureMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Computes the EPSG:4326 union of every dataset's bounding box,
    /// ignoring entries that lack one. Returns <c>null</c> if no
    /// dataset declared a bounding box.
    /// </summary>
    /// <remarks>
    /// Antimeridian-spanning catalogues are not handled here — if a
    /// producer ever ships one, this will return an over-wide box.
    /// Exposed as <c>internal</c> for unit testing.
    /// </remarks>
    internal static BoundingBox? ComputeUnionBoundingBox(
        IReadOnlyList<DatasetDiscoveryMetadata> datasets)
    {
        double? west = null, east = null, south = null, north = null;
        foreach (var d in datasets)
        {
            var b = d.BoundingBox;
            if (b is null) continue;
            west = west is null ? b.WestBoundLongitude : Math.Min(west.Value, b.WestBoundLongitude);
            east = east is null ? b.EastBoundLongitude : Math.Max(east.Value, b.EastBoundLongitude);
            south = south is null ? b.SouthBoundLatitude : Math.Min(south.Value, b.SouthBoundLatitude);
            north = north is null ? b.NorthBoundLatitude : Math.Max(north.Value, b.NorthBoundLatitude);
        }
        if (west is null) return null;
        return new BoundingBox
        {
            WestBoundLongitude = west.Value,
            EastBoundLongitude = east!.Value,
            SouthBoundLatitude = south!.Value,
            NorthBoundLatitude = north!.Value,
        };
    }

    private static string ResolveSourceKind(string path)
    {
        if (Directory.Exists(path)) return "folder";
        if (File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "zip";
        }
        return "unknown";
    }

    private static IAssetSource OpenSource(string path)
    {
        if (Directory.Exists(path))
        {
            return FileSystemAssetSource.Create(path);
        }
        if (File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            // ZipAssetSource.Create(string) opens the file with read-share,
            // so the archive can stay open for as long as the service holds it.
            return ZipAssetSource.Create(path);
        }
        throw new FileNotFoundException(
            $"Exchange set source not found or not a folder/.zip: {path}", path);
    }

    private void EnsureCollectionSubscription()
    {
        if (_subscribed) return;
        _subscribed = true;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only Remove and Reset can drop entries the service is tracking.
        if (e.Action is not (NotifyCollectionChangedAction.Remove or
            NotifyCollectionChangedAction.Replace or
            NotifyCollectionChangedAction.Reset))
        {
            return;
        }

        // Walk a snapshot so we can dispose+remove tracked sets safely.
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            var tracked = _tracked[i];
            for (var j = tracked.Entries.Count - 1; j >= 0; j--)
            {
                if (!_datasets.Entries.Contains(tracked.Entries[j]))
                {
                    tracked.Entries.RemoveAt(j);
                }
            }

            if (tracked.Entries.Count == 0)
            {
                if (tracked.Header is { } header)
                {
                    _datasets.RemoveExchangeSetHeader(header);
                    tracked.Header = null;
                }
                tracked.Owner.Dispose();
                _tracked.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Removes every <see cref="DatasetEntry"/> that came from the
    /// supplied header's exchange set. The collection-changed listener
    /// then disposes the underlying <see cref="ExchangeSet"/> and
    /// unregisters the header in the same pass.
    /// </summary>
    private void CloseExchangeSetFromHeader(ExchangeSetHeader header)
    {
        var tracked = _tracked.Find(t => ReferenceEquals(t.Header, header));
        if (tracked is null) return;

        // Snapshot the entries: removing from `_datasets.Entries`
        // mutates `tracked.Entries` indirectly via OnEntriesChanged.
        var entriesToRemove = tracked.Entries.ToArray();
        foreach (var entry in entriesToRemove)
        {
            _datasets.Entries.Remove(entry);
        }
    }

    /// <summary>
    /// Returns the lexically-greatest non-null
    /// <see cref="DatasetDiscoveryMetadata.IssueDate"/> across the
    /// catalogue, or <c>null</c> if no dataset declared one. 
    /// </summary>
    private static DateOnly? ResolveLatestIssueDate(
        IReadOnlyList<DatasetDiscoveryMetadata> datasets)
    {
        DateOnly? latest = null;
        foreach (var d in datasets)
        {
            var s = d.IssueDate;
            if (s == null) continue;
            if (latest is null || ((DateOnly)s).CompareTo((DateOnly)latest) > 0)
                latest = s;
        }
        return latest;
    }

    /// <summary>
    /// Runs signature verification in the background and updates the
    /// exchange set header with the result. Non-blocking — errors are
    /// swallowed and surfaced as <see cref="SignatureStatus.Error"/>.
    /// </summary>
    private async Task VerifySignaturesAsync(TrackedExchangeSet tracked)
    {
        if (tracked.Header is null) return;
        if (tracked.Verifier is null) return;

        tracked.Header.SignatureStatus = SignatureStatus.Checking;
        tracked.Header.SignatureTooltip = Strings.Tooltip_SignatureChecking;

        try
        {
            var result = await tracked.Verifier(CancellationToken.None).ConfigureAwait(true);
            ApplySignatureResult(tracked.Header, result);
        }
        catch (Exception)
        {
            tracked.Header.SignatureStatus = SignatureStatus.Error;
            tracked.Header.SignatureTooltip = Strings.Tooltip_SignatureError;
        }
    }

    /// <summary>
    /// Maps an <see cref="ExchangeSetVerificationResult"/> onto the header's
    /// signature badge. Shared by the S-100 and S-57 exchange-set paths so both
    /// surface identical integrity/signature semantics.
    /// </summary>
    private static void ApplySignatureResult(ExchangeSetHeader header, ExchangeSetVerificationResult result)
    {
        if (result.IsUnsigned)
        {
            header.SignatureStatus = SignatureStatus.Unsigned;
            header.SignatureTooltip = Strings.Tooltip_SignatureUnsigned;
        }
        else if (result.AllValid)
        {
            header.SignatureStatus = SignatureStatus.Verified;
            header.SignatureTooltip = Strings.Tooltip_SignatureVerified;
        }
        else if (result.HasInvalidSignatures)
        {
            header.SignatureStatus = SignatureStatus.Invalid;
            header.SignatureTooltip = Strings.Tooltip_SignatureInvalid;
        }
        else
        {
            // Some files ok, some not — e.g. certificate issues
            header.SignatureStatus = SignatureStatus.Mixed;
            header.SignatureTooltip = Strings.Tooltip_SignatureMixed;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_subscribed)
        {
            _datasets.Entries.CollectionChanged -= OnEntriesChanged;
            _subscribed = false;
        }

        foreach (var tracked in _tracked)
        {
            try { tracked.Owner.Dispose(); } catch { /* swallow on shutdown */ }
        }
        _tracked.Clear();
    }

    private sealed class TrackedExchangeSet
    {
        public string SourcePath { get; }

        /// <summary>The asset source backing the set; matched against
        /// <see cref="DatasetEntry.Source"/> for lifetime tracking.</summary>
        public IAssetSource Source { get; }

        /// <summary>The object whose disposal releases the set's underlying
        /// resources — the S-100 <see cref="ExchangeSet"/> for S-100 sets, or the
        /// <see cref="IAssetSource"/> itself for S-57 sets.</summary>
        public IDisposable Owner { get; }

        /// <summary>Produces the integrity/signature verification result for the
        /// header badge, or <c>null</c> when verification is not applicable.</summary>
        public Func<CancellationToken, Task<ExchangeSetVerificationResult>>? Verifier { get; }

        public List<DatasetEntry> Entries { get; } = new();
        public ExchangeSetHeader? Header { get; set; }

        public TrackedExchangeSet(
            string sourcePath,
            IAssetSource source,
            IDisposable owner,
            Func<CancellationToken, Task<ExchangeSetVerificationResult>>? verifier)
        {
            SourcePath = sourcePath;
            Source = source;
            Owner = owner;
            Verifier = verifier;
        }
    }
}
