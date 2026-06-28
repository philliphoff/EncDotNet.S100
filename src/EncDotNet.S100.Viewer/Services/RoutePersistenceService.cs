using System;
using System.Threading;
using EncDotNet.S100.Viewer.Routing.Persistence;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Bridges the in-memory <see cref="RoutesService"/> to the on-disk
/// <c>routes.json</c> store: loads saved routes once at startup and writes
/// them back (debounced) whenever the route set changes, so a user's (or an
/// agent's) routes survive a viewer restart.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator is deliberately thin — all serialization lives in
/// <see cref="RouteStore"/>. It subscribes to
/// <see cref="RouteCollection.Changed"/> (not the broader
/// <see cref="RoutesService.Changed"/>) so transient editor-selection
/// changes, which are not persisted, do not trigger redundant writes.
/// </para>
/// <para>
/// Saves are coalesced behind a short debounce timer because a single user
/// gesture (e.g. building a multi-waypoint route) raises many change events
/// in quick succession; <see cref="Flush"/> forces any pending write out
/// synchronously and is called on shutdown. When the run is read-only
/// (<c>--ephemeral</c>) the coordinator still loads the user's existing
/// routes but never writes, leaving the persisted file untouched.
/// </para>
/// </remarks>
internal sealed class RoutePersistenceService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(750);

    private readonly RoutesService _routes;
    private readonly string _path;
    private readonly bool _readOnly;
    private readonly object _gate = new();

    private Timer? _debounce;
    private bool _initialized;
    private bool _loading;
    private bool _disposed;

    /// <summary>
    /// Creates the coordinator for the supplied route service, resolving the
    /// store path and read-only policy from the run's data paths and
    /// settings. Construction does no I/O; call <see cref="Initialize"/> once
    /// the rest of startup is ready.
    /// </summary>
    /// <param name="routes">The application route service to persist.</param>
    /// <param name="dataPaths">Provides the <c>routes.json</c> location.</param>
    /// <param name="settings">Supplies the read-only (<c>--ephemeral</c>) flag.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public RoutePersistenceService(RoutesService routes, ViewerDataPaths dataPaths, ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(settings);

        _routes = routes;
        _path = dataPaths.RoutesFilePath;
        _readOnly = settings.IsReadOnly;
    }

    /// <summary>
    /// Loads persisted routes into the route service and begins tracking
    /// subsequent changes for save. Idempotent: a second call is a no-op.
    /// A load failure is swallowed (the service starts with no routes) so a
    /// corrupt file never blocks startup.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        _loading = true;
        try
        {
            RouteStore.Load(_routes.Routes, _path);
        }
        catch
        {
            // Best-effort: RouteStore.Load already absorbs the expected I/O
            // and JSON faults; this guards against anything unforeseen so the
            // viewer still launches with an empty route set.
        }
        finally
        {
            _loading = false;
        }

        // Persist structural route changes only; selection changes flow
        // through RoutesService.Changed, which we intentionally ignore here.
        _routes.Routes.Changed += OnRoutesChanged;
    }

    private void OnRoutesChanged(object? sender, EventArgs e)
    {
        if (_loading || _readOnly || _disposed)
            return;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _debounce ??= new Timer(_ => SaveNow());
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Writes the current route set to disk immediately, cancelling any
    /// pending debounced save. A no-op for read-only runs. Exposed for tests
    /// and used by <see cref="Flush"/>.
    /// </summary>
    public void SaveNow()
    {
        lock (_gate)
        {
            _debounce?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        if (_readOnly)
            return;

        try
        {
            RouteStore.Save(_routes.Routes, _path);
        }
        catch
        {
            // Best-effort persistence: a failed save must not crash the app
            // or block shutdown. The next change reschedules another attempt.
        }
    }

    /// <summary>
    /// Forces any pending debounced save to complete synchronously. Call on
    /// shutdown so the final edit is not lost to the debounce window.
    /// </summary>
    public void Flush() => SaveNow();

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }

        _routes.Routes.Changed -= OnRoutesChanged;
    }
}
