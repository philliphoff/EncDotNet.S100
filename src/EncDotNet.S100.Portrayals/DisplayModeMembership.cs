using System.Globalization;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Resolves S-100 Part 9 §11.7 display-mode membership: walks
/// <c>displayMode → viewingGroupLayer → viewingGroup</c> and returns
/// the integer viewing-group ids that belong to a given mode.
/// </summary>
public static class DisplayModeMembership
{
    /// <summary>
    /// Resolves the integer viewing-group ids that are visible for the
    /// given display mode declared in <paramref name="catalogue"/>.
    /// Layer ids referenced by the mode but absent from the catalogue
    /// are skipped silently; viewing-group ids that don't parse as
    /// integers are skipped silently (a non-numeric id is meaningless
    /// to <c>DrawingInstruction.ViewingGroup</c>).
    /// </summary>
    /// <param name="catalogue">The parsed portrayal catalogue.</param>
    /// <param name="displayModeId">
    /// The id of the desired display mode (e.g. <c>"DisplayBase"</c>).
    /// Matched case-insensitively. If the mode is not declared the
    /// returned set is empty.
    /// </param>
    public static IReadOnlySet<int> Resolve(PortrayalCatalogue catalogue, string displayModeId)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentException.ThrowIfNullOrEmpty(displayModeId);

        var mode = catalogue.DisplayModes
            .FirstOrDefault(m => string.Equals(m.Id, displayModeId, StringComparison.OrdinalIgnoreCase));
        if (mode is null) return new HashSet<int>();

        var layerIndex = catalogue.ViewingGroupLayers
            .ToDictionary(l => l.Id, l => l, StringComparer.OrdinalIgnoreCase);

        var ids = new HashSet<int>();
        foreach (var layerId in mode.ViewingGroupLayerIds)
        {
            if (!layerIndex.TryGetValue(layerId, out var layer)) continue;
            foreach (var raw in layer.ViewingGroupIds)
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Wires <paramref name="displayModes"/> to keep
    /// <paramref name="viewingGroups"/> in sync with the active display
    /// mode declared in <paramref name="catalogue"/>. The wiring
    /// updates the viewing-group controller's mode membership only;
    /// any user overrides set via
    /// <see cref="ViewingGroupController.SetUserOverride"/> are
    /// preserved across mode changes.
    /// </summary>
    /// <returns>
    /// The handler that was attached to
    /// <see cref="DisplayModeController.Changed"/>, in case callers
    /// need to detach it later.
    /// </returns>
    public static Action Bind(
        DisplayModeController displayModes,
        ViewingGroupController viewingGroups,
        PortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(displayModes);
        ArgumentNullException.ThrowIfNull(viewingGroups);
        ArgumentNullException.ThrowIfNull(catalogue);

        var declaredIds = catalogue.DisplayModes
            .Select(m => m.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        displayModes.SetDeclaredModeIds(declaredIds);

        Action handler = () =>
        {
            var modeId = displayModes.ActiveDisplayModeId;

            // Treat a mode that isn't declared by this catalogue as
            // "no filter" rather than "empty membership" — picking
            // an undeclared mode (e.g. requesting DisplayBase on a
            // spec that only ships StandardDisplay) should fall back
            // to rendering every viewing group, matching the
            // catalogue authoring intent.
            if (modeId is null || !declaredIds.Contains(modeId))
            {
                viewingGroups.SetActiveModeMembership(null);
                return;
            }

            viewingGroups.SetActiveModeMembership(Resolve(catalogue, modeId));
        };

        displayModes.Changed += handler;
        // Apply current state immediately so first-bind catalogues
        // start in a consistent place.
        handler();
        return handler;
    }
}
