using System;
using Avalonia.Media;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class SettingsViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;

    private Color _accentColor;
    public Color AccentColor
    {
        get => _accentColor;
        set
        {
            if (SetProperty(ref _accentColor, value))
            {
                _settings.AccentColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                _settings.Save();
                AccentColorChanged?.Invoke(value);
            }
        }
    }

    public event Action<Color>? AccentColorChanged;

    public static PaletteType[] AvailablePalettes { get; } = [PaletteType.Day, PaletteType.Dusk, PaletteType.Night];

    private PaletteType _selectedPalette;
    public PaletteType SelectedPalette
    {
        get => _selectedPalette;
        set
        {
            if (SetProperty(ref _selectedPalette, value))
            {
                _settings.ColorProfile = value.ToString();
                _settings.Save();
                PaletteChanged?.Invoke(value);
            }
        }
    }

    public event Action<PaletteType>? PaletteChanged;

    public SettingsViewModel(ViewerSettings settings)
    {
        _settings = settings;
        _accentColor = Color.TryParse(settings.AccentColor, out var c) ? c : Color.Parse("#007ACC");
        _selectedPalette = Enum.TryParse<PaletteType>(settings.ColorProfile, ignoreCase: true, out var p) ? p : PaletteType.Day;
    }
}
