namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Counts Library work that outlives the downloader's progress (issue #790):
/// a <c>library_action</c> download runs on through re-indexing and opening
/// its datasets after the batch's progress clears, and a load opens datasets
/// after its call starts. Each piece of work holds a scope from start to
/// finish, so "idle" can wait for all of it. Thread-safe and UI-free.
/// </summary>
internal sealed class LibraryActivityTracker
{
    private readonly object _gate = new();
    private int _active;
    private int _datasets;

    /// <summary>Pieces of work still running.</summary>
    public int Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    /// <summary>Datasets the running work will open (still downloading or opening).</summary>
    public int PendingDatasets
    {
        get
        {
            lock (_gate)
                return _datasets;
        }
    }

    /// <summary>
    /// Marks a piece of work as running until the returned scope is disposed.
    /// </summary>
    /// <param name="datasets">How many datasets it will open (0 for work that opens none).</param>
    public IDisposable Begin(int datasets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(datasets);
        lock (_gate)
        {
            _active++;
            _datasets += datasets;
        }
        return new Scope(this, datasets);
    }

    private void End(int datasets)
    {
        lock (_gate)
        {
            _active--;
            _datasets -= datasets;
        }
    }

    private sealed class Scope(LibraryActivityTracker owner, int datasets) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.End(datasets);
        }
    }
}
