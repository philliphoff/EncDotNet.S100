using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Serves tiles rendered from datasets, and reopens the datasets when their
/// files change (issue #865, <c>tiles serve --refresh</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every <c>--refresh</c> seconds the input files are fingerprinted again
/// (<see cref="TileRenderSession.Fingerprint(Commands.TilesRenderSettings)"/>:
/// paths, sizes and write times only). A change is acted on once the new
/// fingerprint has held for two checks in a row, so a dataset still being
/// copied in isn't read half-written. The datasets are then opened and
/// portrayed again in the background while the old ones keep serving, and new
/// requests switch over when that's done. The old datasets are disposed once
/// the requests and renders still using them finish.
/// </para>
/// <para>
/// The new datasets start with an empty memory cache, and a disk cache under
/// their own fingerprint, so no tile of the old data is served again. If they
/// can't be opened, the old ones keep serving and the change is reported; it
/// is tried again when the files change again.
/// </para>
/// </remarks>
internal sealed class RefreshingTileSource : ITileSource, IAsyncDisposable
{
    private readonly Func<RenderedTileSource?> _open;
    private readonly Func<string> _fingerprint;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private readonly List<Task> _retiring = [];

    // Serialises checks and replacements (issue #1494), so they don't open the datasets twice at once.
    private readonly SemaphoreSlim _changing = new(1, 1);

    private Generation _current;
    private string _servedFingerprint;
    private string? _pendingFingerprint;
    private string? _failedFingerprint;
    private int _reloads;

    /// <param name="initial">The datasets as first opened.</param>
    /// <param name="fingerprint">The fingerprint <paramref name="initial"/> was opened at.</param>
    /// <param name="computeFingerprint">Fingerprints the input files as they are now.</param>
    /// <param name="open">Opens and prepares the datasets again; <see langword="null"/> when they can't be (having said why).</param>
    /// <param name="interval">How often to check the files; <see cref="Timeout.InfiniteTimeSpan"/> to check only through <see cref="CheckAsync"/>.</param>
    /// <param name="log">Reports reloads and failures.</param>
    public RefreshingTileSource(
        RenderedTileSource initial,
        string fingerprint,
        Func<string> computeFingerprint,
        Func<RenderedTileSource?> open,
        TimeSpan interval,
        Action<string> log)
    {
        _current = new Generation(initial);
        _servedFingerprint = fingerprint;
        _fingerprint = computeFingerprint;
        _open = open;
        _log = log;
        _loop = interval == Timeout.InfiniteTimeSpan ? Task.CompletedTask : RunAsync(interval, _stopping.Token);
    }

    /// <summary>The datasets currently served.</summary>
    public RenderedTileSource Current
    {
        get
        {
            lock (_gate)
                return _current.Source;
        }
    }

    /// <summary>How many times the datasets have been reopened.</summary>
    public int Reloads => Volatile.Read(ref _reloads);

    public string Path => Current.Path;

    public TileImageFormat Format => Current.Format;

    public IReadOnlyList<string> Palettes => Current.Palettes;

    public IReadOnlyList<DateTime> Times => Current.Times;

    public DatasetFeatureQuery? FeatureQuery => Current.FeatureQuery;

    public MarinerStyleRegistry? MarinerStyles => Current.MarinerStyles;

    public async ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken)
    {
        var generation = Acquire();
        try
        {
            return await generation.Source.ReadAsync(zoom, x, y, palette, time, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release(generation);
        }
    }

    public JsonObject ToTileJson(string? palette, DateTime? time = null)
    {
        var generation = Acquire();
        try
        {
            return generation.Source.ToTileJson(palette, time);
        }
        finally
        {
            Release(generation);
        }
    }

    /// <summary>
    /// Checks the input files once, and reopens the datasets when a change has
    /// held since the previous check. Waits for a <see cref="ReplaceAsync"/>
    /// under way, and the other way round.
    /// </summary>
    /// <returns>Whether the datasets were reopened.</returns>
    public async Task<bool> CheckAsync()
    {
        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            return await CheckCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// Opens other datasets now and serves them in place of the current ones
    /// once they are ready (<c>--manage-datasets</c>, issue #1494). The current
    /// ones keep serving until then, and when they can't be opened.
    /// </summary>
    /// <param name="open">Opens and prepares the datasets; <see langword="null"/> when they can't be (having said why). An exception it throws is thrown here.</param>
    /// <param name="fingerprint">The fingerprint the datasets are opened at, which later checks compare with.</param>
    /// <param name="replaced">Called once they serve, before any later check or replacement starts.</param>
    /// <returns>Whether the datasets were replaced.</returns>
    public async Task<bool> ReplaceAsync(Func<RenderedTileSource?> open, string fingerprint, Action? replaced = null)
    {
        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            var opened = await Task.Run(open).ConfigureAwait(false);
            if (opened is null)
                return false;

            Serve(opened, fingerprint, $"Now serving {opened.Path}, zoom {opened.Layout.MinZoom}–{opened.Layout.MaxZoom}.");
            _pendingFingerprint = null;
            replaced?.Invoke();
            return true;
        }
        finally
        {
            _changing.Release();
        }
    }

    private async Task<bool> CheckCoreAsync()
    {
        string fingerprint;
        try
        {
            fingerprint = _fingerprint();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file vanished mid-listing (or is being replaced); look again next time.
            _pendingFingerprint = null;
            return false;
        }

        if (fingerprint == _servedFingerprint || fingerprint == _failedFingerprint)
        {
            _pendingFingerprint = null;
            return false;
        }

        if (fingerprint != _pendingFingerprint)
        {
            // Changed since the last check: wait for it to settle.
            _pendingFingerprint = fingerprint;
            return false;
        }

        _pendingFingerprint = null;
        RenderedTileSource? reopened;
        try
        {
            reopened = await Task.Run(_open).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log($"The datasets changed but couldn't be reopened ({e.Message}); still serving the previous version.");
            reopened = null;
        }

        if (reopened is null)
        {
            _failedFingerprint = fingerprint;
            return false;
        }

        Serve(reopened, fingerprint, $"The datasets changed; now serving {reopened.Path}, zoom {reopened.Layout.MinZoom}–{reopened.Layout.MaxZoom}.");
        return true;
    }

    /// <summary>Switches new requests to <paramref name="opened"/>, and retires the datasets they replace.</summary>
    private void Serve(RenderedTileSource opened, string fingerprint, string message)
    {
        Generation retired;
        lock (_gate)
        {
            retired = _current;
            _current = new Generation(opened);
        }

        _servedFingerprint = fingerprint;
        _failedFingerprint = null;
        Interlocked.Increment(ref _reloads);
        _log(message);

        lock (_retiring)
            _retiring.Add(RetireAsync(retired));
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // Let a replacement under way finish; none starts after this.
        await _changing.WaitAsync().ConfigureAwait(false);

        Task[] retiring;
        lock (_retiring)
            retiring = [.. _retiring];
        await Task.WhenAll(retiring).ConfigureAwait(false);

        Generation current;
        lock (_gate)
            current = _current;

        await RetireAsync(current).ConfigureAwait(false);
        _stopping.Dispose();
        _changing.Dispose();
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await CheckAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log($"Checking the datasets for changes failed: {e.Message}");
            }
        }
    }

    private Generation Acquire()
    {
        lock (_gate)
        {
            _current.Readers++;
            return _current;
        }
    }

    private void Release(Generation generation)
    {
        lock (_gate)
            generation.Readers--;
    }

    /// <summary>Disposes a replaced generation once its readers and renders are done.</summary>
    private async Task RetireAsync(Generation generation)
    {
        while (true)
        {
            lock (_gate)
            {
                if (generation.Readers == 0)
                    break;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        await generation.Source.DrainAsync().ConfigureAwait(false);
        generation.Source.Dispose();
    }

    /// <summary>One opening of the datasets, and how many requests are reading from it.</summary>
    private sealed class Generation(RenderedTileSource source)
    {
        public RenderedTileSource Source { get; } = source;

        public int Readers { get; set; }
    }
}
