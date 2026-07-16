namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Immutable snapshot of one of the viewer's activity panels (a tab in
/// the left / right / bottom dock). Returned by
/// <see cref="IViewerUiController"/> so MCP / scripted callers can
/// discover the available panels and verify their visibility after a
/// mutation without coupling to <c>MainViewModel</c> or the Avalonia
/// activity-bar internals.
/// </summary>
/// <param name="Id">Stable panel identifier (e.g. <c>"Datasets"</c>, <c>"PickReport"</c>, <c>"Timeline"</c>).</param>
/// <param name="Title">Resolved, localised pane title.</param>
/// <param name="Dock">The dock the panel lives in: <c>"Left"</c>, <c>"Right"</c>, or <c>"Bottom"</c>.</param>
/// <param name="Available">
/// Whether the panel is currently registered in the activity bar. A few
/// panels are conditionally available (e.g. <c>Vessels</c> only while the
/// AIS overlay is enabled, <c>Helm</c> only while own-ship tracking is
/// enabled); an unavailable panel cannot be shown.
/// </param>
/// <param name="Selected">Whether this panel is the active tab within its dock.</param>
/// <param name="DockOpen">Whether the panel's owning dock is currently expanded.</param>
/// <param name="Showing">
/// Whether the panel is actually visible to the user right now — the
/// conjunction of <paramref name="Available"/>, <paramref name="Selected"/>,
/// and <paramref name="DockOpen"/>.
/// </param>
internal readonly record struct ViewerPanelState(
    string Id,
    string Title,
    string Dock,
    bool Available,
    bool Selected,
    bool DockOpen,
    bool Showing);

/// <summary>
/// Outcome of an <see cref="IViewerUiController.SetPanelVisibilityAsync"/>
/// call. Distinguishes an unknown panel id from a well-formed request that
/// could not be honoured because the panel is not currently available.
/// </summary>
/// <param name="Found">Whether the requested panel id resolved to a registered panel.</param>
/// <param name="Available">
/// Whether the resolved panel was available to be shown at the time of the
/// call. When a caller requested <c>visible = true</c> and this is
/// <see langword="false"/>, the panel state was left unchanged.
/// </param>
/// <param name="State">
/// The panel's state after the operation, or <see langword="null"/> when
/// <paramref name="Found"/> is <see langword="false"/>.
/// </param>
/// <param name="PreviousShowing">
/// Whether the panel was showing (visible to the user) immediately before
/// the operation. Lets callers detect an idempotent no-op by comparing
/// against <see cref="ViewerPanelState.Showing"/> on <paramref name="State"/>.
/// </param>
internal readonly record struct PanelMutationOutcome(
    bool Found,
    bool Available,
    ViewerPanelState? State,
    bool PreviousShowing);

/// <summary>
/// Late-bound controller for the viewer's activity-panel UX (which docks
/// are open and which tab each shows) — the analogue of
/// <see cref="IRenderStateController"/> for panel visibility rather than
/// render state. Lets MCP tools inspect and drive the viewer's panels from
/// off-UI threads without coupling them directly to <c>MainViewModel</c>.
/// </summary>
internal interface IViewerUiController
{
    /// <summary>
    /// Snapshots every registered activity panel and its current
    /// visibility. Marshals to the UI thread so the snapshot is
    /// consistent with the interactive activity bar.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The panels, ordered left dock first, then right, then bottom, each in activity-bar order.</returns>
    Task<IReadOnlyList<ViewerPanelState>> GetPanelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Shows or hides the panel with the given id. Showing selects the
    /// panel's tab and opens its dock; hiding closes the panel's dock when
    /// the panel is the one currently shown there (otherwise a no-op).
    /// Idempotent — a panel that is already in the requested state is left
    /// untouched. Marshals to the UI thread.
    /// </summary>
    /// <param name="panelId">The panel id (case-insensitive).</param>
    /// <param name="visible"><see langword="true"/> to show, <see langword="false"/> to hide.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The mutation outcome, including whether the id resolved, whether the
    /// panel was available, and the resulting panel state.
    /// </returns>
    Task<PanelMutationOutcome> SetPanelVisibilityAsync(string panelId, bool visible, CancellationToken ct = default);
}

/// <summary>
/// Late-bound accessor for <see cref="IViewerUiController"/>, mirroring
/// <see cref="IRenderStateControllerAccessor"/>. Allows
/// <c>McpServerHost</c> to resolve the controller before the viewer's main
/// window finishes constructing it.
/// </summary>
internal interface IViewerUiControllerAccessor
{
    /// <summary>The current UI controller, or <see langword="null"/> when not yet attached.</summary>
    IViewerUiController? Current { get; set; }
}

/// <summary>Default in-memory implementation of <see cref="IViewerUiControllerAccessor"/>.</summary>
internal sealed class ViewerUiControllerAccessor : IViewerUiControllerAccessor
{
    public IViewerUiController? Current { get; set; }
}
