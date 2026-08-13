using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The CLI's headless, renderer-neutral mutating session: it holds the shared
/// presentation / time / viewport state and backs the mutating MCP capability
/// interfaces (<see cref="IPresentationController"/>, <see cref="ITimeController"/>,
/// <see cref="IViewportController"/>, <see cref="IImageRenderer"/>) over the
/// Mapsui-free Skia composite pipeline.
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
/// Each render composites the catalog's resident processors directly, so a
/// dataset is parsed once at load and never re-parsed per render (issue #566).
/// </description></item>
/// <item><description>
/// The viewport auto-fits the union extent until <c>set_viewport</c> pins an
/// explicit geographic viewport (centre + scale, or a framed bounding box),
/// which is re-fit to each render's pixel size. Rotation is north-up only.
/// </description></item>
/// <item><description>
/// <c>render_to_image</c>'s <c>pixelDensity</c> is not applied — the render
/// uses the requested width/height as literal output pixels, so the PNG matches
/// the reported dimensions. HiDPI scaling would need a DPI-aware composite pass.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class HeadlessS100Session
    : IPresentationController, ITimeController, IViewportController, IImageRenderer, IDisposable
{
    // Reference render surface used to resolve a bounding-box viewport into a
    // representative MapViewport scale for IViewportController.Current. Matches
    // render_to_image's default output size, so the echoed scale lines up with a
    // subsequent default render. Every real render re-fits the box to its own
    // pixel size (see ResolveViewport), so this is an introspection echo only.
    private const int ReferenceWidthPx = 1024;
    private const int ReferenceHeightPx = 768;

    private readonly HeadlessMutableCatalog _catalog;
    private readonly PngS100DatasetRenderer _renderer = new();
    private readonly object _gate = new();

    private MapPresentationState _presentation = MapPresentationState.Default;
    private DateTime? _currentTime;

    // Explicit geographic viewport state. Mutually exclusive: at most one is
    // non-null. Both null means auto-fit the loaded datasets' union extent.
    private MapViewport? _viewport;
    private BoundingBox? _bounds;

    // Volatile so a dispose on one thread is observed by the ObjectDisposedException
    // guards on another; the guards themselves stay best-effort (a dispose racing an
    // in-flight call may still slip through), consistent across the session's methods.
    private volatile bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _presentation = presentation;
        return Task.CompletedTask;
    }

    // ---- ITimeController ------------------------------------------------

    DateTime? ITimeController.Current
    {
        get { lock (_gate) return _currentTime; }
    }

    public IReadOnlyList<DateTime> AvailableSteps => StepsOf(_catalog.RenderProcessors);

    public Task SetTimeAsync(DateTime time, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _currentTime = time;
        return Task.CompletedTask;
    }

    // ---- IViewportController --------------------------------------------

    MapViewport? IViewportController.Current
    {
        get
        {
            lock (_gate)
            {
                if (_viewport is { } v)
                {
                    return v;
                }
                if (_bounds is { } b)
                {
                    // Resolve the box against the reference surface, then echo the
                    // centre and scale of that resolved rectangle. The centre is
                    // taken from the resolved viewport (Mercator midpoint), not the
                    // box's arithmetic midpoint, so it stays consistent with the
                    // rectangle the aspect-fit actually produces.
                    var resolved = CompositeViewportBuilder.FromBoundingBox(
                        b.WestLongitude, b.SouthLatitude,
                        b.EastLongitude, b.NorthLatitude,
                        ReferenceWidthPx, ReferenceHeightPx);
                    var (centerLon, centerLat) = CompositeViewportBuilder.CenterOf(resolved);
                    return new MapViewport(centerLon, centerLat, resolved.ScaleDenominator);
                }
                return null;
            }
        }
    }

    public void Set(MapViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Guard the public capability seam: a programmatic caller (not going
        // through set_viewport, which validates first) must not be able to store a
        // viewport the renderer can't honour or that would feed NaN / out-of-range
        // values into CompositeViewportBuilder. The headless render path is
        // north-up only (ResolveViewport uses centre + scale, not rotation).
        if (viewport.RotationDegrees != 0.0)
        {
            throw new ArgumentException(
                "The headless composite renderer is north-up only; RotationDegrees must be 0.",
                nameof(viewport));
        }
        ValidateLongitude(viewport.CenterLongitude, nameof(viewport));
        ValidateLatitude(viewport.CenterLatitude, nameof(viewport));
        if (!double.IsFinite(viewport.ScaleDenominator) || viewport.ScaleDenominator <= 0.0)
        {
            throw new ArgumentException(
                $"ScaleDenominator must be a positive, finite number; got {viewport.ScaleDenominator}.",
                nameof(viewport));
        }

        lock (_gate)
        {
            _viewport = viewport;
            _bounds = null;
        }
    }

    public void SetToBounds(BoundingBox bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Same seam guard as Set: reject non-finite / out-of-range edges and an
        // inverted or antimeridian-crossing box, which CompositeViewportBuilder
        // cannot frame correctly, before they can reach Current or a render.
        ValidateLongitude(bounds.WestLongitude, nameof(bounds));
        ValidateLongitude(bounds.EastLongitude, nameof(bounds));
        ValidateLatitude(bounds.SouthLatitude, nameof(bounds));
        ValidateLatitude(bounds.NorthLatitude, nameof(bounds));
        if (bounds.WestLongitude >= bounds.EastLongitude)
        {
            throw new ArgumentException(
                $"West ({bounds.WestLongitude}) must be less than East ({bounds.EastLongitude}); antimeridian crossing is not supported.",
                nameof(bounds));
        }
        if (bounds.SouthLatitude >= bounds.NorthLatitude)
        {
            throw new ArgumentException(
                $"South ({bounds.SouthLatitude}) must be less than North ({bounds.NorthLatitude}).",
                nameof(bounds));
        }

        lock (_gate)
        {
            _bounds = bounds;
            _viewport = null;
        }
    }

    private const double MaxLongitude = 180.0;

    private static void ValidateLongitude(double value, string paramName)
    {
        if (!double.IsFinite(value) || value < -MaxLongitude || value > MaxLongitude)
        {
            throw new ArgumentException(
                $"Longitude {value} is outside the supported range [{-MaxLongitude}, {MaxLongitude}].",
                paramName);
        }
    }

    private static void ValidateLatitude(double value, string paramName)
    {
        if (!double.IsFinite(value)
            || value < -CompositeViewportBuilder.MaxLatitude
            || value > CompositeViewportBuilder.MaxLatitude)
        {
            throw new ArgumentException(
                $"Latitude {value} is outside the Web Mercator range "
                + $"[{-CompositeViewportBuilder.MaxLatitude}, {CompositeViewportBuilder.MaxLatitude}].",
                paramName);
        }
    }

    /// <summary>
    /// Resolves the current geographic viewport state to a pixel
    /// <see cref="EncDotNet.S100.Pipelines.Viewport"/> for a
    /// <paramref name="widthPx"/> × <paramref name="heightPx"/> render, or
    /// <see langword="null"/> to keep the compositor's union auto-fit.
    /// </summary>
    private EncDotNet.S100.Pipelines.Viewport? ResolveViewport(int widthPx, int heightPx)
    {
        lock (_gate)
        {
            if (_viewport is { } v)
            {
                return CompositeViewportBuilder.FromCenterScale(
                    v.CenterLongitude, v.CenterLatitude, v.ScaleDenominator, widthPx, heightPx);
            }
            if (_bounds is { } b)
            {
                return CompositeViewportBuilder.FromBoundingBox(
                    b.WestLongitude, b.SouthLatitude,
                    b.EastLongitude, b.NorthLatitude,
                    widthPx, heightPx);
            }
            return null;
        }
    }

    // ---- IImageRenderer -------------------------------------------------

    public Task<byte[]?> RenderToPngAsync(
        int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Render under the catalog's render gate so the processors cannot be
        // disposed by a concurrent close_dataset / close_all_datasets mid-render.
        // The dimensions are captured in the closure (no shared render state).
        return _catalog.RenderAsync(
            (processors, token) => RenderCoreAsync(processors, widthPx, heightPx, token),
            cancellationToken);
    }

    private async Task<byte[]?> RenderCoreAsync(
        IReadOnlyList<IDatasetProcessor> processors, int widthPx, int heightPx, CancellationToken cancellationToken)
    {
        if (processors.Count == 0)
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

        var options = new S100CompositeOptions
        {
            Width = widthPx,
            Height = heightPx,
            Palette = presentation.Palette,
            SymbolScale = presentation.SymbolScale,
            TextScale = presentation.TextScale,
            Mariner = presentation.Mariner,
            TimeStep = ResolveTimeStepIndex(time, processors),
            // Carry the full ECDIS snapshot (category, hidden viewing groups /
            // display planes, and per-spec display modes). The composite renderer
            // resolves the per-spec display mode for each layer via
            // FacadeRenderContextBuilder, matching the single-dataset
            // MapPresentationState.ApplyTo projection.
            EcdisDisplay = presentation.EcdisDisplay,
            // Resolve the explicit geographic viewport (if any) to this render's
            // pixel size; null keeps the compositor's union auto-fit.
            Viewport = ResolveViewport(widthPx, heightPx),
        };

        // Composite the resident processors directly — no per-render re-parse.
        // The literal output pixel dimensions are used (pixelDensity is not
        // applied — the composite renderer has no DPI-aware pass; a v1 gap).
        return await _renderer.RenderAsync(processors, options, cancellationToken).ConfigureAwait(false);
    }

    private static List<DateTime> StepsOf(IReadOnlyList<IDatasetProcessor> processors)
    {
        var steps = new List<DateTime>();
        foreach (var processor in processors)
        {
            if (processor is not ITimeAwareDatasetProcessor timeAware)
            {
                continue;
            }
            try
            {
                steps.AddRange(timeAware.AvailableTimes);
            }
            catch (ObjectDisposedException)
            {
                // The dataset was closed concurrently; skip it.
            }
        }
        return steps.Distinct().OrderBy(t => t).ToList();
    }

    private static int ResolveTimeStepIndex(DateTime? time, IReadOnlyList<IDatasetProcessor> processors)
    {
        if (time is null)
        {
            return 0;
        }

        var steps = StepsOf(processors);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // The session owns the renderer only; the catalog (and its dataset
        // handles) is owned and disposed by the caller.
        _renderer.Dispose();
    }
}
