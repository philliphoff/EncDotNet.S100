using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
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
        EcdisLabelOverrideProvider labelOverrides,
        DisplayModeToolbarViewModel? displayModeToolbar,
        DisplayCombinationsViewModel? displayCombinations,
        AmlDisplaySectionViewModel? amlDisplay)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(labelOverrides);

        _state = state;
        _catalogueManager = catalogueManager;
        _datasets = datasets;
        _labelOverrides = labelOverrides;
        _displayModeToolbar = displayModeToolbar;
        DisplayCombinations = displayCombinations;
        Aml = amlDisplay;
        if (Aml is not null)
            Aml.PropertyChanged += OnAmlPropertyChanged;

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

    /// <summary>
    /// The Additional Military Layers section (AML handoff decision 24), below
    /// Display Planes; <see langword="null"/> where it isn't wired.
    /// </summary>
    public AmlDisplaySectionViewModel? Aml { get; }

    /// <summary>Whether the Additional Military Layers section shows: an AML cell is loaded.</summary>
    public bool HasAml => Aml?.IsShown == true;

    private void OnAmlPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AmlDisplaySectionViewModel.IsShown) or null or "")
        {
            OnPropertyChanged(nameof(HasAml));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowsViewAboveEmptyState));
        }
    }

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
    /// True when there is nothing to control: no loaded vector spec, no
    /// weather dataset, no AML cell and no S-102 bathymetry.
    /// </summary>
    public bool IsEmpty => Specs.Count == 0 && !HasWeatherData && !HasAml && !HasBathymetryData;

    /// <summary>
    /// Whether an S-102 bathymetry dataset is loaded, which shows the
    /// Bathymetry section. Its switch applies to every S-102 dataset and is
    /// kept with the display settings whether or not one is loaded.
    /// </summary>
    public bool HasBathymetryData { get; private set; }

    /// <summary>
    /// The Bathymetry section's Relief shading switch: a hillshade over the
    /// S-102 depth bands (<see cref="S102Relief"/>). Not part of the S-102
    /// portrayal, so off by default.
    /// </summary>
    public bool IsReliefShadingVisible
    {
        get => _state.GetLayerVisible(S102Relief.LayerKey) ?? false;
        set => _state.SetLayerVisible(S102Relief.LayerKey, value);
    }

    /// <summary>
    /// Whether a weather dataset is loaded (a GRIB2 or NetCDF grid, a tropical
    /// cyclone or a surface analysis), which shows the Weather section (#1072,
    /// V26), as the per-spec sections show only for loaded specs. The switches
    /// apply to every weather dataset and are kept with the display settings
    /// whether or not one is loaded.
    /// </summary>
    public bool HasWeatherData { get; private set; }

    /// <summary>
    /// Whether a tropical cyclone is loaded, which shows the Weather section's
    /// Wind radii, Past track and Forecast times switches (V26).
    /// </summary>
    public bool HasTropicalCyclone { get; private set; }

    /// <summary>
    /// The Weather section's Wind radii switch: the 34, 50 and 64 kt wind
    /// radii of every tropical cyclone. On by default (V26).
    /// </summary>
    public bool IsWindRadiiVisible
    {
        get => GetCycloneSwitch(TropicalCycloneStyle.WindRadiiKey);
        set => _state.SetLayerVisible(TropicalCycloneStyle.WindRadiiKey, value);
    }

    /// <summary>
    /// The Weather section's Past track switch: every tropical cyclone's
    /// best-track line and points. On by default (V26).
    /// </summary>
    public bool IsPastTrackVisible
    {
        get => GetCycloneSwitch(TropicalCycloneStyle.PastTrackKey);
        set => _state.SetLayerVisible(TropicalCycloneStyle.PastTrackKey, value);
    }

    /// <summary>
    /// The Weather section's Forecast times switch: the time and intensity
    /// beside each tropical cyclone's forecast points. On by default (V26).
    /// </summary>
    public bool IsForecastTimesVisible
    {
        get => GetCycloneSwitch(TropicalCycloneStyle.ForecastTimesKey);
        set => _state.SetLayerVisible(TropicalCycloneStyle.ForecastTimesKey, value);
    }

    // A cyclone switch, on unless the mariner turned it off.
    private bool GetCycloneSwitch(string key) => _state.GetLayerVisible(key) ?? true;

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

    /// <summary>
    /// The Weather section's Isotherms switch: sea surface temperature
    /// isotherms on every weather dataset with sea temperature. Off by
    /// default (<see cref="SeaTemperatureIsothermStyle"/>).
    /// </summary>
    public bool IsIsothermsVisible
    {
        get => GetWeatherLayer(SeaTemperatureIsothermStyle.Key);
        set => _state.SetLayerVisible(SeaTemperatureIsothermStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Currents switch: surface current arrows on every
    /// weather dataset with currents. On by default (<see cref="CurrentArrowStyle"/>).
    /// </summary>
    public bool IsCurrentArrowsVisible
    {
        get => GetWeatherLayer(CurrentArrowStyle.Key);
        set => _state.SetLayerVisible(CurrentArrowStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Air temperature switch: dashed 2 m air
    /// temperature isotherms on every weather dataset with air temperature.
    /// Off by default (<see cref="AirTemperatureIsothermStyle"/>).
    /// </summary>
    public bool IsAirTemperatureVisible
    {
        get => GetWeatherLayer(AirTemperatureIsothermStyle.Key);
        set => _state.SetLayerVisible(AirTemperatureIsothermStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Precipitation switch: the precipitation rate in
    /// rain radar colours on every weather dataset with it. Off by default
    /// (<see cref="PrecipitationStyle"/>).
    /// </summary>
    public bool IsPrecipitationVisible
    {
        get => GetWeatherLayer(PrecipitationStyle.Key);
        set => _state.SetLayerVisible(PrecipitationStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Thunderstorm risk switch: CAPE from 1000 J/kg on
    /// every weather dataset with it. Off by default (<see cref="ThunderstormRiskStyle"/>).
    /// </summary>
    public bool IsThunderstormRiskVisible
    {
        get => GetWeatherLayer(ThunderstormRiskStyle.Key);
        set => _state.SetLayerVisible(ThunderstormRiskStyle.Key, value);
    }

    /// <summary>
    /// The Weather section's Cloud cover switch: the total cloud cover veil
    /// on every weather dataset with it. Off by default (<see cref="CloudCoverStyle"/>).
    /// </summary>
    public bool IsCloudCoverVisible
    {
        get => GetWeatherLayer(CloudCoverStyle.Key);
        set => _state.SetLayerVisible(CloudCoverStyle.Key, value);
    }

    // The switch, or the style's own default when the mariner hasn't set it.
    private bool GetWeatherLayer(string layerKey) =>
        _state.GetLayerVisible(layerKey) ?? WeatherDefaults[layerKey];

    private static readonly IReadOnlyDictionary<string, bool> WeatherDefaults =
        WeatherCoverageStyle.CreateAll().ToDictionary(s => s.LayerKey, s => s.IsVisibleByDefault, StringComparer.Ordinal);

    private static readonly HashSet<string> WeatherSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        WeatherProducts.Grib2.Spec, WeatherProducts.NetCdf.Spec, WeatherProducts.TropicalCyclone.Spec, WeatherProducts.Codsus.Spec,
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
        OnPropertyChanged(nameof(IsIsothermsVisible));
        OnPropertyChanged(nameof(IsCurrentArrowsVisible));
        OnPropertyChanged(nameof(IsAirTemperatureVisible));
        OnPropertyChanged(nameof(IsPrecipitationVisible));
        OnPropertyChanged(nameof(IsThunderstormRiskVisible));
        OnPropertyChanged(nameof(IsCloudCoverVisible));
        OnPropertyChanged(nameof(IsWindRadiiVisible));
        OnPropertyChanged(nameof(IsPastTrackVisible));
        OnPropertyChanged(nameof(IsForecastTimesVisible));
        OnPropertyChanged(nameof(IsReliefShadingVisible));
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

        var hasBathymetry = _datasets.Entries.Any(e => string.Equals(e.ProductSpec, "S-102", StringComparison.OrdinalIgnoreCase));
        if (hasBathymetry != HasBathymetryData)
        {
            HasBathymetryData = hasBathymetry;
            OnPropertyChanged(nameof(HasBathymetryData));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowsViewAboveEmptyState));
        }

        var hasCyclone = _datasets.Entries.Any(e => string.Equals(e.ProductSpec, WeatherProducts.TropicalCyclone.Spec, StringComparison.OrdinalIgnoreCase));
        if (hasCyclone != HasTropicalCyclone)
        {
            HasTropicalCyclone = hasCyclone;
            OnPropertyChanged(nameof(HasTropicalCyclone));
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
        if (Aml is not null)
            Aml.PropertyChanged -= OnAmlPropertyChanged;
    }
}
