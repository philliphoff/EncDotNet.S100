using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The CLI's headless, renderer-neutral mutating session: it holds the shared
/// presentation / time state and backs the mutating MCP capability interfaces
/// (<see cref="IPresentationController"/>, <see cref="ITimeController"/>,
/// <see cref="IImageRenderer"/>) over the Mapsui-free Skia composite pipeline.
/// The dataset set itself lives in the <see cref="HeadlessMutableCatalog"/> —
/// the single source of truth shared with the read-only query tools — so the
/// session always renders whatever is currently loaded, including datasets added
/// via <c>open_dataset</c> mid-session.
/// </summary>
/// <remarks>
/// <para>
/// v1 scope and known gaps (tracked for the presentation-model unification
/// follow-up):
/// </para>
/// <list type="bullet">
/// <item><description>
/// The composite render options (<see cref="S100CompositeOptions"/>) carry a
/// reduced presentation model — palette, symbol/text scale, mariner, a single
/// display-mode id, and a time-step index — so ECDIS <em>category</em>,
/// viewing-group, and display-plane selections in
/// <see cref="MapPresentationState"/> are not yet honoured here.
/// </description></item>
/// <item><description>
/// Each render re-creates processors from the dataset paths (the composite
/// renderer's contract), so repeated renders re-parse. Acceptable for v1.
/// </description></item>
/// <item><description>
/// The viewport auto-fits the union extent (no <c>set_viewport</c> yet).
/// </description></item>
/// <item><description>
/// <c>render_to_image</c>'s <c>pixelDensity</c> is not applied — the render
/// uses the requested width/height as literal output pixels, so the PNG matches
/// the reported dimensions. HiDPI scaling would need a DPI-aware composite pass.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class HeadlessS100Session
    : IPresentationController, ITimeController, IImageRenderer, IDisposable
{
    private readonly HeadlessMutableCatalog _catalog;
    private readonly PngS100DatasetRenderer _renderer = new();
    private readonly object _gate = new();

    private MapPresentationState _presentation = MapPresentationState.Default;
    private DateTime? _currentTime;
    private bool _disposed;

    /// <summary>Creates a session over the shared mutable catalog.</summary>
    /// <param name="catalog">
    /// The catalog owning the loaded datasets. The session does not own it — the
    /// caller disposes the catalog — but reads the current dataset set from it on
    /// every render and time query.
    /// </param>
    public HeadlessS100Session(HeadlessMutableCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    // ---- IPresentationController ---------------------------------------

    MapPresentationState IPresentationController.Current
    {
        get { lock (_gate) return _presentation; }
    }

    public Task SetPresentationAsync(
        MapPresentationState presentation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        lock (_gate) _presentation = presentation;
        return Task.CompletedTask;
    }

    // ---- ITimeController ------------------------------------------------

    DateTime? ITimeController.Current
    {
        get { lock (_gate) return _currentTime; }
    }

    public IReadOnlyList<DateTime> AvailableSteps => StepsOf(_catalog.RenderHandles);

    public Task SetTimeAsync(DateTime time, CancellationToken cancellationToken = default)
    {
        lock (_gate) _currentTime = time;
        return Task.CompletedTask;
    }

    // ---- IImageRenderer -------------------------------------------------

    public Task<byte[]?> RenderToPngAsync(
        int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Render under the catalog's render gate so the handles cannot be
        // disposed by a concurrent close_dataset / close_all_datasets mid-render.
        // The dimensions are captured in the closure (no shared render state).
        return _catalog.RenderAsync(
            (handles, token) => RenderCoreAsync(handles, widthPx, heightPx, token),
            cancellationToken);
    }

    private async Task<byte[]?> RenderCoreAsync(
        IReadOnlyList<S100Dataset> handles, int widthPx, int heightPx, CancellationToken cancellationToken)
    {
        if (handles.Count == 0)
        {
            return null;
        }

        MapPresentationState presentation;
        DateTime? time;
        lock (_gate)
        {
            presentation = _presentation;
            time = _currentTime;
        }

        // The literal output pixel dimensions are used (pixelDensity is not
        // applied — the composite renderer has no DPI-aware pass; a v1 gap).
        var layers = handles.Select(d => new S100Layer { Dataset = d }).ToList();

        var options = new S100CompositeOptions
        {
            Width = widthPx,
            Height = heightPx,
            Palette = presentation.Palette,
            SymbolScale = presentation.SymbolScale,
            TextScale = presentation.TextScale,
            Mariner = presentation.Mariner,
            TimeStep = ResolveTimeStepIndex(time, handles),
            DisplayModeId = ResolveDisplayModeId(presentation),
            Viewport = null, // auto-fit the union extent
        };

        return await _renderer.RenderAsync(layers, options, cancellationToken).ConfigureAwait(false);
    }

    private static List<DateTime> StepsOf(IReadOnlyList<S100Dataset> handles)
    {
        var steps = new List<DateTime>();
        foreach (var handle in handles)
        {
            try
            {
                steps.AddRange(handle.AvailableTimes);
            }
            catch (ObjectDisposedException)
            {
                // The dataset was closed concurrently; skip it.
            }
        }
        return steps.Distinct().OrderBy(t => t).ToList();
    }

    private static int ResolveTimeStepIndex(DateTime? time, IReadOnlyList<S100Dataset> handles)
    {
        if (time is null)
        {
            return 0;
        }

        var steps = StepsOf(handles);
        if (steps.Count == 0)
        {
            return 0;
        }

        var exact = steps.IndexOf(time.Value);
        if (exact >= 0)
        {
            return exact;
        }

        var best = 0;
        var bestDiff = long.MaxValue;
        for (var i = 0; i < steps.Count; i++)
        {
            var diff = Math.Abs((steps[i] - time.Value).Ticks);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    private static string? ResolveDisplayModeId(MapPresentationState presentation)
    {
        var modes = presentation.EcdisDisplay.ActiveDisplayModes;
        if (modes.TryGetValue("S-411", out var s411) && !string.IsNullOrEmpty(s411))
        {
            return s411;
        }
        return modes.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // The session owns the renderer only; the catalog (and its dataset
        // handles) is owned and disposed by the caller.
        _renderer.Dispose();
    }
}
