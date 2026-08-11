using Avalonia.Threading;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

/// <summary>
/// Avalonia-specific one-call wiring for the reusable S-100 Mapsui extension.
/// </summary>
public static class AvaloniaS100MapExtensions
{
    /// <summary>
    /// Attaches an S-100 session to the control's map and an
    /// <see cref="AvaloniaMapsuiMapAdapter"/> to the control in a single call,
    /// defaulting the session's redraw marshal to the control's dispatcher so the
    /// background vector renderers repaint the live control when a settled cached
    /// / scene / tile image publishes off-thread.
    /// </summary>
    /// <param name="mapControl">
    /// The live control to attach to. Its <see cref="global::Mapsui.Map"/> must be
    /// set before calling.
    /// </param>
    /// <param name="options">
    /// Rendering configuration for <see cref="S100MapExtensions.AddS100"/>. Supply
    /// a CRS transform factory (or a prebuilt renderer). When its
    /// <see cref="S100MapsuiOptions.RedrawMarshal"/> is <see langword="null"/>, a
    /// UI-thread marshal is supplied automatically; a host-set marshal is kept.
    /// </param>
    /// <returns>
    /// The owned, disposable session and the attached adapter. The caller disposes
    /// both (the adapter borrows the control and map; disposing it does not dispose
    /// them).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mapControl"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on Avalonia's UI thread, or the control has no map.
    /// </exception>
    public static (IS100MapSession Session, AvaloniaMapsuiMapAdapter Adapter) AddS100(
        this CaptureSynchronizedMapControl mapControl,
        S100MapsuiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "An S-100 session must be attached to a Mapsui control on the UI thread.");
        }

        var map = mapControl.Map
            ?? throw new InvalidOperationException(
                "The Mapsui map control must have a map before attaching an S-100 session.");

        options ??= new S100MapsuiOptions();
        if (options.RedrawMarshal is null)
        {
            options = options with
            {
                RedrawMarshal = static action => Dispatcher.UIThread.Post(action),
            };
        }

        var session = map.AddS100(options);
        var adapter = AvaloniaMapsuiMapAdapter.Attach(mapControl);
        return (session, adapter);
    }
}
