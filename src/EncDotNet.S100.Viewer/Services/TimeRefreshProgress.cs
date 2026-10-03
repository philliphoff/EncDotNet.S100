namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// How far the map has got with drawing a new view time (#709, handoff B3):
/// the time being drawn, the datasets still drawing it, and the last view
/// time fully drawn. Driven by the dataset loader (each time refresh) and
/// the map session's per-dataset render events, which arrive on worker
/// threads; <see cref="Changed"/> is raised on the thread that changed it.
/// </summary>
internal sealed class TimeRefreshProgress
{
    private readonly object _sync = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private long _generation;
    private bool _isDrawing;
    private DateTime? _target;
    private DateTime? _drawn;
    private int _ready;
    private int _total;

    /// <summary>Raised whenever anything here changes.</summary>
    public event Action? Changed;

    /// <summary>True while a view time is being drawn and some layer is still drawing it.</summary>
    public bool IsDrawing
    {
        get { lock (_sync) return _isDrawing && _pending.Count > 0; }
    }

    /// <summary>The view time being drawn, or null.</summary>
    public DateTime? Target
    {
        get { lock (_sync) return _isDrawing ? _target : null; }
    }

    /// <summary>The last view time fully drawn, or null before the first.</summary>
    public DateTime? Drawn
    {
        get { lock (_sync) return _drawn; }
    }

    /// <summary>Layers that have finished drawing <see cref="Target"/>.</summary>
    public int Ready
    {
        get { lock (_sync) return _ready; }
    }

    /// <summary>Layers that have started drawing <see cref="Target"/>.</summary>
    public int Total
    {
        get { lock (_sync) return _total; }
    }

    /// <summary>True while the dataset with <paramref name="datasetId"/> is drawing the new view time.</summary>
    public bool IsDatasetDrawing(string datasetId)
    {
        lock (_sync) return _isDrawing && _pending.Contains(datasetId);
    }

    /// <summary>A time refresh for <paramref name="target"/> begins; any earlier one is superseded.</summary>
    /// <returns>A token to pass to <see cref="End"/>.</returns>
    public long Begin(DateTime target)
    {
        long generation;
        lock (_sync)
        {
            generation = ++_generation;
            _isDrawing = true;
            _target = target;
            _pending.Clear();
            _ready = 0;
            _total = 0;
        }
        Changed?.Invoke();
        return generation;
    }

    /// <summary>The refresh with <paramref name="generation"/> returned; when it is the latest, the map now shows its time.</summary>
    public void End(long generation)
    {
        lock (_sync)
        {
            if (generation != _generation)
                return;
            _isDrawing = false;
            _drawn = _target;
            _pending.Clear();
        }
        Changed?.Invoke();
    }

    /// <summary>A dataset started drawing the new view time.</summary>
    public void Started(string datasetId)
    {
        lock (_sync)
        {
            if (!_isDrawing || !_pending.Add(datasetId))
                return;
            _total++;
        }
        Changed?.Invoke();
    }

    /// <summary>A dataset finished (or failed) drawing the new view time.</summary>
    public void Finished(string datasetId)
    {
        lock (_sync)
        {
            if (!_pending.Remove(datasetId))
                return;
            _ready++;
        }
        Changed?.Invoke();
    }
}
