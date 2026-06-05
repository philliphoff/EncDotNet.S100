using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Production <see cref="IDatasetLoadGateway"/> that drives the viewer's
/// existing load / unload code paths on the UI thread.
/// </summary>
internal sealed class DatasetLoadGateway : IDatasetLoadGateway
{
    private readonly DatasetsViewModel _datasets;
    private readonly IDatasetLoaderService _loader;
    private readonly IExchangeSetService _exchangeSet;
    private readonly Func<Func<Task>, Task> _dispatcher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public DatasetLoadGateway(
        DatasetsViewModel datasets,
        IDatasetLoaderService loader,
        IExchangeSetService exchangeSet)
        : this(datasets, loader, exchangeSet, dispatcher: null)
    {
    }

    /// <summary>
    /// Test seam: allows tests to provide a synchronous dispatcher so the
    /// production path through <see cref="Dispatcher.UIThread"/> can be
    /// exercised without a running Avalonia application.
    /// </summary>
    internal DatasetLoadGateway(
        DatasetsViewModel datasets,
        IDatasetLoaderService loader,
        IExchangeSetService exchangeSet,
        Func<Func<Task>, Task>? dispatcher)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(exchangeSet);
        _datasets = datasets;
        _loader = loader;
        _exchangeSet = exchangeSet;
        _dispatcher = dispatcher ?? DefaultDispatchAsync;
    }

    /// <inheritdoc />
    public bool IsReady => _loader.IsInitialized;

    /// <inheritdoc />
    public DatasetPathKind Classify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (System.IO.Directory.Exists(path))
        {
            return ExchangeSetDetection.LooksLikeExchangeSetFolder(path)
                ? DatasetPathKind.ExchangeSet
                : DatasetPathKind.File;
        }

        if (ExchangeSetDetection.IsZipPath(path)
            && ExchangeSetDetection.LooksLikeExchangeSetZip(path))
        {
            return DatasetPathKind.ExchangeSet;
        }

        return DatasetPathKind.File;
    }

    /// <inheritdoc />
    public async Task<bool> LoadFileAsync(
        string path, string? specHint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var loaded = false;
        await _dispatcher(async () =>
        {
            var spec = string.IsNullOrWhiteSpace(specHint)
                ? DatasetPipelineFactory.DetectProductSpec(path)
                : specHint.Trim();
            if (spec is null)
            {
                loaded = false;
                return;
            }

            var entry = _datasets.Add(path, spec);
            try
            {
                await _loader.LoadAsync(entry, cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                // Do not leave a half-loaded entry in the panel / catalog
                // when the load throws; remove it before surfacing the error.
                _datasets.Entries.Remove(entry);
                throw;
            }
            loaded = true;
        }).ConfigureAwait(false);
        return loaded;
    }

    /// <inheritdoc />
    public async Task<int> TriggerExchangeSetAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var dispatched = 0;
        await _dispatcher(async () =>
        {
            var result = await _exchangeSet.OpenAsync(path, progress: null, cancellationToken).ConfigureAwait(true);
            dispatched = result.Loaded;
        }).ConfigureAwait(false);
        return dispatched;
    }

    /// <inheritdoc />
    public async Task<int> RemoveAsync(string datasetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);

        var removed = 0;
        await _dispatcher(() =>
        {
            var matches = _datasets.Entries
                .Where(e => string.Equals(e.DisplayName, datasetId, StringComparison.Ordinal))
                .ToList();
            foreach (var entry in matches)
            {
                // Remove from the panel collection AND drop the loader's
                // layers/processor directly. RemoveEntry is idempotent, so
                // the overlap with MainWindow's CollectionChanged handler is
                // a harmless no-op — this keeps the gateway self-sufficient
                // and independent of that window wiring.
                _datasets.Entries.Remove(entry);
                _loader.RemoveEntry(entry);
                removed++;
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return removed;
    }

    /// <inheritdoc />
    public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_operationGate);
    }

    private static async Task DefaultDispatchAsync(Func<Task> work)
    {
        await Dispatcher.UIThread.InvokeAsync(work).ConfigureAwait(false);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
