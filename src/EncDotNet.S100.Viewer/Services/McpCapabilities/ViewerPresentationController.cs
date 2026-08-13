using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services.McpCapabilities;

/// <summary>
/// Adapts the viewer's per-knob <see cref="IRenderStateController"/> to the
/// shared, whole-state <see cref="IPresentationController"/> that backs the
/// <c>set_palette</c>, <c>set_display_category</c>, and <c>set_display_mode</c>
/// tools.
/// </summary>
/// <remarks>
/// <para>
/// The shared tools read <see cref="Current"/>, produce a one-field transform
/// (e.g. <c>Current.WithPalette(…)</c>), and hand the whole
/// <see cref="MapPresentationState"/> back through
/// <see cref="SetPresentationAsync"/>. The viewer instead exposes individual,
/// UI-thread-marshalling setters, so this adapter bridges the two directions:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="Current"/> is sourced from the live
/// <see cref="MapPresentationStateProjection"/> (passed as its
/// <c>CreateSnapshot</c> delegate), preserving the viewer's full ECDIS fidelity
/// (viewing-group and display-plane overrides, mariner settings) that a
/// reconstruction from the controller's scalar reads would lose.
/// </description></item>
/// <item><description>
/// <see cref="SetPresentationAsync"/> diffs the incoming state against the
/// current snapshot and forwards only the fields these tools can change —
/// palette, ECDIS category, and per-spec display modes — to the matching
/// <see cref="IRenderStateController"/> setters, each idempotent and marshalled
/// to the UI thread by the controller. Symbol/text scale and mariner settings
/// have no setter here and are never mutated by this tool set, so they pass
/// through untouched.
/// </description></item>
/// </list>
/// </remarks>
/// <param name="controller">The viewer's live render-state controller.</param>
/// <param name="readCurrent">
/// Reads the viewer's current presentation as an immutable snapshot — in
/// production, <see cref="MapPresentationStateProjection.CreateSnapshot"/>.
/// </param>
internal sealed class ViewerPresentationController(
    IRenderStateController controller,
    Func<MapPresentationState> readCurrent)
    : IPresentationController
{
    private readonly IRenderStateController _controller = controller
        ?? throw new ArgumentNullException(nameof(controller));

    private readonly Func<MapPresentationState> _readCurrent = readCurrent
        ?? throw new ArgumentNullException(nameof(readCurrent));

    /// <inheritdoc />
    public MapPresentationState Current => _readCurrent();

    /// <inheritdoc />
    public async Task SetPresentationAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        cancellationToken.ThrowIfCancellationRequested();

        var current = _readCurrent();

        if (presentation.Palette != current.Palette)
        {
            await _controller.SetPaletteAsync(presentation.Palette, cancellationToken)
                .ConfigureAwait(false);
        }

        if (presentation.EcdisDisplay.Category != current.EcdisDisplay.Category)
        {
            await _controller.SetDisplayCategoryAsync(
                presentation.EcdisDisplay.Category, cancellationToken).ConfigureAwait(false);
        }

        // Forward every per-spec display-mode entry that differs, in either
        // direction (a newly-selected mode, a changed mode, or a cleared one).
        // Production snapshots key ActiveDisplayModes case-insensitively; take
        // OrdinalIgnoreCase views up front so the union keys and the value
        // lookups below resolve under the same comparer regardless of how the
        // source dictionaries were built.
        var currentModes = new Dictionary<string, string?>(
            current.EcdisDisplay.ActiveDisplayModes, StringComparer.OrdinalIgnoreCase);
        var nextModes = new Dictionary<string, string?>(
            presentation.EcdisDisplay.ActiveDisplayModes, StringComparer.OrdinalIgnoreCase);
        foreach (var spec in currentModes.Keys.Union(nextModes.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var next = nextModes.GetValueOrDefault(spec);
            var previous = currentModes.GetValueOrDefault(spec);
            if (!string.Equals(next, previous, StringComparison.Ordinal))
            {
                await _controller.SetDisplayModeAsync(spec, next, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
