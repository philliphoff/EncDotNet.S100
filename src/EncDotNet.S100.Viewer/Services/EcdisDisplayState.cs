using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Cross-spec ECDIS display state singleton owned by the viewer.
/// Tracks the active <see cref="EcdisDisplayCategory"/> and the
/// per-spec set of viewing-group ids the user has explicitly hidden.
/// Mutations raise <see cref="Changed"/>; the dataset loader
/// subscribes and re-renders every loaded vector dataset.
/// </summary>
internal sealed class EcdisDisplayState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<int>> _hidden =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DisplayPlane> _hiddenPlanes = new();
    private readonly Dictionary<string, string?> _displayModes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _layerVisibility = new(StringComparer.Ordinal);
    private EcdisDisplayCategory _category = EcdisDisplayCategory.Standard;
    private bool _showNavigationalWarnings = true;

    /// <summary>Raised after any mutation completes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised after <see cref="ShowNavigationalWarnings"/> changes. Not
    /// <see cref="Changed"/>: the switch only hides S-124 parts, which
    /// <c>NavigationalWarningDrawingService</c> does without re-rendering
    /// every vector dataset.
    /// </summary>
    public event Action? NavigationalWarningsChanged;

    /// <summary>
    /// Whether the map draws S-124 navigational warnings at all (S-98
    /// Edition 2.0.0 §6.1.1, §12.12.2; #995). On by default. Off hides every
    /// part, affected area and text placement, and the pick highlight of a
    /// warning, whatever each warning's status, its open card or its
    /// dataset's own visibility say. The Navigational warnings panel and the
    /// Warning cards still list and open them.
    /// </summary>
    public bool ShowNavigationalWarnings
    {
        get { lock (_gate) return _showNavigationalWarnings; }
    }

    /// <summary>
    /// Sets <see cref="ShowNavigationalWarnings"/> and raises
    /// <see cref="NavigationalWarningsChanged"/> when it differs.
    /// </summary>
    public void SetShowNavigationalWarnings(bool show)
    {
        bool changed;
        lock (_gate)
        {
            changed = _showNavigationalWarnings != show;
            _showNavigationalWarnings = show;
        }
        if (changed) NavigationalWarningsChanged?.Invoke();
    }

    /// <summary>
    /// The active ECDIS standard display category (defaults to Standard).
    /// </summary>
    public EcdisDisplayCategory Category
    {
        get { lock (_gate) return _category; }
    }

    /// <summary>
    /// Sets <see cref="Category"/> and raises <see cref="Changed"/>
    /// when the value differs from the current category.
    /// </summary>
    public void SetCategory(EcdisDisplayCategory category)
    {
        bool changed;
        lock (_gate)
        {
            changed = _category != category;
            _category = category;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Hides the given viewing-group id for <paramref name="productSpec"/>.
    /// </summary>
    public void HideViewingGroup(string productSpec, int viewingGroupId)
    {
        ArgumentNullException.ThrowIfNull(productSpec);

        bool changed;
        lock (_gate)
        {
            if (!_hidden.TryGetValue(productSpec, out var set))
            {
                set = new HashSet<int>();
                _hidden[productSpec] = set;
            }
            changed = set.Add(viewingGroupId);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Removes any explicit hide for the given viewing-group id under
    /// <paramref name="productSpec"/>.
    /// </summary>
    public void ShowViewingGroup(string productSpec, int viewingGroupId)
    {
        ArgumentNullException.ThrowIfNull(productSpec);

        bool changed;
        lock (_gate)
        {
            if (!_hidden.TryGetValue(productSpec, out var set))
                return;
            changed = set.Remove(viewingGroupId);
            if (set.Count == 0) _hidden.Remove(productSpec);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Hides a display plane (S-100 Part 9 §11.6).
    /// </summary>
    public void HideDisplayPlane(DisplayPlane plane)
    {
        bool changed;
        lock (_gate)
        {
            changed = _hiddenPlanes.Add(plane);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Shows a previously hidden display plane.
    /// </summary>
    public void ShowDisplayPlane(DisplayPlane plane)
    {
        bool changed;
        lock (_gate)
        {
            changed = _hiddenPlanes.Remove(plane);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Returns the set of hidden display planes.
    /// </summary>
    public IReadOnlySet<DisplayPlane> GetHiddenDisplayPlanes()
    {
        lock (_gate)
        {
            return new HashSet<DisplayPlane>(_hiddenPlanes);
        }
    }

    /// <summary>
    /// Sets the explicit S-100 Part 9 §11.7 display-mode selection for
    /// <paramref name="productSpec"/> (e.g. an S-411 concentration /
    /// stage-of-development / navigational choice). A <c>null</c> or empty
    /// <paramref name="modeId"/> clears the selection so the spec's
    /// portrayal catalogue renders its default mode. Raises
    /// <see cref="Changed"/> when the effective selection differs.
    /// </summary>
    public void SetDisplayMode(string productSpec, string? modeId)
    {
        ArgumentNullException.ThrowIfNull(productSpec);

        bool changed;
        lock (_gate)
        {
            _displayModes.TryGetValue(productSpec, out var current);
            if (string.IsNullOrEmpty(modeId))
            {
                changed = _displayModes.Remove(productSpec);
            }
            else
            {
                changed = !string.Equals(current, modeId, StringComparison.Ordinal);
                _displayModes[productSpec] = modeId;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Returns the explicit display-mode selection for
    /// <paramref name="productSpec"/>, or <c>null</c> when the spec uses its
    /// default mode.
    /// </summary>
    public string? GetDisplayMode(string productSpec)
    {
        ArgumentNullException.ThrowIfNull(productSpec);
        lock (_gate)
        {
            return _displayModes.TryGetValue(productSpec, out var id) ? id : null;
        }
    }

    /// <summary>
    /// The map-wide switch for an optional product layer, such as the
    /// weather isobars (<c>"weather.pressure.isobars"</c>; #1072), or
    /// <see langword="null"/> when it is unset and the layer follows its
    /// producer's default.
    /// </summary>
    /// <param name="layerKey">The processor's stable sub-layer key.</param>
    public bool? GetLayerVisible(string layerKey)
    {
        ArgumentNullException.ThrowIfNull(layerKey);
        lock (_gate)
        {
            return _layerVisibility.TryGetValue(layerKey, out var visible) ? visible : null;
        }
    }

    /// <summary>
    /// Sets the map-wide switch for an optional product layer and raises
    /// <see cref="Changed"/> when it differs, so every dataset of the
    /// product re-renders with it (<see cref="EcdisDisplaySettings.LayerVisibility"/>).
    /// </summary>
    /// <param name="layerKey">The processor's stable sub-layer key.</param>
    /// <param name="visible">Whether the layer is drawn.</param>
    public void SetLayerVisible(string layerKey, bool visible)
    {
        ArgumentNullException.ThrowIfNull(layerKey);
        bool changed;
        lock (_gate)
        {
            changed = !_layerVisibility.TryGetValue(layerKey, out var current) || current != visible;
            _layerVisibility[layerKey] = visible;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Clears every per-spec viewing-group override and raises
    /// <see cref="Changed"/> if anything was cleared.
    /// </summary>
    public void ClearAllOverrides()
    {
        bool changed;
        lock (_gate)
        {
            changed = _hidden.Count > 0;
            _hidden.Clear();
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Clears the hidden set for a single spec and raises
    /// <see cref="Changed"/> when something was cleared.
    /// </summary>
    public void ClearOverridesForSpec(string productSpec)
    {
        ArgumentNullException.ThrowIfNull(productSpec);
        bool changed;
        lock (_gate)
        {
            changed = _hidden.Remove(productSpec);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Returns the hidden viewing-group ids for <paramref name="productSpec"/>
    /// (empty when the spec has no overrides).
    /// </summary>
    public IReadOnlySet<int> GetHidden(string productSpec)
    {
        ArgumentNullException.ThrowIfNull(productSpec);
        lock (_gate)
        {
            return _hidden.TryGetValue(productSpec, out var set)
                ? new HashSet<int>(set)
                : (IReadOnlySet<int>)new HashSet<int>();
        }
    }

    /// <summary>
    /// Builds an <see cref="EcdisDisplaySettings"/> snapshot suitable
    /// for attaching to a <see cref="RenderContext"/>. The snapshot
    /// is detached from this state, so subsequent mutations do not
    /// affect in-flight renders.
    /// </summary>
    public EcdisDisplaySettings Snapshot()
    {
        lock (_gate)
        {
            var copy = _hidden.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlySet<int>)new HashSet<int>(kv.Value),
                StringComparer.OrdinalIgnoreCase);
            return new EcdisDisplaySettings
            {
                Category = _category,
                HiddenViewingGroups = copy,
                HiddenDisplayPlanes = new HashSet<DisplayPlane>(_hiddenPlanes),
                ActiveDisplayModes = new Dictionary<string, string?>(
                    _displayModes, StringComparer.OrdinalIgnoreCase),
                LayerVisibility = new Dictionary<string, bool>(_layerVisibility, StringComparer.Ordinal),
            };
        }
    }

    /// <summary>
    /// Applies the display part of a display combination in one step and
    /// raises <see cref="Changed"/> once when anything differs. Only the
    /// values given change: hidden display planes and the navigational-warnings
    /// switch are never touched, and a viewing group the combination doesn't
    /// name keeps its state.
    /// </summary>
    /// <param name="category">The category to set, or <see langword="null"/> to keep the current one.</param>
    /// <param name="hide">Viewing-group ids to hide, per product.</param>
    /// <param name="show">Viewing-group ids to show, per product.</param>
    /// <param name="displayModes">Display-mode ids to select, per product.</param>
    /// <param name="layerVisibility">Map-wide optional-layer switches to set.</param>
    public void ApplyPicture(
        EcdisDisplayCategory? category,
        IReadOnlyDictionary<string, IReadOnlySet<int>> hide,
        IReadOnlyDictionary<string, IReadOnlySet<int>> show,
        IReadOnlyDictionary<string, string> displayModes,
        IReadOnlyDictionary<string, bool> layerVisibility)
    {
        ArgumentNullException.ThrowIfNull(hide);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(displayModes);
        ArgumentNullException.ThrowIfNull(layerVisibility);

        var changed = false;
        lock (_gate)
        {
            if (category is { } c && _category != c)
            {
                _category = c;
                changed = true;
            }
            foreach (var (spec, ids) in hide)
            {
                if (ids.Count == 0) continue;
                if (!_hidden.TryGetValue(spec, out var set))
                {
                    set = new HashSet<int>();
                    _hidden[spec] = set;
                }
                foreach (var id in ids)
                    changed |= set.Add(id);
            }
            foreach (var (spec, ids) in show)
            {
                if (!_hidden.TryGetValue(spec, out var set)) continue;
                foreach (var id in ids)
                    changed |= set.Remove(id);
                if (set.Count == 0) _hidden.Remove(spec);
            }
            foreach (var (spec, mode) in displayModes)
            {
                if (string.IsNullOrEmpty(mode)) continue;
                _displayModes.TryGetValue(spec, out var current);
                if (string.Equals(current, mode, StringComparison.Ordinal)) continue;
                _displayModes[spec] = mode;
                changed = true;
            }
            foreach (var (key, visible) in layerVisibility)
            {
                if (_layerVisibility.TryGetValue(key, out var current) && current == visible) continue;
                _layerVisibility[key] = visible;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Replaces the entire state from a hydrated settings record
    /// (used on viewer startup to restore the persisted category,
    /// per-spec overrides, explicit display-mode selections and the
    /// navigational-warnings switch). Raises <see cref="Changed"/> exactly
    /// once after the swap, and <see cref="NavigationalWarningsChanged"/>
    /// too.
    /// </summary>
    public void Hydrate(
        EcdisDisplayCategory category,
        IReadOnlyDictionary<string, IReadOnlySet<int>> hidden,
        IReadOnlySet<DisplayPlane>? hiddenPlanes = null,
        IReadOnlyDictionary<string, string?>? displayModes = null,
        bool showNavigationalWarnings = true,
        IReadOnlyDictionary<string, bool>? layerVisibility = null)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        lock (_gate)
        {
            _category = category;
            _showNavigationalWarnings = showNavigationalWarnings;
            _hidden.Clear();
            foreach (var kv in hidden)
                _hidden[kv.Key] = new HashSet<int>(kv.Value);
            _hiddenPlanes.Clear();
            if (hiddenPlanes is not null)
            {
                foreach (var p in hiddenPlanes)
                    _hiddenPlanes.Add(p);
            }
            _layerVisibility.Clear();
            if (layerVisibility is not null)
            {
                foreach (var kv in layerVisibility)
                    _layerVisibility[kv.Key] = kv.Value;
            }
            _displayModes.Clear();
            if (displayModes is not null)
            {
                foreach (var kv in displayModes)
                {
                    if (!string.IsNullOrEmpty(kv.Value))
                        _displayModes[kv.Key] = kv.Value;
                }
            }
        }
        Changed?.Invoke();
        NavigationalWarningsChanged?.Invoke();
    }
}
