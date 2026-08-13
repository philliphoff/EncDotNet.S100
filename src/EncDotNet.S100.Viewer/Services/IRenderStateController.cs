using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Late-bound controller for the viewer's render-state knobs (palette,
/// ECDIS display category) — the analogue of
/// <see cref="ICapabilityAccessor{TCapability}"/>
/// for state that lives in view-models and singletons rather than in the
/// Mapsui control. Lets MCP tools mutate the live render state from
/// off-UI threads without coupling them directly to <c>SettingsViewModel</c>
/// or <c>EcdisDisplayState</c>.
/// </summary>
internal interface IRenderStateController
{
    /// <summary>The currently active map palette.</summary>
    PaletteType CurrentPalette { get; }

    /// <summary>The currently active ECDIS display category.</summary>
    EcdisDisplayCategory CurrentDisplayCategory { get; }

    /// <summary>The currently active base-plane render subsystem ("A" vs "B").</summary>
    RenderSubsystemKind CurrentRenderSubsystem { get; }

    /// <summary>
    /// True when the render subsystem is pinned by the
    /// <c>S100_RENDER_SUBSYSTEM</c> environment variable and therefore
    /// cannot be switched at runtime.
    /// </summary>
    bool RenderSubsystemPinned { get; }

    /// <summary>
    /// Sets the active map palette. Marshals to the UI thread when
    /// the underlying setter has thread affinity (e.g. INotifyPropertyChanged
    /// observers bound to the UI). Idempotent: setting the current value
    /// is a no-op.
    /// </summary>
    Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default);

    /// <summary>
    /// Sets the active ECDIS display category. Marshals to the UI
    /// thread for parity with <see cref="SetPaletteAsync"/> — the
    /// underlying state is itself thread-safe but downstream
    /// <c>Changed</c> subscribers may touch UI state.
    /// </summary>
    Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default);

    /// <summary>
    /// Switches the live base-plane render subsystem between "A"
    /// (<see cref="RenderSubsystemKind.Mapsui"/>) and "B"
    /// (<see cref="RenderSubsystemKind.TiledScene"/>). The change is read
    /// per-render, so it rebinds the active subsystem on the next
    /// re-render. Marshals to the UI thread for parity with
    /// <see cref="SetPaletteAsync"/>. Idempotent: setting the current
    /// value is a no-op. Throws <see cref="System.InvalidOperationException"/>
    /// when <see cref="RenderSubsystemPinned"/> is true.
    /// </summary>
    Task SetRenderSubsystemAsync(RenderSubsystemKind subsystem, CancellationToken ct = default);

    /// <summary>
    /// Gets the explicit spec-native display-mode id currently active for
    /// <paramref name="spec"/> (e.g. an S-411 sea-ice portrayal mode,
    /// S-100 Part 9 §11.7), or <see langword="null"/> when no explicit
    /// mode is selected (each catalogue's default look applies).
    /// </summary>
    /// <param name="spec">The product-specification code (e.g. <c>S-411</c>).</param>
    string? GetDisplayMode(string spec);

    /// <summary>
    /// Sets the explicit spec-native display-mode id for
    /// <paramref name="spec"/> (S-100 Part 9 §11.7). A <see langword="null"/>
    /// or empty <paramref name="modeId"/> clears the selection so the
    /// catalogue default applies. Marshals to the UI thread for parity with
    /// <see cref="SetDisplayCategoryAsync"/>. Idempotent: setting the
    /// current value is a no-op.
    /// </summary>
    /// <param name="spec">The product-specification code (e.g. <c>S-411</c>).</param>
    /// <param name="modeId">The spec-native mode id, or <see langword="null"/> to clear.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetDisplayModeAsync(string spec, string? modeId, CancellationToken ct = default);
}

/// <summary>
/// Late-bound accessor for <see cref="IRenderStateController"/>, mirroring
/// <see cref="ICapabilityAccessor{TCapability}"/>. Allows
/// <see cref="McpServerHost"/> to
/// resolve the controller before the viewer's main window finishes
/// constructing it.
/// </summary>
internal interface IRenderStateControllerAccessor
{
    /// <summary>The current render-state controller, or null when not yet attached.</summary>
    IRenderStateController? Current { get; set; }
}

/// <summary>Default in-memory implementation of <see cref="IRenderStateControllerAccessor"/>.</summary>
internal sealed class RenderStateControllerAccessor : IRenderStateControllerAccessor
{
    public IRenderStateController? Current { get; set; }
}
