using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;
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

    public async Task OpenAsync(string folderOrZipPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderOrZipPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureCollectionSubscription();

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
                return;
            }

            var datasets = exchangeSet.Catalogue.DatasetDiscoveryMetadata;
            if (datasets.Count == 0)
            {
                _status.StatusText = string.Format(Strings.Status_ExchangeSetCatalogNotFound, folderOrZipPath);
                exchangeSet.Dispose();
                exchangeSet = null;
                return;
            }

            _status.StatusText = string.Format(Strings.Status_ExchangeSetLoading, folderOrZipPath);

            var tracked = new TrackedExchangeSet(folderOrZipPath, exchangeSet);
            _tracked.Add(tracked);
            // From this point on, lifetime ownership transfers to the tracked
            // entry — do not dispose `exchangeSet` / `source` directly below.
            exchangeSet = null;
            source = null;

            var dispatched = 0;
            var skipped = 0;

            foreach (var metadata in datasets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = ExchangeSet.NormalizeFileName(metadata.FileName);
                var spec = DatasetPipelineFactory.MapProductIdentifierToSpec(
                    metadata.ProductSpecification?.ProductIdentifier);
                if (spec is null)
                {
                    _status.StatusText = string.Format(
                        Strings.Status_ExchangeSetUnsupportedSpec,
                        relativePath,
                        metadata.ProductSpecification?.ProductIdentifier ?? string.Empty);
                    skipped++;
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
            }

            if (skipped == 0)
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

            // If every dataset was skipped (unsupported product specs),
            // there will be no entries to keep the set alive — release it
            // immediately so the file handle / archive is not leaked.
            if (tracked.Entries.Count == 0)
            {
                tracked.ExchangeSet.Dispose();
                _tracked.Remove(tracked);
            }
        }
        catch (OperationCanceledException)
        {
            exchangeSet?.Dispose();
            source?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _status.StatusText = string.Format(Strings.Status_ExchangeSetFailed, folderOrZipPath, ex.Message);
            exchangeSet?.Dispose();
            source?.Dispose();
        }
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
