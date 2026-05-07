using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;

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
    private EcdisDisplayCategory _category = EcdisDisplayCategory.Standard;

    /// <summary>Raised after any mutation completes.</summary>
    public event Action? Changed;

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
            };
        }
    }

    /// <summary>
    /// Replaces the entire state from a hydrated settings record
    /// (used on viewer startup to restore the persisted category and
    /// per-spec overrides). Raises <see cref="Changed"/> exactly
    /// once after the swap.
    /// </summary>
    public void Hydrate(EcdisDisplayCategory category, IReadOnlyDictionary<string, IReadOnlySet<int>> hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        lock (_gate)
        {
            _category = category;
            _hidden.Clear();
            foreach (var kv in hidden)
                _hidden[kv.Key] = new HashSet<int>(kv.Value);
        }
        Changed?.Invoke();
    }
}
