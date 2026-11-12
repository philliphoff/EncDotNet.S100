using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The display toolbar's palette menu: Day, Dusk and Night (#1398).</summary>
internal sealed class PaletteMenuViewModel : ViewModelBase
{
    private readonly ViewerSettingsService _settings;

    /// <summary>Creates the menu.</summary>
    /// <param name="settings">Holds the palette.</param>
    public PaletteMenuViewModel(ViewerSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        SetPaletteDayCommand = new RelayCommand(() => _settings.SelectedPalette = PaletteType.Day);
        SetPaletteDuskCommand = new RelayCommand(() => _settings.SelectedPalette = PaletteType.Dusk);
        SetPaletteNightCommand = new RelayCommand(() => _settings.SelectedPalette = PaletteType.Night);
        settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>True when the active S-100 map palette is Day.</summary>
    public bool IsPaletteDay => _settings.SelectedPalette == PaletteType.Day;

    /// <summary>True when the active S-100 map palette is Dusk.</summary>
    public bool IsPaletteDusk => _settings.SelectedPalette == PaletteType.Dusk;

    /// <summary>True when the active S-100 map palette is Night.</summary>
    public bool IsPaletteNight => _settings.SelectedPalette == PaletteType.Night;

    /// <summary>Sets the S-100 map palette to Day.</summary>
    public ICommand SetPaletteDayCommand { get; }

    /// <summary>Sets the S-100 map palette to Dusk.</summary>
    public ICommand SetPaletteDuskCommand { get; }

    /// <summary>Sets the S-100 map palette to Night.</summary>
    public ICommand SetPaletteNightCommand { get; }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewerSettingsService.SelectedPalette))
            return;
        OnPropertyChanged(nameof(IsPaletteDay));
        OnPropertyChanged(nameof(IsPaletteDusk));
        OnPropertyChanged(nameof(IsPaletteNight));
    }
}
