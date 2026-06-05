using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Late-bound controller for the viewer's render-state knobs (palette,
/// ECDIS display category) — the analogue of <see cref="IMapHostAccessor"/>
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
}

/// <summary>
/// Late-bound accessor for <see cref="IRenderStateController"/>, mirroring
/// <see cref="IMapHostAccessor"/>. Allows <see cref="McpServerHost"/> to
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
