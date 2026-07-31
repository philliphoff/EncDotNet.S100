namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Sticky record of unclean shutdowns detected from previous runs.
/// </summary>
/// <remarks>
/// Unlike <see cref="ILastErrorTracker"/>, which keeps only the most recent
/// runtime error in a single slot that later (non-fatal) errors overwrite,
/// this history is captured once at startup and never evicted. A crash is a
/// far stronger signal than an exception the app recovered from, so the
/// feedback bundle must always carry every detected crash regardless of any
/// errors that occur afterwards. Implementations are thread-safe.
/// </remarks>
internal interface ICrashHistory
{
    /// <summary>
    /// Previous sessions that terminated without a clean shutdown, oldest
    /// first. Empty when the previous run(s) exited cleanly.
    /// </summary>
    IReadOnlyList<PreviousSession> Crashes { get; }

    /// <summary>
    /// Best-effort tail of the shared crash log captured when the crashes
    /// were detected, or <see langword="null"/> when none was available.
    /// </summary>
    string? CrashLogTail { get; }

    /// <summary>Whether any unclean shutdown was detected.</summary>
    bool HasCrashes { get; }

    /// <summary>
    /// Records the crashes detected at startup. Intended to be called once;
    /// a later call replaces the previous capture.
    /// </summary>
    /// <param name="crashes">Detected crashed sessions (oldest first).</param>
    /// <param name="crashLogTail">Optional tail of the crash log.</param>
    void Capture(IReadOnlyList<PreviousSession> crashes, string? crashLogTail);
}

/// <summary>
/// Default <see cref="ICrashHistory"/>: holds the startup crash capture in
/// lock-guarded fields. Registered as a singleton and populated by
/// <c>App.DetectPreviousUncleanShutdown</c>.
/// </summary>
internal sealed class CrashHistory : ICrashHistory
{
    private readonly object _gate = new();
    private IReadOnlyList<PreviousSession> _crashes = Array.Empty<PreviousSession>();
    private string? _crashLogTail;

    /// <inheritdoc />
    public IReadOnlyList<PreviousSession> Crashes
    {
        get { lock (_gate) return _crashes; }
    }

    /// <inheritdoc />
    public string? CrashLogTail
    {
        get { lock (_gate) return _crashLogTail; }
    }

    /// <inheritdoc />
    public bool HasCrashes
    {
        get { lock (_gate) return _crashes.Count > 0; }
    }

    /// <inheritdoc />
    public void Capture(IReadOnlyList<PreviousSession> crashes, string? crashLogTail)
    {
        ArgumentNullException.ThrowIfNull(crashes);
        lock (_gate)
        {
            _crashes = crashes;
            _crashLogTail = crashLogTail;
        }
    }
}
