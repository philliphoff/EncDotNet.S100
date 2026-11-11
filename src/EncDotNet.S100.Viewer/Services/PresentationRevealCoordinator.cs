using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Drives the map session's lazy "reveal" re-portrayal: when the viewport
/// changes, cells that a prior viewport-gated presentation change deferred as
/// off-view are re-portrayed once they scroll back into view.
/// </summary>
/// <remarks>
/// <para>
/// The application service provider owns this singleton for the Viewer
/// lifetime. It owns only the viewport subscription; the map session and its
/// layers remain owned by the injected <see cref="DatasetLoaderService"/>.
/// </para>
/// <para>
/// Viewport changes are debounced (trailing edge) so a single pan/zoom gesture
/// coalesces into one reveal pass, and the pass is marshalled to the UI thread
/// because it composes the map layer stack.
/// </para>
/// </remarks>
internal sealed class PresentationRevealCoordinator : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    private readonly IMapViewportNotifier _notifier;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly DatasetLoaderService _loader;
    private readonly object _sync = new();
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public PresentationRevealCoordinator(
        IMapViewportNotifier notifier,
        IUiDispatcher uiDispatcher,
        DatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(loader);

        _notifier = notifier;
        _uiDispatcher = uiDispatcher;
        _loader = loader;
        _notifier.ViewportChanged += OnViewportChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _notifier.ViewportChanged -= OnViewportChanged;
        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_disposed)
                return;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            cts = _debounceCts = new CancellationTokenSource();
        }

        _ = RevealAfterDebounceAsync(snapshot, cts.Token);
    }

    private async Task RevealAfterDebounceAsync(
        MapViewportSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Debounce, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // The reveal pass composes the map layer stack, so run it on the UI
            // thread regardless of which thread published the viewport change.
            if (_uiDispatcher.IsOnUiThread)
            {
                await _loader.RefreshRevealedAsync(snapshot).ConfigureAwait(true);
            }
            else
            {
                _uiDispatcher.Post(() => _ = RevealOnUiThreadAsync(snapshot));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reveal refresh failed:\n{exception}");
        }
    }

    private async Task RevealOnUiThreadAsync(MapViewportSnapshot snapshot)
    {
        try
        {
            await _loader.RefreshRevealedAsync(snapshot).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reveal refresh failed:\n{exception}");
        }
    }
}
