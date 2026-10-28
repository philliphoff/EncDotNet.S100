using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Restores the <see cref="EcdisDisplayState"/> from <see cref="ViewerSettings"/>
/// on startup and saves every change back, so a crash doesn't lose the
/// mariner's ECDIS display preferences.
/// </summary>
internal static class EcdisDisplayStatePersistence
{
    /// <summary>
    /// Creates the state from <paramref name="settings"/> and keeps the
    /// settings up to date with it.
    /// </summary>
    /// <param name="settings">The viewer settings the state is kept in.</param>
    public static EcdisDisplayState Create(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var state = new EcdisDisplayState();

        // Seed the viewer's default viewing-group visibility once so
        // the noisy S-101 mariner-selector patterns (shallow water
        // pattern, survey accuracy/quality, low-accuracy marker)
        // start off even in the "All" category. Mariner choices made
        // afterwards persist and are never re-forced.
        if (EcdisDisplayDefaults.Apply(settings))
        {
            try { settings.Save(); } catch { /* best-effort */ }
        }

        var category = Enum.TryParse<EcdisDisplayCategory>(
            settings.EcdisDisplayCategory, ignoreCase: true, out var c)
            ? c
            : EcdisDisplayCategory.Standard;
        var hidden = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in settings.EcdisHiddenViewingGroups)
        {
            var ids = new HashSet<int>();
            foreach (var token in (kv.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    ids.Add(id);
            }
            if (ids.Count > 0) hidden[kv.Key] = ids;
        }
        // Hydrate hidden display planes
        var hiddenPlanes = new HashSet<DisplayPlane>();
        foreach (var token in (settings.EcdisHiddenDisplayPlanes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<DisplayPlane>(token, ignoreCase: true, out var plane))
                hiddenPlanes.Add(plane);
        }
        // Hydrate explicit per-spec display-mode selections (§11.7).
        var displayModes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in settings.EcdisActiveDisplayModes)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
                displayModes[kv.Key] = kv.Value;
        }
        state.Hydrate(category, hidden, hiddenPlanes.Count > 0 ? hiddenPlanes : null,
            displayModes.Count > 0 ? displayModes : null,
            settings.EcdisShowNavigationalWarnings,
            settings.EcdisLayerVisibility);

        // Persist on every change. Cheap because settings.json is small.
        state.Changed += () =>
        {
            settings.EcdisDisplayCategory = state.Category.ToString();
            var snap = state.Snapshot();
            settings.EcdisHiddenViewingGroups.Clear();
            foreach (var kv in snap.HiddenViewingGroups)
            {
                settings.EcdisHiddenViewingGroups[kv.Key] =
                    string.Join(",", kv.Value.OrderBy(i => i));
            }
            settings.EcdisHiddenDisplayPlanes =
                string.Join(",", snap.HiddenDisplayPlanes.OrderBy(p => p));
            settings.EcdisActiveDisplayModes.Clear();
            foreach (var kv in snap.ActiveDisplayModes)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                    settings.EcdisActiveDisplayModes[kv.Key] = kv.Value;
            }
            settings.EcdisLayerVisibility.Clear();
            foreach (var kv in snap.LayerVisibility)
                settings.EcdisLayerVisibility[kv.Key] = kv.Value;
            Save(settings);
        };
        state.NavigationalWarningsChanged += () =>
        {
            if (settings.EcdisShowNavigationalWarnings == state.ShowNavigationalWarnings)
                return;
            settings.EcdisShowNavigationalWarnings = state.ShowNavigationalWarnings;
            Save(settings);
        };
        return state;
    }

    private static void Save(ViewerSettings settings)
    {
        try { settings.Save(); } catch { /* best-effort */ }
    }
}
