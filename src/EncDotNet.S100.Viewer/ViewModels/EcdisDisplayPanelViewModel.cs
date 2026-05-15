using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;
using DisplayPlane = EncDotNet.S100.Pipelines.Vector.DisplayPlane;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Backs the ECDIS Display Controls panel in the activity bar.
/// Shows a per-spec tree of viewing-group checkboxes sourced from
/// each loaded vector spec's portrayal catalogue.
/// </summary>
internal sealed class EcdisDisplayPanelViewModel : ViewModelBase, IDisposable
{
    private readonly EcdisDisplayState _state;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly DatasetsViewModel _datasets;
    private readonly EcdisLabelOverrideProvider _labelOverrides;

    /// <summary>
    /// Spec codes for coverage products that have no viewing-group
    /// concept and should not appear in the ECDIS panel.
    /// </summary>
    private static readonly HashSet<string> CoverageSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-102", "S-104", "S-111",
    };

    public EcdisDisplayPanelViewModel(
        EcdisDisplayState state,
        PortrayalCatalogueManager catalogueManager,
        DatasetsViewModel datasets,
        EcdisLabelOverrideProvider? labelOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(datasets);

        _state = state;
        _catalogueManager = catalogueManager;
        _datasets = datasets;
        _labelOverrides = labelOverrides ?? new EcdisLabelOverrideProvider();

        ResetAllOverridesCommand = new RelayCommand(() => _state.ClearAllOverrides());
        SetDisplayBaseCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.DisplayBase);
        SetStandardCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.Standard);
        SetOtherInformationCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.OtherInformation);
        SetAllCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.All);

        _state.Changed += OnStateChanged;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;

        RebuildSpecs();
    }

    /// <summary>Per-spec sections shown in the panel.</summary>
    public ObservableCollection<EcdisSpecViewModel> Specs { get; } = new();

    /// <summary>True when there are no loaded vector specs.</summary>
    public bool IsEmpty => Specs.Count == 0;

    /// <summary>Clears all overrides across every spec.</summary>
    public ICommand ResetAllOverridesCommand { get; }

    /// <summary>Sets the display category to Display Base.</summary>
    public ICommand SetDisplayBaseCommand { get; }

    /// <summary>Sets the display category to Standard.</summary>
    public ICommand SetStandardCommand { get; }

    /// <summary>Sets the display category to Other Information.</summary>
    public ICommand SetOtherInformationCommand { get; }

    /// <summary>Sets the display category to All.</summary>
    public ICommand SetAllCommand { get; }

    /// <summary>
    /// The active display category, exposed for binding the category
    /// radio group at the top of the panel.
    /// </summary>
    public EcdisDisplayCategory ActiveCategory
    {
        get => _state.Category;
        set
        {
            if (_state.Category != value)
                _state.SetCategory(value);
        }
    }

    public bool IsDisplayBase => _state.Category == EcdisDisplayCategory.DisplayBase;
    public bool IsStandard => _state.Category == EcdisDisplayCategory.Standard;
    public bool IsOtherInformation => _state.Category == EcdisDisplayCategory.OtherInformation;
    public bool IsAll => _state.Category == EcdisDisplayCategory.All;

    /// <summary>
    /// Whether the Under Radar display plane is visible.
    /// </summary>
    public bool IsUnderRadarVisible
    {
        get => !_state.GetHiddenDisplayPlanes().Contains(DisplayPlane.UnderRadar);
        set
        {
            if (value)
                _state.ShowDisplayPlane(DisplayPlane.UnderRadar);
            else
                _state.HideDisplayPlane(DisplayPlane.UnderRadar);

            Telemetry.DisplayPlaneToggled.Add(1,
                new KeyValuePair<string, object?>("s100.displayplane", nameof(DisplayPlane.UnderRadar)),
                new KeyValuePair<string, object?>("s100.visible", value));

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the Over Radar display plane is visible.
    /// </summary>
    public bool IsOverRadarVisible
    {
        get => !_state.GetHiddenDisplayPlanes().Contains(DisplayPlane.OverRadar);
        set
        {
            if (value)
                _state.ShowDisplayPlane(DisplayPlane.OverRadar);
            else
                _state.HideDisplayPlane(DisplayPlane.OverRadar);

            Telemetry.DisplayPlaneToggled.Add(1,
                new KeyValuePair<string, object?>("s100.displayplane", nameof(DisplayPlane.OverRadar)),
                new KeyValuePair<string, object?>("s100.visible", value));

            OnPropertyChanged();
        }
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(ActiveCategory));
        OnPropertyChanged(nameof(IsDisplayBase));
        OnPropertyChanged(nameof(IsStandard));
        OnPropertyChanged(nameof(IsOtherInformation));
        OnPropertyChanged(nameof(IsAll));
        OnPropertyChanged(nameof(IsUnderRadarVisible));
        OnPropertyChanged(nameof(IsOverRadarVisible));
        foreach (var spec in Specs)
            spec.Refresh();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSpecs();
    }

    private void RebuildSpecs()
    {
        // Determine which vector specs are currently loaded
        var loadedSpecs = _datasets.Entries
            .Select(e => e.ProductSpec)
            .Where(s => !CoverageSpecs.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Only rebuild if the set of specs actually changed
        var existing = Specs.Select(s => s.SpecCode).ToList();
        if (existing.SequenceEqual(loadedSpecs, StringComparer.OrdinalIgnoreCase))
            return;

        Specs.Clear();
        foreach (var spec in loadedSpecs)
        {
            if (!_catalogueManager.HasCatalogue(spec)) continue;
            try
            {
                var provider = _catalogueManager.GetProvider(spec);
                var catalogue = provider.Catalogue;
                if (catalogue.ViewingGroups.Count > 0)
                {
                    Specs.Add(new EcdisSpecViewModel(_state, spec, catalogue, _labelOverrides));
                }
            }
            catch
            {
                // If the catalogue can't be loaded, skip the spec
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        _state.Changed -= OnStateChanged;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
    }
}
