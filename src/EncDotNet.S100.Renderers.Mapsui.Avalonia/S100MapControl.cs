using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

/// <summary>
/// A batteries-included Avalonia map control that attaches a reusable S-100
/// session to itself in one call and owns that session's lifetime.
/// </summary>
/// <remarks>
/// <para>
/// This is the smallest-wiring entry point for an Avalonia host: drop the
/// control in XAML and call <see cref="Configure"/> once from the code-behind.
/// It creates an <c>EPSG:3857</c> map if one is not already set, runs
/// <see cref="AvaloniaS100MapExtensions.AddS100"/> to attach the session and the
/// <see cref="AvaloniaMapsuiMapAdapter"/>, exposes both through
/// <see cref="Session"/> and <see cref="Adapter"/>, and disposes them together
/// when the control is disposed — collapsing the map creation, the
/// <c>AddS100</c> tuple, the adapter/session fields, the pick plumbing, and the
/// two-object disposal a host would otherwise write by hand.
/// </para>
/// <para>
/// It derives from <see cref="CaptureSynchronizedMapControl"/>, so
/// <see cref="AvaloniaMapsuiMapAdapter.RenderCurrentViewToPngAsync"/> captures are
/// race-safe against the live paint. A host that needs to inject shared
/// collaborators, keep its own paint diagnostics, or otherwise drive the wiring
/// itself can instead call <c>mapControl.AddS100(...)</c> on a plain control; this
/// control is the convenience path, not the only one.
/// </para>
/// <para>
/// The control owns the session and adapter, so a host disposes it (e.g. from the
/// window's <c>Closed</c> handler) to release them; disposal does not dispose any
/// collaborator the host supplied on the options. All members follow the base
/// control's threading contract — call them on Avalonia's UI thread.
/// </para>
/// </remarks>
public class S100MapControl : CaptureSynchronizedMapControl
{
    private IS100MapSession? _session;
    private AvaloniaMapsuiMapAdapter? _adapter;
    private bool _disposed;

    /// <summary>
    /// The attached S-100 session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Configure"/> has not been called.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The control is disposed.</exception>
    public IS100MapSession Session
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _session ?? throw new InvalidOperationException(
                "The S-100 map control has not been configured yet. Call Configure first.");
        }
    }

    /// <summary>
    /// The attached Avalonia adapter (redraw, coordinate conversion, capture,
    /// pointer picking).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Configure"/> has not been called.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The control is disposed.</exception>
    public AvaloniaMapsuiMapAdapter Adapter
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _adapter ?? throw new InvalidOperationException(
                "The S-100 map control has not been configured yet. Call Configure first.");
        }
    }

    /// <summary>
    /// Whether the control currently has a usable attached session — that is,
    /// <see cref="Configure"/> has run and the control has not been disposed.
    /// </summary>
    public bool IsConfigured => !_disposed && _session is not null;

    /// <summary>
    /// Attaches an S-100 session and adapter to this control. Creates an
    /// <c>EPSG:3857</c> <see cref="global::Mapsui.Map"/> when the control has no
    /// map, then runs <see cref="AvaloniaS100MapExtensions.AddS100"/>.
    /// </summary>
    /// <param name="options">
    /// Rendering configuration. Supply a
    /// <see cref="S100MapsuiOptions.CrsTransformFactory"/> (or a prebuilt
    /// <see cref="S100MapsuiOptions.DatasetRenderer"/>); see
    /// <see cref="S100MapExtensions.AddS100"/> for the full set.
    /// </param>
    /// <returns>The attached session (also available through <see cref="Session"/>).</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The control is already configured, the call is not on Avalonia's UI thread,
    /// or the map could not be attached.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The control is disposed.</exception>
    public IS100MapSession Configure(S100MapsuiOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        if (_session is not null)
        {
            throw new InvalidOperationException(
                "The S-100 map control is already configured.");
        }

        Map ??= new Map { CRS = "EPSG:3857" };
        (_session, _adapter) = this.AddS100(options);
        return _session;
    }

    /// <summary>
    /// Schedules a live redraw of the map on Avalonia's UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Configure"/> has not been called.
    /// </exception>
    public void RequestRedraw() => Adapter.RequestRedraw();

    /// <summary>
    /// Picks the S-100 features and coverage samples under a live-viewport pixel,
    /// delegating to <see cref="AvaloniaMapsuiMapAdapter.PickAtScreenAsync"/>
    /// against this control's <see cref="Session"/> query.
    /// </summary>
    /// <param name="xPx">Horizontal live-control pixel coordinate.</param>
    /// <param name="yPx">Vertical live-control pixel coordinate.</param>
    /// <param name="radiusMeters">Point/curve tolerance in metres.</param>
    /// <param name="maxResults">Optional cap on the returned picks (topmost-first).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The ranked picks, topmost-first.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Configure"/> has not been called.
    /// </exception>
    public Task<IReadOnlyList<S100Pick>> PickAsync(
        double xPx,
        double yPx,
        double radiusMeters = AvaloniaMapsuiMapAdapter.DefaultPickRadiusMeters,
        int? maxResults = null,
        CancellationToken cancellationToken = default) =>
        Adapter.PickAtScreenAsync(
            Session.Query, xPx, yPx, radiusMeters, maxResults, cancellationToken);

    /// <summary>
    /// Disposes the attached adapter and session alongside the base control.
    /// Idempotent; safe when <see cref="Configure"/> was never called. Does not
    /// dispose collaborators the host supplied on the options (e.g. a shared
    /// processor owner).
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="System.IDisposable.Dispose"/>.
    /// </param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _adapter?.Dispose();
            _session?.Dispose();
        }

        base.Dispose(disposing);
    }
}
