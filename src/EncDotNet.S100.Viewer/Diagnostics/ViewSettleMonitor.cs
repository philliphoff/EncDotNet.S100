using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>One reading of the two halves of the settled signal.</summary>
/// <param name="Timestamp">When it was taken (<see cref="Stopwatch"/> timestamp).</param>
/// <param name="Loads">
/// The lazy-load coordinator's work for the view, or <see langword="null"/>
/// when no coordinator is wired (nothing loads as you pan).
/// </param>
/// <param name="Tiles">What the tiled layers showed in their latest live frames.</param>
internal readonly record struct SettleSample(long Timestamp, LazyLoadVisibleWork? Loads, TileSettleState Tiles);

/// <summary>Outcome of <see cref="ViewSettleMonitor.WaitForSettledAsync"/>.</summary>
/// <param name="Settled">The view settled and stayed settled for the confirmation period.</param>
/// <param name="TimedOut">The timeout elapsed first.</param>
/// <param name="SettleMs">
/// Milliseconds from the trigger (the last <c>set_viewport</c>, or the call
/// when there was none since tracking started) to the frame that completed the
/// settled picture, or <see langword="null"/> on a timeout.
/// </param>
/// <param name="WaitedMs">Milliseconds the call waited.</param>
/// <param name="TriggerAgeMs">Milliseconds from the trigger to the call.</param>
/// <param name="HadTrigger">Whether a <c>set_viewport</c> trigger was recorded.</param>
/// <param name="Last">The last sample taken.</param>
internal readonly record struct SettleOutcome(
    bool Settled,
    bool TimedOut,
    double? SettleMs,
    double WaitedMs,
    double TriggerAgeMs,
    bool HadTrigger,
    SettleSample Last);

/// <summary>
/// The viewer's settled signal for map rendering slice 1: the view is
/// settled when every cell the lazy-load coordinator classifies as visible
/// for the current viewport is loaded, and every visible tile slot of every
/// visible tiled layer shows its own target tile (no fallback band,
/// predecessor, held tile or sibling scale class) with no visible tile work
/// pending.
/// </summary>
/// <remarks>
/// <para>
/// The monitor is inert until <see cref="MarkTrigger"/> or
/// <see cref="WaitForSettledAsync"/> first runs; that turns on the tile
/// renderer's per-frame settle stamps (<see cref="EnableTracking"/>). The
/// viewer only calls it from MCP tools, so a viewer without a harness
/// attached does no extra work per frame.
/// </para>
/// <para>
/// Sampling runs on the caller's thread (an MCP request on the thread pool):
/// it takes the coordinator's lock and each tiled layer's tile lock briefly,
/// and never posts to the UI thread.
/// </para>
/// </remarks>
internal sealed class ViewSettleMonitor
{
    private readonly object _gate = new();
    private readonly Action<bool> _setTileTracking;
    private long _triggerTimestamp;
    private bool _tracking;

    /// <summary>Creates a monitor.</summary>
    /// <param name="setTileTracking">
    /// Turns the tile renderer's settle stamps on or off; the viewer passes
    /// <see cref="S100VectorTileRenderer.SettleTrackingEnabled"/>.
    /// </param>
    public ViewSettleMonitor(Action<bool> setTileTracking)
    {
        ArgumentNullException.ThrowIfNull(setTileTracking);
        _setTileTracking = setTileTracking;
    }

    /// <summary>Reads the lazy-load coordinator's visible work, or null when none is wired.</summary>
    public Func<LazyLoadVisibleWork?>? LoadProbe { get; set; }

    /// <summary>Reads the tiled layers' settle state; set by the window once the map exists.</summary>
    public Func<TileSettleState>? TileProbe { get; set; }

    /// <summary>Requests a map repaint; set by the window, so the first stamps arrive without waiting for a change.</summary>
    public Action? RequestRedraw { get; set; }

    /// <summary>Whether tile settle stamps are on.</summary>
    public bool IsTracking
    {
        get { lock (_gate) return _tracking; }
    }

    /// <summary>Turns on the tile renderer's settle stamps and requests a repaint the first time.</summary>
    public void EnableTracking()
    {
        bool first;
        lock (_gate)
        {
            first = !_tracking;
            _tracking = true;
        }

        if (first)
        {
            _setTileTracking(true);
            try
            {
                RequestRedraw?.Invoke();
            }
            catch
            {
                // A repaint request is best effort; the next change paints anyway.
            }
        }
    }

    /// <summary>
    /// Records that a viewport change was just applied (the
    /// <c>set_viewport</c> tool calls it), so the next wait measures from it.
    /// </summary>
    public void MarkTrigger()
    {
        EnableTracking();
        lock (_gate)
        {
            _triggerTimestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Takes one sample. Safe to call from any thread.</summary>
    /// <returns>The sample.</returns>
    public SettleSample Sample()
    {
        LazyLoadVisibleWork? loads = null;
        try
        {
            loads = LoadProbe?.Invoke();
        }
        catch
        {
            // A probe racing a closing exchange set reads as "nothing to load".
        }

        TileSettleState tiles = default;
        try
        {
            if (TileProbe is { } probe)
                tiles = probe();
        }
        catch
        {
            // The layer collection changed mid-read: try again next sample.
            tiles = new TileSettleState(0, 1, 0, 0, 0, 0);
        }

        return new SettleSample(Stopwatch.GetTimestamp(), loads, tiles);
    }

    /// <summary>
    /// The pure settled decision. Loads: no viewport change waiting for
    /// evaluation and no cell in view queued or loading. Tiles: the renderer
    /// reports every considered layer settled, in a frame drawn after both the
    /// trigger and the last finished load (so a just-loaded cell's layer has
    /// painted).
    /// </summary>
    /// <param name="sample">The sample.</param>
    /// <param name="triggerTimestamp">The trigger, or 0.</param>
    /// <returns>Whether the view is settled.</returns>
    internal static bool IsSettled(SettleSample sample, long triggerTimestamp)
    {
        if (sample.Loads is { } loads && !loads.VisibleLoaded)
            return false;

        if (!sample.Tiles.IsSettled)
            return false;

        var frameAfter = Math.Max(triggerTimestamp, sample.Loads?.LastLoadFinishedTimestamp ?? 0);
        return sample.Tiles.LatestFrameTimestamp > frameAfter;
    }

    /// <summary>
    /// Waits until the view is settled and has stayed settled for
    /// <paramref name="confirm"/>, or <paramref name="timeout"/> elapses.
    /// The settle time is measured to the frame that completed the settled
    /// picture, not to the end of the confirmation period.
    /// </summary>
    /// <param name="confirm">How long the settled state must hold, which rides out a load finishing and its layer arriving a frame later.</param>
    /// <param name="timeout">The longest wait.</param>
    /// <param name="poll">How often to sample.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The outcome.</returns>
    public async Task<SettleOutcome> WaitForSettledAsync(
        TimeSpan confirm,
        TimeSpan timeout,
        TimeSpan poll,
        CancellationToken cancellationToken = default)
    {
        EnableTracking();
        var callStart = Stopwatch.GetTimestamp();
        long trigger;
        lock (_gate)
        {
            trigger = _triggerTimestamp;
        }

        var hadTrigger = trigger != 0;
        var reference = hadTrigger ? trigger : callStart;
        if (poll <= TimeSpan.Zero)
            poll = TimeSpan.FromMilliseconds(1);

        long? runStart = null;
        long settledAt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = Sample();
            if (IsSettled(sample, reference))
            {
                if (runStart is null)
                {
                    runStart = sample.Timestamp;
                    settledAt = Math.Min(sample.Timestamp, Math.Max(reference, sample.Tiles.LatestFrameTimestamp));
                }

                if (Stopwatch.GetElapsedTime(runStart.Value, sample.Timestamp) >= confirm)
                {
                    return Outcome(settled: true, Stopwatch.GetElapsedTime(reference, settledAt).TotalMilliseconds, sample);
                }
            }
            else
            {
                runStart = null;
            }

            if (Stopwatch.GetElapsedTime(callStart, sample.Timestamp) >= timeout)
            {
                return Outcome(settled: false, null, sample);
            }

            await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
        }

        SettleOutcome Outcome(bool settled, double? settleMs, SettleSample last) => new(
            Settled: settled,
            TimedOut: !settled,
            SettleMs: settleMs,
            WaitedMs: Stopwatch.GetElapsedTime(callStart).TotalMilliseconds,
            TriggerAgeMs: Stopwatch.GetElapsedTime(reference, callStart).TotalMilliseconds,
            HadTrigger: hadTrigger,
            Last: last);
    }
}
