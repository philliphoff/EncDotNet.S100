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

    private double _symbolScale;
    public double SymbolScale
    {
        get => _symbolScale;
        set
        {
            if (SetProperty(ref _symbolScale, value))
            {
                _settings.SymbolScale = value;
                _settings.Save();
                DisplayScaleChanged?.Invoke();
            }
        }
    }

    private double _textScale;
    public double TextScale
    {
        get => _textScale;
        set
        {
            if (SetProperty(ref _textScale, value))
            {
                _settings.TextScale = value;
                _settings.Save();
                DisplayScaleChanged?.Invoke();
            }
        }
    }

    public event Action? DisplayScaleChanged;

    public static DistanceUnit[] AvailableDistanceUnits { get; } =
    [
        EncDotNet.S100.Viewer.DistanceUnit.NauticalMiles,
        EncDotNet.S100.Viewer.DistanceUnit.Metric,
        EncDotNet.S100.Viewer.DistanceUnit.Miles,
    ];

    private DistanceUnit _distanceUnit;
    public DistanceUnit DistanceUnit
    {
        get => _distanceUnit;
        set
        {
            if (SetProperty(ref _distanceUnit, value))
            {
                _settings.DistanceUnit = value.ToString();
                _settings.Save();
                DistanceUnitChanged?.Invoke(value);
            }
        }
    }

    public event Action<DistanceUnit>? DistanceUnitChanged;

    /// <summary>
    /// Identifier of the most-recently-active map tool (e.g. "pick" or
    /// "measure"), or <c>null</c> when no tool was active. Persisted across
    /// sessions so the viewer reopens in whichever tool the user left in.
    /// Setter writes-through to <see cref="ViewerSettings.Save"/>.
    /// </summary>
    public string? LastActiveToolId
    {
        get => _settings.LastActiveToolId;
        set
        {
            if (string.Equals(_settings.LastActiveToolId, value, StringComparison.Ordinal))
                return;
            _settings.LastActiveToolId = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public SettingsViewModel(ViewerSettings settings)
    {
        _settings = settings;
        _accentColor = Color.TryParse(settings.AccentColor, out var c) ? c : Color.Parse("#007ACC");
        _selectedPalette = Enum.TryParse<PaletteType>(settings.ColorProfile, ignoreCase: true, out var p) ? p : PaletteType.Day;
        _symbolScale = settings.SymbolScale;
        _textScale = settings.TextScale;
        _distanceUnit = Enum.TryParse<DistanceUnit>(settings.DistanceUnit, ignoreCase: true, out var u)
            ? u
            : EncDotNet.S100.Viewer.DistanceUnit.NauticalMiles;
    }
}
