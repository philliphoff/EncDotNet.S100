using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Backs the per-dataset "Ice display mode" selector. Surfaces the
/// alternative S-100 Part 9 §11.7 portrayal modes a loaded dataset declares
/// (S-411 sea ice offers concentration, stage-of-development and a
/// provisional navigational preview) and lets the mariner switch between
/// them. The selection is routed through
/// <see cref="EcdisDisplayState.SetDisplayMode"/>, which the dataset loader
/// observes to re-render.
/// </summary>
/// <remarks>
/// This is a <em>separate axis</em> from the ECDIS display category
/// (DisplayBase / Standard / OtherInformation / All): a mode here selects an
/// alternate portrayal over the same data rather than a viewing-group
/// filter. The selector is only enabled/visible when a loaded dataset
/// declares more than one mode (effectively S-411 today); every other
/// product hides it. The mode ids come from the loaded processor's
/// <see cref="IDisplayModeAwareDatasetProcessor.DeclaredDisplayModeIds"/>, so
/// the view model stays product-agnostic — only the friendly labels and the
/// provisional marker are S-411 aware, via <see cref="S411DisplayModes"/>.
/// </remarks>
internal sealed class DisplayModeToolbarViewModel : ViewModelBase, IDisposable
{
    private readonly EcdisDisplayState _state;
    private readonly IDatasetLoaderService _datasetLoader;
    private string? _activeSpec;

    public DisplayModeToolbarViewModel(
        EcdisDisplayState state,
        IDatasetLoaderService datasetLoader)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(datasetLoader);

        _state = state;
        _datasetLoader = datasetLoader;

        _state.Changed += OnStateChanged;
        _datasetLoader.DatasetLoaded += OnDatasetsChanged;
        _datasetLoader.DatasetRemoved += OnDatasetsChanged;

        Rebuild();
    }

    /// <summary>The selectable display-mode options for the active spec.</summary>
    public ObservableCollection<DisplayModeOption> Options { get; } = new();

    /// <summary>Localized header for the selector section.</summary>
    public string Label => Strings.DisplayMode_Header;

    /// <summary>
    /// Whether the selector should be shown (the active dataset declares
    /// more than one display mode). Bound to the section's visibility.
    /// </summary>
    public bool IsVisible => _activeSpec is not null && Options.Count > 1;

    /// <summary>Alias of <see cref="IsVisible"/> for interactivity gating.</summary>
    public bool IsEnabled => IsVisible;

    /// <summary>
    /// Re-scans loaded datasets for the first one that declares more than
    /// one display mode and rebuilds <see cref="Options"/>.
    /// </summary>
    private void Rebuild()
    {
        _activeSpec = null;
        Options.Clear();

        using var processors = _datasetLoader.AcquireProcessors();
        foreach (var kv in processors)
        {
            if (kv.Value is IDisplayModeAwareDatasetProcessor aware
                && aware.DeclaredDisplayModeIds.Count > 1)
            {
                _activeSpec = kv.Value.Spec.Name;
                foreach (var id in OrderModes(aware.DeclaredDisplayModeIds))
                {
                    var modeId = id;
                    Options.Add(new DisplayModeOption(
                        modeId,
                        LabelFor(modeId),
                        DescriptionFor(modeId),
                        TooltipFor(modeId),
                        S411DisplayModes.IsProvisional(modeId),
                        new RelayCommand(() => Select(modeId))));
                }
                break;
            }
        }

        UpdateSelection();
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>
    /// Marks the option matching the active selection (or the default mode
    /// when none is explicitly selected) as checked.
    /// </summary>
    private void UpdateSelection()
    {
        if (_activeSpec is null)
            return;

        var selected = _state.GetDisplayMode(_activeSpec);
        if (string.IsNullOrEmpty(selected))
        {
            selected = Options.Any(o => string.Equals(o.Id, S411DisplayModes.DefaultModeId, StringComparison.Ordinal))
                ? S411DisplayModes.DefaultModeId
                : Options.FirstOrDefault()?.Id;
        }

        foreach (var option in Options)
            option.IsSelected = string.Equals(option.Id, selected, StringComparison.Ordinal);
    }

    private void Select(string modeId)
    {
        if (_activeSpec is null)
            return;
        _state.SetDisplayMode(_activeSpec, modeId);
    }

    /// <summary>
    /// Orders declared mode ids for a stable, intuitive UI sequence:
    /// concentration, stage of development, navigational, then any others
    /// alphabetically.
    /// </summary>
    private static IEnumerable<string> OrderModes(IEnumerable<string> ids)
    {
        int Rank(string id) => id switch
        {
            S411DisplayModes.ConcentrationModeId => 0,
            S411DisplayModes.StageOfDevelopmentModeId => 1,
            S411DisplayModes.NavigationalModeId => 2,
            _ => 3,
        };

        return ids.OrderBy(Rank).ThenBy(id => id, StringComparer.Ordinal);
    }

    private static string LabelFor(string modeId) => modeId switch
    {
        S411DisplayModes.ConcentrationModeId => Strings.DisplayMode_Concentration,
        S411DisplayModes.StageOfDevelopmentModeId => Strings.DisplayMode_StageOfDevelopment,
        S411DisplayModes.NavigationalModeId => Strings.DisplayMode_Navigational,
        _ => modeId,
    };

    private static string DescriptionFor(string modeId) => modeId switch
    {
        S411DisplayModes.ConcentrationModeId => Strings.DisplayMode_Concentration_Description,
        S411DisplayModes.StageOfDevelopmentModeId => Strings.DisplayMode_StageOfDevelopment_Description,
        S411DisplayModes.NavigationalModeId => Strings.DisplayMode_Navigational_Description,
        _ => string.Empty,
    };

    private static string TooltipFor(string modeId) => modeId switch
    {
        S411DisplayModes.ConcentrationModeId => Strings.Tooltip_DisplayMode_Concentration,
        S411DisplayModes.StageOfDevelopmentModeId => Strings.Tooltip_DisplayMode_StageOfDevelopment,
        S411DisplayModes.NavigationalModeId => Strings.Tooltip_DisplayMode_Navigational,
        _ => modeId,
    };

    private void OnStateChanged() => UpdateSelection();

    private void OnDatasetsChanged(DatasetEntry entry) => Rebuild();

    public void Dispose()
    {
        _state.Changed -= OnStateChanged;
        _datasetLoader.DatasetLoaded -= OnDatasetsChanged;
        _datasetLoader.DatasetRemoved -= OnDatasetsChanged;
    }
}

/// <summary>
/// A single selectable display-mode option shown in the selector. Exposes a
/// localized <see cref="Label"/>, a one-line <see cref="Description"/>, a
/// <see cref="Tooltip"/>, whether the mode is a provisional preview, its
/// checked state, and the command that activates it.
/// </summary>
internal sealed class DisplayModeOption : ViewModelBase
{
    private bool _isSelected;

    public DisplayModeOption(string id, string label, string description, string tooltip, bool isProvisional, ICommand selectCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
        Description = description;
        Tooltip = tooltip;
        IsProvisional = isProvisional;
        SelectCommand = selectCommand;
    }

    /// <summary>The spec-native display-mode id.</summary>
    public string Id { get; }

    /// <summary>Localized, human-readable label.</summary>
    public string Label { get; }

    /// <summary>Localized one-line description shown beneath the label.</summary>
    public string Description { get; }

    /// <summary>Localized tooltip (provisional wording for the navigational mode).</summary>
    public string Tooltip { get; }

    /// <summary>Whether this mode is a provisional preview.</summary>
    public bool IsProvisional { get; }

    /// <summary>Activates this mode when invoked.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>Whether this option is the active selection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
