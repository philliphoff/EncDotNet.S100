using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Backs the compact display-category pill button overlaid on the map.
/// Shows the active <see cref="EcdisDisplayCategory"/> and exposes
/// commands to switch between the four categories.
/// </summary>
internal sealed class DisplayToolbarViewModel : ViewModelBase, IDisposable
{
    private readonly EcdisDisplayState _state;

    public DisplayToolbarViewModel(EcdisDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _state.Changed += OnStateChanged;

        SetDisplayBaseCommand = new RelayCommand(() => SetCategory(EcdisDisplayCategory.DisplayBase));
        SetStandardCommand = new RelayCommand(() => SetCategory(EcdisDisplayCategory.Standard));
        SetOtherInformationCommand = new RelayCommand(() => SetCategory(EcdisDisplayCategory.OtherInformation));
        SetAllCommand = new RelayCommand(() => SetCategory(EcdisDisplayCategory.All));
    }

    /// <summary>Sets the display category to Display Base.</summary>
    public ICommand SetDisplayBaseCommand { get; }

    /// <summary>Sets the display category to Standard.</summary>
    public ICommand SetStandardCommand { get; }

    /// <summary>Sets the display category to Other Information.</summary>
    public ICommand SetOtherInformationCommand { get; }

    /// <summary>Sets the display category to All.</summary>
    public ICommand SetAllCommand { get; }

    /// <summary>
    /// Localized label for the toolbar pill, e.g. "Display: Standard ▾".
    /// </summary>
    public string ActiveCategoryLabel =>
        string.Format(Strings.Toolbar_DisplayFormat, CategoryDisplayName(_state.Category));

    /// <summary>
    /// The active category, suitable for binding radio buttons.
    /// </summary>
    public EcdisDisplayCategory ActiveCategory => _state.Category;

    public bool IsDisplayBase => _state.Category == EcdisDisplayCategory.DisplayBase;
    public bool IsStandard => _state.Category == EcdisDisplayCategory.Standard;
    public bool IsOtherInformation => _state.Category == EcdisDisplayCategory.OtherInformation;
    public bool IsAll => _state.Category == EcdisDisplayCategory.All;

    /// <summary>
    /// Sets the display category and emits telemetry.
    /// </summary>
    public void SetCategory(EcdisDisplayCategory category)
    {
        if (_state.Category == category) return;

        using var scope = ViewerObservability.BeginCommand("display.mode.changed");
        scope.SetTag("s100.ecdis.category", category.ToString());
        _state.SetCategory(category);
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(ActiveCategoryLabel));
        OnPropertyChanged(nameof(ActiveCategory));
        OnPropertyChanged(nameof(IsDisplayBase));
        OnPropertyChanged(nameof(IsStandard));
        OnPropertyChanged(nameof(IsOtherInformation));
        OnPropertyChanged(nameof(IsAll));
    }

    /// <summary>
    /// Returns the localized display name for a category.
    /// </summary>
    internal static string CategoryDisplayName(EcdisDisplayCategory category) => category switch
    {
        EcdisDisplayCategory.DisplayBase => Strings.DisplayCategory_DisplayBase,
        EcdisDisplayCategory.Standard => Strings.DisplayCategory_Standard,
        EcdisDisplayCategory.OtherInformation => Strings.DisplayCategory_OtherInformation,
        EcdisDisplayCategory.All => Strings.DisplayCategory_All,
        _ => category.ToString(),
    };

    public void Dispose()
    {
        _state.Changed -= OnStateChanged;
    }
}
