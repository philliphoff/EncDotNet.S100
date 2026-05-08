using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Backs the "Text ▾" toolbar pill. Exposes three checkboxes that
/// toggle S-101 text viewing-group layers via the existing per-spec
/// hidden-VG override map in <see cref="EcdisDisplayState"/>.
/// </summary>
/// <remarks>
/// For specs that do not declare text viewing-group layers the
/// toggles are disabled (greyed out). No new state types are
/// introduced — the toggles manipulate the same
/// <see cref="EcdisDisplayState.HideViewingGroup"/> /
/// <see cref="EcdisDisplayState.ShowViewingGroup"/> API used by the
/// ECDIS panel checkboxes.
/// </remarks>
internal sealed class TextGroupToolbarViewModel : ViewModelBase, IDisposable
{
    private readonly EcdisDisplayState _state;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly DatasetsViewModel _datasets;

    /// <summary>Cached VG ids per text group for the primary spec.</summary>
    private readonly Dictionary<TextGroup, IReadOnlySet<int>> _resolvedGroups = new();
    private string? _activeSpec;

    public TextGroupToolbarViewModel(
        EcdisDisplayState state,
        PortrayalCatalogueManager catalogueManager,
        DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(datasets);

        _state = state;
        _catalogueManager = catalogueManager;
        _datasets = datasets;
        _state.Changed += OnStateChanged;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;

        ToggleImportantCommand = new RelayCommand(() => Toggle(TextGroup.Important));
        ToggleOtherCommand = new RelayCommand(() => Toggle(TextGroup.Other));
        ToggleAllCommand = new RelayCommand(() => Toggle(TextGroup.All));

        RebuildFromLoadedSpecs();
    }

    public ICommand ToggleImportantCommand { get; }
    public ICommand ToggleOtherCommand { get; }
    public ICommand ToggleAllCommand { get; }

    /// <summary>Localized label for the toolbar pill.</summary>
    public string Label => Strings.Toolbar_Text;

    public bool IsImportantVisible => IsGroupVisible(TextGroup.Important);
    public bool IsOtherVisible => IsGroupVisible(TextGroup.Other);
    public bool IsAllVisible => IsGroupVisible(TextGroup.All);

    /// <summary>
    /// Whether the pill is enabled (at least one loaded spec has
    /// text layers).
    /// </summary>
    public bool IsEnabled => _resolvedGroups.Count > 0;

    /// <summary>
    /// Called when the loaded dataset set changes. Rebuilds the
    /// resolved VG mapping for the first spec that declares text
    /// layers.
    /// </summary>
    public void RefreshForSpec(string? specCode)
    {
        _resolvedGroups.Clear();
        _activeSpec = null;

        if (specCode is not null && _catalogueManager.HasCatalogue(specCode))
        {
            try
            {
                var provider = _catalogueManager.GetProvider(specCode);
                var catalogue = provider.Catalogue;
                if (TextGroupMapping.HasTextLayers(catalogue))
                {
                    _activeSpec = specCode;
                    foreach (var g in Enum.GetValues<TextGroup>())
                    {
                        _resolvedGroups[g] = TextGroupMapping.Resolve(g, catalogue);
                    }
                }
            }
            catch
            {
                // If the catalogue can't be loaded, disable the pill.
            }
        }

        OnPropertyChanged(nameof(IsEnabled));
        NotifyAll();
    }

    private bool IsGroupVisible(TextGroup group)
    {
        if (_activeSpec is null || !_resolvedGroups.TryGetValue(group, out var vgIds))
            return true;
        if (vgIds.Count == 0) return true;

        var hidden = _state.GetHidden(_activeSpec);
        // Group is visible when at least one of its VGs is not hidden.
        return vgIds.Any(id => !hidden.Contains(id));
    }

    private void Toggle(TextGroup group)
    {
        if (_activeSpec is null || !_resolvedGroups.TryGetValue(group, out var vgIds))
            return;

        bool currentlyVisible = IsGroupVisible(group);
        foreach (var id in vgIds)
        {
            if (currentlyVisible)
                _state.HideViewingGroup(_activeSpec, id);
            else
                _state.ShowViewingGroup(_activeSpec, id);
        }

        Telemetry.ViewingGroupToggled.Add(1,
            new KeyValuePair<string, object?>("s100.spec", _activeSpec),
            new KeyValuePair<string, object?>("s100.textgroup", group.ToString()));
    }

    private void OnStateChanged() => NotifyAll();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFromLoadedSpecs();
    }

    /// <summary>
    /// Scans loaded datasets and resolves text layers for the first
    /// vector spec that has them.
    /// </summary>
    private void RebuildFromLoadedSpecs()
    {
        // Find the first loaded vector spec that has text layers
        string? matchedSpec = null;
        foreach (var entry in _datasets.Entries)
        {
            var spec = entry.ProductSpec;
            if (_catalogueManager.HasCatalogue(spec))
            {
                try
                {
                    var provider = _catalogueManager.GetProvider(spec);
                    if (TextGroupMapping.HasTextLayers(provider.Catalogue))
                    {
                        matchedSpec = spec;
                        break;
                    }
                }
                catch { /* skip */ }
            }
        }

        RefreshForSpec(matchedSpec);
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsImportantVisible));
        OnPropertyChanged(nameof(IsOtherVisible));
        OnPropertyChanged(nameof(IsAllVisible));
    }

    public void Dispose()
    {
        _state.Changed -= OnStateChanged;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
    }
}
