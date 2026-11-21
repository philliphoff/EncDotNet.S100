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

    /// <summary>Portraying one or more cells.</summary>
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
/// the background, several at a time, so their Lua portrayal and pattern-fill clip
/// are already in the disk caches when the map reaches them (#1266). Those two
/// are most of a cold cell load; with both cached a load takes tens of
/// milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// Lowest priority: cells start only after the map has had nothing visible
/// to load or draw for <see cref="PortrayalWarmOptions.QuietPeriod"/>, and
/// every running cell is cancelled within a
/// <see cref="PortrayalWarmOptions.PollInterval"/> of that changing (or of
/// the cell starting to load for real); the Lua run stops at its next
/// feature. Cells nearest the view go first.
/// </para>
/// <para>
/// While the map is idle, several cells are portrayed at once, each as a
/// background portrayal job on the work scheduler (map rendering slice 5), so
/// map work always runs first. The warmer keeps as many cells going as the
/// scheduler admits allocation-heavy background jobs; the scheduler's GC
/// pause governor (slice 3's limit, now shared by every such job) raises that
/// while GC pauses take little of the wall time and halves it when they climb.
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
    private readonly Func<int> _concurrencyLimit;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();

    // Session state, touched only by the loop.
    private readonly HashSet<string> _warmKeys = new(StringComparer.Ordinal);
    private readonly HashSet<DatasetEntry> _warmEntries = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DatasetEntry> _failed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DatasetEntry, Running> _running = new(ReferenceEqualityComparer.Instance);
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
    /// <param name="concurrencyLimit">How many cells may be portrayed at once now; read every pass.</param>
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
        Func<int> concurrencyLimit,
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
        ArgumentNullException.ThrowIfNull(concurrencyLimit);
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
        _concurrencyLimit = concurrencyLimit;
        _time = time;
        _logger = logger;
    }

    /// <summary>The latest progress.</summary>
    public PortrayalWarmStatus Status => _status;

    /// <summary>Raised on the loop's context when <see cref="Status"/> changes.</summary>
    public event EventHandler<PortrayalWarmStatus>? StatusChanged;

    /// <summary>Starts the loop on the current context. Later calls do nothing.</summary>
    public void Start() => _loop ??= RunAsync(_stop.Token);

    /// <summary>How many cells may be portrayed at once now.</summary>
    public int ConcurrencyLimit => Math.Max(1, _concurrencyLimit());

    /// <summary>Stops the loop and saves the ledger. Does not wait for running portrayals.</summary>
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
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    Harvest(stop);
                    var enabled = _isEnabled();
                    var busy = enabled && _isBusy();
                    GiveWay(enabled, busy);

                    if (!enabled)
                    {
                        quietSince = null;
                        if (_running.Count == 0)
                            Publish(PortrayalWarmState.Off, _status.Warm, _status.Total);
                        await WaitAsync(_running.Count == 0 ? _options.IdleInterval : _options.PollInterval, stop);
                        continue;
                    }

                    if (busy)
                    {
                        quietSince = null;
                        if (_status.State is PortrayalWarmState.Working or PortrayalWarmState.Paused)
                            Publish(PortrayalWarmState.Paused, _status.Warm, _status.Total);
                        await WaitAsync(_options.PollInterval, stop);
                        continue;
                    }

                    quietSince ??= _time.GetTimestamp();
                    if (_time.GetElapsedTime(quietSince.Value) < _options.QuietPeriod)
                    {
                        await WaitAsync(_options.PollInterval, stop);
                        continue;
                    }

                    // Survey only when a slot is free: it reads every candidate.
                    if (_running.Count < ConcurrencyLimit && !await FillAsync(stop))
                        continue;

                    await WaitAsync(_options.PollInterval, stop);
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
        finally
        {
            foreach (var run in _running.Values)
                run.Cancel.Cancel();
        }
    }

    /// <summary>
    /// Starts pending cells, nearest the view first, until every slot is in
    /// use; or, with nothing running, reports why nothing can start and waits.
    /// </summary>
    /// <returns><see langword="false"/> when it already waited.</returns>
    private async Task<bool> FillAsync(CancellationToken stop)
    {
        var (pending, warm, total) = Survey();
        if (pending.Count == 0 && _running.Count == 0)
        {
            Publish(total == 0 ? PortrayalWarmState.Idle : PortrayalWarmState.Complete, warm, total);
            SaveLedger(force: true);
            await DelayAsync(_options.IdleInterval, stop);
            return false;
        }

        if (!_hasCacheRoom())
        {
            if (_running.Count > 0)
                return true;
            Publish(PortrayalWarmState.CacheFull, warm, total);
            SaveLedger(force: true);
            await DelayAsync(_options.IdleInterval, stop);
            return false;
        }

        while (_running.Count < ConcurrencyLimit && pending.Count > 0)
            Start(TakeNearest(pending), stop);

        Publish(PortrayalWarmState.Working, warm, total);
        return true;
    }

    /// <summary>
    /// Cancels running cells when the map needs the machine, the warmer is
    /// switched off, or the cell starts loading for real (that load portrays
    /// it anyway). The Lua run stops at its next feature.
    /// </summary>
    private void GiveWay(bool enabled, bool busy)
    {
        foreach (var (entry, run) in _running)
        {
            if (!run.Cancel.IsCancellationRequested
                && (!enabled || busy || entry.IsLoading || entry.IsLoaded))
            {
                run.Cancel.Cancel();
            }
        }
    }

    /// <summary>Waits for a poll interval or until a running cell finishes, whichever is first.</summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken stop)
    {
        if (_running.Count == 0)
        {
            await DelayAsync(delay, stop);
            return;
        }

        var waits = new List<Task>(_running.Count + 1) { DelayAsync(delay, stop) };
        foreach (var run in _running.Values)
            waits.Add(run.Portrayal);
        await Task.WhenAny(waits).ConfigureAwait(true);
        stop.ThrowIfCancellationRequested();
    }

    private void Start(DatasetEntry entry, CancellationToken stop)
    {
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task<IReadOnlyList<string>?> task;
        try
        {
            task = _portrayAsync(entry, cancel.Token);
        }
        catch (Exception ex)
        {
            task = Task.FromException<IReadOnlyList<string>?>(ex);
        }

        _running.Add(entry, new Running(
            task, cancel, PortrayalWarmLedger.CellKey(entry), _sessionSignature!, _time.GetTimestamp()));
    }

    /// <summary>Records the cells that finished since the last pass.</summary>
    private void Harvest(CancellationToken stop)
    {
        List<DatasetEntry>? done = null;
        foreach (var (entry, run) in _running)
        {
            if (run.Portrayal.IsCompleted)
                (done ??= []).Add(entry);
        }

        if (done is null)
            return;

        foreach (var entry in done)
        {
            var run = _running[entry];
            _running.Remove(entry);
            if (!stop.IsCancellationRequested)
                Record(entry, run);
            run.Cancel.Dispose();
        }
    }

    private void Record(DatasetEntry entry, Running run)
    {
        IReadOnlyList<string>? keys;
        try
        {
            keys = run.Portrayal.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (run.Cancel.IsCancellationRequested)
        {
            _logger.LogDebug("Portrayal pre-warm of {Cell} gave way to the map after {Elapsed:N0} ms.",
                entry.DisplayName, _time.GetElapsedTime(run.Start).TotalMilliseconds);
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

        if (run.CellKey is not null)
            _ledger.MarkWarm(run.CellKey, run.Signature, keys);

        // A cell started under a presentation that has since changed is
        // recorded under its own signature, but isn't warm for this one.
        if (!string.Equals(run.Signature, _sessionSignature, StringComparison.Ordinal))
            return;

        _warmEntries.Add(entry);
        if (run.CellKey is not null)
            _warmKeys.Add(run.CellKey);

        _portrayedThisSession++;
        _logger.LogDebug("Portrayal pre-warm portrayed {Cell} in {Elapsed:N0} ms ({Count} this session, {Running} running).",
            entry.DisplayName, _time.GetElapsedTime(run.Start).TotalMilliseconds, _portrayedThisSession, _running.Count);
        Publish(_status.State, _status.Warm + 1, _status.Total);
        SaveLedger(force: false);
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
            else if (!_running.ContainsKey(entry))
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

    /// <summary>Removes and returns the pending cell whose footprint is nearest the view's centre; cells with no footprint go last.</summary>
    private DatasetEntry TakeNearest(List<DatasetEntry> pending)
    {
        var index = NearestIndex(pending);
        var entry = pending[index];
        pending.RemoveAt(index);
        return entry;
    }

    private int NearestIndex(List<DatasetEntry> pending)
    {
        if (_view() is not { } view)
            return 0;

        var lat = (view.MinLatitude + view.MaxLatitude) / 2.0;
        var lon = (view.MinLongitude + view.MaxLongitude) / 2.0;
        var cosLat = Math.Cos(lat * Math.PI / 180.0);
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i].GeographicBounds is not { } b)
                continue;
            // Distance from the view centre to the nearest point of the footprint.
            var dLat = Math.Max(0, Math.Max(b.SouthBoundLatitude - lat, lat - b.NorthBoundLatitude));
            var dLon = Math.Max(0, Math.Max(b.WestBoundLongitude - lon, lon - b.EastBoundLongitude)) * cosLat;
            var distance = (dLat * dLat) + (dLon * dLon);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
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

    /// <summary>A cell being portrayed.</summary>
    /// <param name="Portrayal">The portrayal.</param>
    /// <param name="Cancel">Cancels it; linked to the warmer's stop.</param>
    /// <param name="CellKey">The cell's ledger key, or null.</param>
    /// <param name="Signature">The presentation signature it was started under.</param>
    /// <param name="Start">When it started.</param>
    private sealed record Running(
        Task<IReadOnlyList<string>?> Portrayal,
        CancellationTokenSource Cancel,
        string? CellKey,
        string Signature,
        long Start);
}
