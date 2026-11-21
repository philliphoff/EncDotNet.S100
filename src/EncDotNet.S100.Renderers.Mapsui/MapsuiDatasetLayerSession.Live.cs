using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

// Datasets whose processor is an ILiveDatasetProcessor: the session listens to
// its Changed event and draws the dataset again.
public sealed partial class MapsuiDatasetLayerSession
{
    // Long enough to fold a burst of changes (a feed applying several updates)
    // into one render, short enough that a refresh still feels immediate.
    private static readonly TimeSpan LiveRefreshDebounceWindow =
        TimeSpan.FromMilliseconds(250);

    // Called under _sync. Follows the entry's current processor: subscribes to
    // a new live processor and drops the subscription to one that was replaced.
    private void AttachLive(MapDatasetId datasetId, Entry entry, ILiveDatasetProcessor? live)
    {
        if (ReferenceEquals(entry.LiveProcessor, live))
            return;

        DetachLive(entry);
        if (live is null)
            return;

        // Changed may be raised on any thread, but layers must be installed on
        // the thread the host drives the session from, which is the one adding
        // the dataset.
        var context = SynchronizationContext.Current;
        EventHandler<DatasetChangedEventArgs> handler = (_, _) =>
        {
            if (context is null)
                _ = RefreshLiveAsync(datasetId, live);
            else
                context.Post(_ => _ = RefreshLiveAsync(datasetId, live), null);
        };
        entry.LiveProcessor = live;
        entry.LiveHandler = handler;
        live.Changed += handler;
    }

    // Called under _sync.
    private static void DetachLive(Entry entry)
    {
        if (entry.LiveProcessor is { } live && entry.LiveHandler is { } handler)
            live.Changed -= handler;
        entry.LiveProcessor = null;
        entry.LiveHandler = null;
        entry.LiveRefreshCts?.Cancel();
        entry.LiveRefreshCts = null;
    }

    /// <summary>
    /// Waits for a burst of changes to settle, then re-applies the dataset, so
    /// new available times reach the timeline, and renders it again. A newer
    /// change cancels the wait and starts its own.
    /// </summary>
    private async Task RefreshLiveAsync(MapDatasetId datasetId, ILiveDatasetProcessor live)
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_disposed
                || !_entries.TryGetValue(datasetId, out var entry)
                || !ReferenceEquals(entry.LiveProcessor, live))
            {
                return;
            }
            entry.LiveRefreshCts?.Cancel();
            cts = new CancellationTokenSource();
            entry.LiveRefreshCts = cts;
        }

        try
        {
            await Task.Delay(LiveRefreshDebounceWindow, cts.Token).ConfigureAwait(true);

            MapDataset dataset;
            int? minimumDisplayScale;
            int? maximumDisplayScale;
            MapPresentationState? presentation;
            lock (_sync)
            {
                if (_disposed
                    || !_entries.TryGetValue(datasetId, out var entry)
                    || !ReferenceEquals(entry.LiveRefreshCts, cts))
                {
                    return;
                }
                dataset = entry.Dataset;
                minimumDisplayScale = entry.CatalogueMinimumDisplayScale;
                maximumDisplayScale = entry.MaximumDisplayScale;
                presentation = entry.LastPresentation ?? _currentPresentation;
            }

            // A dataset that was never rendered has nothing on the map to update;
            // its first render will draw the changed data.
            if (presentation is null)
                return;

            SetDataset(dataset, minimumDisplayScale, maximumDisplayScale);
            await RenderAsAsync(datasetId, presentation, MapSessionRenderKind.LiveRefresh, cts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change, or the dataset was removed.
        }
        catch (ObjectDisposedException)
        {
            // The session was disposed while the refresh waited.
        }
        catch (Exception exception)
        {
            // No caller awaits a live refresh, so report the failure the way
            // the other background refreshes do.
            DatasetRenderFailed?.Invoke(
                this,
                new MapSessionDatasetRenderFailedEventArgs(datasetId, MapSessionRenderKind.LiveRefresh, exception));
        }
        finally
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(datasetId, out var entry)
                    && ReferenceEquals(entry.LiveRefreshCts, cts))
                {
                    entry.LiveRefreshCts = null;
                }
            }
            cts.Dispose();
        }
    }
}
