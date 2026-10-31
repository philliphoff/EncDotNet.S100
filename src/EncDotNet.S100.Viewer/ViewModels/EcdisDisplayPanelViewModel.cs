using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Weather;
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
    private readonly DisplayModeToolbarViewModel? _displayModeToolbar;

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
        EcdisLabelOverrideProvider? labelOverrides = null,
        DisplayModeToolbarViewModel? displayModeToolbar = null,
        DisplayCombinationsViewModel? displayCombinations = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(datasets);

        _state = state;
        _catalogueManager = catalogueManager;
        _datasets = datasets;
        _labelOverrides = labelOverrides ?? new EcdisLabelOverrideProvider();
        _displayModeToolbar = displayModeToolbar;
        DisplayCombinations = displayCombinations;

        ResetAllOverridesCommand = new RelayCommand(() => _state.ClearAllOverrides());
        SetDisplayBaseCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.DisplayBase);
        SetStandardCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.Standard);
        SetOtherInformationCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.OtherInformation);
        SetAllCommand = new RelayCommand(() => ActiveCategory = EcdisDisplayCategory.All);

        _state.Changed += OnStateChanged;
        _state.NavigationalWarningsChanged += OnNavigationalWarningsChanged;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        _datasets.EntryPortrayalSpecChanged += OnEntryPortrayalSpecChanged;

        RebuildSpecs();
    }

    /// <summary>
    /// The display combinations shown in the panel's View section, above
    /// Display Category; <see langword="null"/> where they aren't wired.
    /// </summary>
    public DisplayCombinationsViewModel? DisplayCombinations { get; }

    /// <summary>True when the panel shows its View section.</summary>
    public bool HasDisplayCombinations => DisplayCombinations is not null;

    /// <summary>
    /// True when the panel is otherwise empty but still shows its View
    /// section, above the empty state: a view chosen now applies to what
    /// loads later.
    /// </summary>
    public bool ShowsViewAboveEmptyState => IsEmpty && HasDisplayCombinations;

    /// <summary>Per-spec sections shown in the panel.</summary>
    public ObservableCollection<EcdisSpecViewModel> Specs { get; } = new();

    /// <summary>
    /// The shared S-411 sea-ice display-mode selector, surfaced so the panel
    /// can host the same reusable selector shown in the map Display Settings
    /// flyout. Null in unit-test contexts where it is not injected; the view
    /// binds defensively (the selector self-hides when absent).
    /// </summary>
    public DisplayModeToolbarViewModel? DisplayModeToolbar => _displayModeToolbar;

    /// <summary>
    /// True when there is nothing to control: no loaded vector spec and no
    /// weather dataset.
    /// </summary>
    public bool IsEmpty => Specs.Count == 0 && !HasWeatherData;

    /// <summary>
    /// Whether a GRIB2 or NetCDF weather dataset is loaded, which shows the
    /// Weather section (#1072), as the per-spec sections show only for loaded
    /// specs. The switches apply to every weather dataset and are kept with
    /// the display settings whether or not one is loaded.
    /// </summary>
    public bool HasWeatherData { get; private set; }

    /// <summary>
    /// The Weather section's Isobars switch: mean sea level pressure isobars
    /// on every weather dataset with pressure. On by default
    /// (<see cref="PressureIsobarStyle"/>).
    /// </summary>
    public bool IsIsobarsVisible
    {
        get => GetWeatherLayer(PressureIsobarStyle.Key);
        set => _state.SetLayerVisible(PressureIsobarStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Wind speed fill switch: the Beaufort fill on
    /// every weather dataset with wind. Off by default
    /// (<see cref="WindSpeedStyle"/>).
    /// </summary>
    public bool IsWindSpeedFillVisible
    {
        get => GetWeatherLayer(WindSpeedStyle.Key);
        set => _state.SetLayerVisible(WindSpeedStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Wave arrows switch: wave direction arrows on
    /// every weather dataset with wave direction. Off by default
    /// (<see cref="WaveDirectionStyle"/>).
    /// </summary>
    public bool IsWaveArrowsVisible
    {
        get => GetWeatherLayer(WaveDirectionStyle.Key);
        set => _state.SetLayerVisible(WaveDirectionStyle.Key, value);
    }

    // The switch, or the style's own default when the mariner hasn't set it.
    private bool GetWeatherLayer(string layerKey) =>
        _state.GetLayerVisible(layerKey) ?? WeatherDefaults[layerKey];

    private static readonly IReadOnlyDictionary<string, bool> WeatherDefaults =
        WeatherCoverageStyle.CreateAll().ToDictionary(s => s.LayerKey, s => s.IsVisibleByDefault, StringComparer.Ordinal);

    private static readonly HashSet<string> WeatherSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        WeatherProducts.Grib2.Spec, WeatherProducts.NetCdf.Spec,
    };

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

    /// <summary>
    /// The Navigational warnings switch: whether the map draws S-124 warnings
    /// at all (S-98 Edition 2.0.0 §6.1.1, §12.12.2; #995). It overrides each
    /// warning dataset's own visibility, which keeps its state.
    /// </summary>
    public bool IsNavigationalWarningsVisible
    {
        get => _state.ShowNavigationalWarnings;
        set => _state.SetShowNavigationalWarnings(value);
    }

    private void OnNavigationalWarningsChanged() => OnPropertyChanged(nameof(IsNavigationalWarningsVisible));

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsIsobarsVisible));
        OnPropertyChanged(nameof(IsWindSpeedFillVisible));
        OnPropertyChanged(nameof(IsWaveArrowsVisible));
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

    private void OnEntryPortrayalSpecChanged(object? sender, EventArgs e)
    {
        RebuildSpecs();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSpecs();
    }

    private void RebuildSpecs()
    {
        var hasWeather = _datasets.Entries.Any(e => WeatherSpecs.Contains(e.ProductSpec));
        if (hasWeather != HasWeatherData)
        {
            HasWeatherData = hasWeather;
            OnPropertyChanged(nameof(HasWeatherData));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowsViewAboveEmptyState));
        }

        // Determine which portrayal specs are currently loaded. An S-57 entry
        // is portrayed as S-101 (maritime) or S-401 (inland), so it shares that
        // product's control group — key on the entry's portrayal spec before
        // de-duplicating.
        var loadedSpecs = _datasets.Entries
            .Select(e => e.PortrayalSpec)
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
        OnPropertyChanged(nameof(ShowsViewAboveEmptyState));
    }

    public void Dispose()
    {
        _state.Changed -= OnStateChanged;
        _state.NavigationalWarningsChanged -= OnNavigationalWarningsChanged;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        _datasets.EntryPortrayalSpecChanged -= OnEntryPortrayalSpecChanged;
    }
}
