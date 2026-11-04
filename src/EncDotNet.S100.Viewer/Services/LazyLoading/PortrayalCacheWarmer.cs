using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>What the portrayal-cache warmer is doing.</summary>
internal enum PortrayalWarmState
{
    /// <summary>Switched off in the settings.</summary>
    Off,

    /// <summary>No load-as-you-pan cells are registered.</summary>
    Idle,

    /// <summary>Portraying a cell.</summary>
    Working,

    /// <summary>Cells are waiting, but the map is loading or drawing.</summary>
    Paused,

    /// <summary>Cells are waiting, but the portrayal cache has no room left for pre-warming.</summary>
    CacheFull,

    /// <summary>Every registered cell is in the portrayal cache.</summary>
    Complete,
}

/// <summary>A snapshot of the warmer's progress.</summary>
/// <param name="State">What the warmer is doing.</param>
/// <param name="Warm">Registered cells already in the portrayal cache.</param>
/// <param name="Total">Registered cells it considers.</param>
internal readonly record struct PortrayalWarmStatus(PortrayalWarmState State, int Warm, int Total);

/// <summary>Tunables for <see cref="PortrayalCacheWarmer"/>.</summary>
internal sealed record PortrayalWarmOptions
{
    /// <summary>
    /// How long the map must have had nothing visible to load or draw before
    /// the warmer starts a cell. Lazy loads arrive in waves a few hundred
    /// milliseconds apart, so a gap between waves must not count as idle.
    /// </summary>
    public required TimeSpan QuietPeriod { get; init; }

    /// <summary>How often the warmer checks whether the map is busy.</summary>
    public required TimeSpan PollInterval { get; init; }

    /// <summary>How long the warmer sleeps when it is off, done or out of cache room.</summary>
    public required TimeSpan IdleInterval { get; init; }

    /// <summary>The least time between ledger saves while cells are being portrayed.</summary>
    public required TimeSpan SaveInterval { get; init; }

    /// <summary>The viewer's defaults.</summary>
    public static PortrayalWarmOptions Default { get; } = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(750),
        PollInterval = TimeSpan.FromMilliseconds(100),
        IdleInterval = TimeSpan.FromSeconds(2),
        SaveInterval = TimeSpan.FromSeconds(10),
    };
}

/// <summary>
/// Renders the registered, not-yet-loaded cells of load-as-you-pan sets in
/// the background, one at a time, so their Lua portrayal and pattern-fill clip
/// are already in the disk caches when the map reaches them (#1266). Those two
/// are most of a cold cell load; with both cached a load takes tens of
/// milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// One worker, lowest priority: a cell starts only after the map has had
/// nothing visible to load or draw for <see cref="PortrayalWarmOptions.QuietPeriod"/>,
/// and the running cell is cancelled as soon as that changes (or the cell
/// starts loading for real); the Lua run stops at its next feature. Cells
/// nearest the view go first.
/// </para>
/// <para>
/// Progress is resumable: what has been portrayed is kept in a
/// <see cref="PortrayalWarmLedger"/> under the current presentation
/// signature, so a restart or a pause picks up where it left off, and a
/// settings change that alters the portrayal sends every cell back to the
/// queue.
/// </para>
/// <para>
/// The warmer never pushes the cache past its room: when
/// <c>hasCacheRoom</c> says no it stops, so pre-warming never makes the cache
/// evict entries the map is using.
/// </para>
/// <para>
/// The loop runs on the context <see cref="Start"/> is called on (the UI
/// thread in the viewer) because the candidates are view-model state; the
/// portrayal itself runs on the pool.
/// </para>
/// </remarks>
internal sealed class PortrayalCacheWarmer : IDisposable
{
    private readonly Func<IReadOnlyList<DatasetEntry>> _candidates;
    private readonly Func<DatasetEntry, CancellationToken, Task<IReadOnlyList<string>?>> _portrayAsync;
    private readonly Func<bool> _isBusy;
    private readonly Func<MapViewportSnapshot?> _view;
    private readonly Func<bool> _isEnabled;
    private readonly Func<bool> _hasCacheRoom;
    private readonly Func<string, bool> _isCached;
    private readonly Func<string> _signature;
    private readonly PortrayalWarmLedger _ledger;
    private readonly PortrayalWarmOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();

    // Session state, touched only by the loop.
    private readonly HashSet<string> _warmKeys = new(StringComparer.Ordinal);
    private readonly HashSet<DatasetEntry> _warmEntries = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DatasetEntry> _failed = new(ReferenceEqualityComparer.Instance);
    private string? _sessionSignature;
    private long _lastSave;
    private int _portrayedThisSession;

    private Task? _loop;
    private PortrayalWarmStatus _status = new(PortrayalWarmState.Idle, 0, 0);

    /// <summary>Creates the warmer. Nothing runs until <see cref="Start"/>.</summary>
    /// <param name="candidates">The registered, not-loaded cells to consider (the load-as-you-pan cells).</param>
    /// <param name="portrayAsync">
    /// Renders one cell into the disk caches and returns the cache keys it
    /// used, or <see langword="null"/> when the cell does not apply.
    /// </param>
    /// <param name="isBusy">Whether the map still has something visible to load or draw.</param>
    /// <param name="view">The current map view, to put nearby cells first; <see langword="null"/> before the map exists.</param>
    /// <param name="isEnabled">Whether pre-warming is switched on; read every pass, so a change applies at once.</param>
    /// <param name="hasCacheRoom">Whether the disk caches have room for more pre-warmed entries.</param>
    /// <param name="isCached">Whether a cache key from <paramref name="portrayAsync"/> still has an entry.</param>
    /// <param name="signature">Identifies the current presentation and build; cells portrayed under another are redone.</param>
    /// <param name="ledger">What has been portrayed, kept across restarts.</param>
    /// <param name="options">Timings.</param>
    /// <param name="time">Clock for the timings.</param>
    /// <param name="logger">Logs progress.</param>
    public PortrayalCacheWarmer(
        Func<IReadOnlyList<DatasetEntry>> candidates,
        Func<DatasetEntry, CancellationToken, Task<IReadOnlyList<string>?>> portrayAsync,
        Func<bool> isBusy,
        Func<MapViewportSnapshot?> view,
        Func<bool> isEnabled,
        Func<bool> hasCacheRoom,
        Func<string, bool> isCached,
        Func<string> signature,
        PortrayalWarmLedger ledger,
        PortrayalWarmOptions options,
        TimeProvider time,
        ILogger<PortrayalCacheWarmer> logger)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(portrayAsync);
        ArgumentNullException.ThrowIfNull(isBusy);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentNullException.ThrowIfNull(hasCacheRoom);
        ArgumentNullException.ThrowIfNull(isCached);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _candidates = candidates;
        _portrayAsync = portrayAsync;
        _isBusy = isBusy;
        _view = view;
        _isEnabled = isEnabled;
        _hasCacheRoom = hasCacheRoom;
        _isCached = isCached;
        _signature = signature;
        _ledger = ledger;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>The latest progress.</summary>
    public PortrayalWarmStatus Status => _status;

    /// <summary>Raised on the loop's context when <see cref="Status"/> changes.</summary>
    public event EventHandler<PortrayalWarmStatus>? StatusChanged;

    /// <summary>Starts the loop on the current context. Later calls do nothing.</summary>
    public void Start() => _loop ??= RunAsync(_stop.Token);

    /// <summary>Stops the loop and saves the ledger. Does not wait for a running portrayal.</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
            return;
        _stop.Cancel();
        _ledger.Save();
    }

    private async Task RunAsync(CancellationToken stop)
    {
        // Leave the caller (app start-up) before doing anything.
        await Task.Yield();
        long? quietSince = null;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!_isEnabled())
                {
                    quietSince = null;
                    Publish(PortrayalWarmState.Off, _status.Warm, _status.Total);
                    await DelayAsync(_options.IdleInterval, stop);
                    continue;
                }

                if (_isBusy())
                {
                    quietSince = null;
                    if (_status.State is PortrayalWarmState.Working or PortrayalWarmState.Paused)
                        Publish(PortrayalWarmState.Paused, _status.Warm, _status.Total);
                    await DelayAsync(_options.PollInterval, stop);
                    continue;
                }

                quietSince ??= _time.GetTimestamp();
                if (_time.GetElapsedTime(quietSince.Value) < _options.QuietPeriod)
                {
                    await DelayAsync(_options.PollInterval, stop);
                    continue;
                }

                var (pending, warm, total) = Survey();
                if (pending.Count == 0)
                {
                    Publish(total == 0 ? PortrayalWarmState.Idle : PortrayalWarmState.Complete, warm, total);
                    SaveLedger(force: true);
                    await DelayAsync(_options.IdleInterval, stop);
                    continue;
                }

                if (!_hasCacheRoom())
                {
                    Publish(PortrayalWarmState.CacheFull, warm, total);
                    SaveLedger(force: true);
                    await DelayAsync(_options.IdleInterval, stop);
                    continue;
                }

                Publish(PortrayalWarmState.Working, warm, total);
                await PortrayOneAsync(Nearest(pending), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Portrayal pre-warm pass failed.");
                await DelayAsync(_options.IdleInterval, stop).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Splits the candidates into those still to portray and those already
    /// warm. A signature change (mariner settings, display mode, build) forgets
    /// the session's warm set, so every cell is checked again.
    /// </summary>
    private (List<DatasetEntry> Pending, int Warm, int Total) Survey()
    {
        var signature = _signature();
        if (!string.Equals(signature, _sessionSignature, StringComparison.Ordinal))
        {
            _sessionSignature = signature;
            _warmKeys.Clear();
            _warmEntries.Clear();
            _failed.Clear();
        }

        var pending = new List<DatasetEntry>();
        var warm = 0;
        var total = 0;
        foreach (var entry in _candidates())
        {
            if (entry.IsLoaded || entry.IsLoading || _failed.Contains(entry))
                continue;
            total++;
            if (IsWarm(entry, signature))
                warm++;
            else
                pending.Add(entry);
        }

        return (pending, warm, total);
    }

    private bool IsWarm(DatasetEntry entry, string signature)
    {
        if (_warmEntries.Contains(entry))
            return true;
        if (PortrayalWarmLedger.CellKey(entry) is not { } cellKey)
            return false;
        if (_warmKeys.Contains(cellKey))
            return true;
        if (!_ledger.IsWarm(cellKey, signature, _isCached))
            return false;
        _warmKeys.Add(cellKey);
        return true;
    }

    /// <summary>The pending cell whose footprint is nearest the view's centre; cells with no footprint go last.</summary>
    private DatasetEntry Nearest(List<DatasetEntry> pending)
    {
        if (_view() is not { } view)
            return pending[0];

        var lat = (view.MinLatitude + view.MaxLatitude) / 2.0;
        var lon = (view.MinLongitude + view.MaxLongitude) / 2.0;
        var cosLat = Math.Cos(lat * Math.PI / 180.0);
        DatasetEntry best = pending[0];
        var bestDistance = double.MaxValue;
        foreach (var entry in pending)
        {
            if (entry.GeographicBounds is not { } b)
                continue;
            // Distance from the view centre to the nearest point of the footprint.
            var dLat = Math.Max(0, Math.Max(b.SouthBoundLatitude - lat, lat - b.NorthBoundLatitude));
            var dLon = Math.Max(0, Math.Max(b.WestBoundLongitude - lon, lon - b.EastBoundLongitude)) * cosLat;
            var distance = (dLat * dLat) + (dLon * dLon);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry;
            }
        }

        return best;
    }

    private async Task PortrayOneAsync(DatasetEntry entry, CancellationToken stop)
    {
        var signature = _sessionSignature!;
        var cellKey = PortrayalWarmLedger.CellKey(entry);
        using var cell = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var start = _time.GetTimestamp();
        var portrayal = _portrayAsync(entry, cell.Token);

        // Give way at once when the map needs the machine, or when the cell
        // starts loading for real (that load portrays it anyway).
        while (!portrayal.IsCompleted)
        {
            await Task.WhenAny(portrayal, Task.Delay(_options.PollInterval, _time, stop)).ConfigureAwait(true);
            if (!portrayal.IsCompleted && !cell.IsCancellationRequested
                && (_isBusy() || !_isEnabled() || entry.IsLoading || entry.IsLoaded))
            {
                cell.Cancel();
            }

            stop.ThrowIfCancellationRequested();
        }

        IReadOnlyList<string>? keys;
        try
        {
            keys = await portrayal.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cell.IsCancellationRequested)
        {
            stop.ThrowIfCancellationRequested();
            _logger.LogDebug("Portrayal pre-warm of {Cell} gave way to the map after {Elapsed:N0} ms.",
                entry.DisplayName, _time.GetElapsedTime(start).TotalMilliseconds);
            Publish(PortrayalWarmState.Paused, _status.Warm, _status.Total);
            return;
        }
        catch (Exception ex)
        {
            // A cell that cannot be opened or portrayed is skipped for the rest
            // of the session; its interactive load reports the problem.
            _failed.Add(entry);
            _logger.LogDebug(ex, "Portrayal pre-warm skipped {Cell}.", entry.DisplayName);
            return;
        }

        if (keys is null)
        {
            _failed.Add(entry);
            return;
        }

        _warmEntries.Add(entry);
        if (cellKey is not null)
        {
            _warmKeys.Add(cellKey);
            _ledger.MarkWarm(cellKey, signature, keys);
        }

        _portrayedThisSession++;
        _logger.LogDebug("Portrayal pre-warm portrayed {Cell} in {Elapsed:N0} ms ({Count} this session).",
            entry.DisplayName, _time.GetElapsedTime(start).TotalMilliseconds, _portrayedThisSession);
        Publish(PortrayalWarmState.Working, _status.Warm + 1, _status.Total);
        SaveLedger(force: false);
    }

    private void SaveLedger(bool force)
    {
        var now = _time.GetTimestamp();
        if (!force && _time.GetElapsedTime(_lastSave, now) < _options.SaveInterval)
            return;
        _lastSave = now;
        _ledger.Save();
    }

    private void Publish(PortrayalWarmState state, int warm, int total)
    {
        var status = new PortrayalWarmStatus(state, warm, total);
        if (status == _status)
            return;
        var previous = _status;
        _status = status;
        if (state != previous.State && state is PortrayalWarmState.Complete or PortrayalWarmState.CacheFull)
        {
            _logger.LogInformation("Portrayal pre-warm {State}: {Warm} of {Total} cells in the portrayal cache.",
                state, warm, total);
        }

        StatusChanged?.Invoke(this, status);
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken stop) => Task.Delay(delay, _time, stop);
}
