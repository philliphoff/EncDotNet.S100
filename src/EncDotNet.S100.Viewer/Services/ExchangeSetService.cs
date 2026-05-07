using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
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
    private readonly IStatusPresenter _status;
    private readonly List<TrackedExchangeSet> _tracked = new();
    private bool _subscribed;
    private bool _disposed;

    public ExchangeSetService(DatasetsViewModel datasets, IStatusPresenter status)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(status);
        _datasets = datasets;
        _status = status;
    }

    public async Task<ExchangeSetOpenResult> OpenAsync(
        string folderOrZipPath,
        IProgress<ExchangeSetProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderOrZipPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureCollectionSubscription();

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
                _status.StatusText = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath);
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
                _status.StatusText = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath);
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

            _status.StatusText = string.Format(Strings.Status_ExchangeSetLoading, folderOrZipPath);
            progress?.Report(new ExchangeSetProgress(folderOrZipPath, datasets.Count, 0, 0, null));

            var tracked = new TrackedExchangeSet(folderOrZipPath, exchangeSet);
            _tracked.Add(tracked);
            // From this point on, lifetime ownership transfers to the tracked
            // entry — do not dispose `exchangeSet` / `source` directly below.
            exchangeSet = null;
            source = null;

            var dispatched = 0;
            var skipped = 0;
            var skipMessages = new List<string>();
            var cancelled = false;

            foreach (var metadata in datasets)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var relativePath = ExchangeSet.NormalizeFileName(metadata.FileName);
                var spec = DatasetPipelineFactory.MapProductIdentifierToSpec(
                    metadata.ProductSpecification?.ProductIdentifier);
                if (spec is null)
                {
                    var msg = string.Format(
                        Strings.Status_ExchangeSetUnsupportedSpec,
                        relativePath,
                        metadata.ProductSpecification?.ProductIdentifier ?? string.Empty);
                    _status.StatusText = msg;
                    skipMessages.Add(msg);
                    skipped++;
                    progress?.Report(new ExchangeSetProgress(
                        folderOrZipPath, datasets.Count, dispatched + skipped, skipped, relativePath));
                    continue;
                }

                var entry = _datasets.AddFromExchangeSet(
                    tracked.ExchangeSet.Source,
                    relativePath,
                    spec,
                    displayName: Path.GetFileName(relativePath));
                tracked.Entries.Add(entry);
                _datasets.RequestLoad(entry);
                dispatched++;
                progress?.Report(new ExchangeSetProgress(
                    folderOrZipPath, datasets.Count, dispatched + skipped, skipped, relativePath));
            }

            if (cancelled)
            {
                _status.StatusText = string.Format(
                    Strings.Status_ExchangeSetCancelled,
                    dispatched, datasets.Count, folderOrZipPath);
            }
            else if (skipped == 0)
            {
                _status.StatusText = string.Format(
                    Strings.Status_ExchangeSetLoaded, dispatched, folderOrZipPath);
            }
            else
            {
                _status.StatusText = string.Format(
                    Strings.Status_ExchangeSetLoadedWithErrors,
                    dispatched, datasets.Count, folderOrZipPath, skipped);
            }

            activity?.SetTag("s100.exchangeset.dataset.loaded", dispatched);
            activity?.SetTag("s100.exchangeset.dataset.skipped", skipped);
            activity?.SetTag("s100.exchangeset.cancelled", cancelled);
            activity?.SetStatus(
                cancelled ? ActivityStatusCode.Error : ActivityStatusCode.Ok,
                cancelled ? "cancelled" : null);

            // If every dataset was skipped (unsupported product specs),
            // there will be no entries to keep the set alive — release it
            // immediately so the file handle / archive is not leaked.
            if (tracked.Entries.Count == 0)
            {
                tracked.ExchangeSet.Dispose();
                _tracked.Remove(tracked);
            }

            return new ExchangeSetOpenResult
            {
                SourcePath = folderOrZipPath,
                Total = datasets.Count,
                Loaded = dispatched,
                SkippedUnsupported = skipped,
                Cancelled = cancelled,
                SkipMessages = skipMessages,
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
            _status.StatusText = string.Format(Strings.Status_ExchangeSetFailed, folderOrZipPath, ex.Message);
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
                tracked.ExchangeSet.Dispose();
                _tracked.RemoveAt(i);
            }
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
            try { tracked.ExchangeSet.Dispose(); } catch { /* swallow on shutdown */ }
        }
        _tracked.Clear();
    }

    private sealed class TrackedExchangeSet
    {
        public string SourcePath { get; }
        public ExchangeSet ExchangeSet { get; }
        public List<DatasetEntry> Entries { get; } = new();

        public TrackedExchangeSet(string sourcePath, ExchangeSet exchangeSet)
        {
            SourcePath = sourcePath;
            ExchangeSet = exchangeSet;
        }
    }
}
