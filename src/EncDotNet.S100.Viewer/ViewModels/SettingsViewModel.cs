using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;

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

    // -------------------------------------------------------------------
    // Mariner settings (S-100 Part 9 §4.2).
    //
    // Depth values are stored in metres internally; the *Display string
    // properties round-trip through DepthFormatting using the active
    // SelectedDepthUnit so the user types and sees their chosen unit.
    // -------------------------------------------------------------------

    public static DepthUnit[] AvailableDepthUnits { get; } =
    [
        DepthUnit.Metres,
        DepthUnit.Feet,
        DepthUnit.FathomsFeet,
        DepthUnit.Fathoms,
    ];

    /// <summary>
    /// Languages the user can pick. <see cref="LanguageOption.Code"/> is the
    /// ISO 639-2/B 3-letter code stored in settings; empty string means
    /// "follow the operating system's UI culture" (resolved at snapshot time
    /// in <see cref="BuildMarinerSettings"/>).
    /// </summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } = BuildLanguageOptions();

    private static IReadOnlyList<LanguageOption> BuildLanguageOptions()
    {
        var sysCulture = CultureInfo.CurrentUICulture;
        var systemLabel = string.Format(
            CultureInfo.CurrentUICulture,
            Strings.Language_System,
            sysCulture.DisplayName);

        var list = new List<LanguageOption> { new("", systemLabel) };

        // S-100 PC NationalLanguage uses ISO 639-2/B; we surface a short list
        // of common chart languages and look up the localised display name
        // from the OS culture catalogue so labels match the user's locale.
        string[] codes = ["eng", "fra", "spa", "deu", "ita", "nld", "nor", "swe", "fin", "dan", "rus", "jpn", "kor", "zho", "ara"];
        foreach (var code in codes)
        {
            var culture = TryFindCultureByThreeLetterCode(code);
            var name = culture?.DisplayName ?? code;
            list.Add(new LanguageOption(code, name));
        }
        return list;
    }

    private static CultureInfo? TryFindCultureByThreeLetterCode(string threeLetterIsoCode)
    {
        foreach (var c in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (string.Equals(c.ThreeLetterISOLanguageName, threeLetterIsoCode, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    /// <summary>
    /// Raised after any mariner-affecting property has changed and the
    /// settings file has been saved. The <see cref="MarinerSettingsProvider"/>
    /// listens for this and rebuilds its snapshot.
    /// </summary>
    public event Action? MarinerChanged;

    private void RaiseMarinerChanged()
    {
        _settings.Save();
        MarinerChanged?.Invoke();
    }

    private DepthUnit _selectedDepthUnit;
    public DepthUnit SelectedDepthUnit
    {
        get => _selectedDepthUnit;
        set
        {
            if (SetProperty(ref _selectedDepthUnit, value))
            {
                _settings.DepthUnit = value.ToString();
                // Re-emit display strings so bound TextBoxes refresh.
                OnPropertyChanged(nameof(SafetyContourDisplay));
                OnPropertyChanged(nameof(SafetyDepthDisplay));
                OnPropertyChanged(nameof(ShallowContourDisplay));
                OnPropertyChanged(nameof(DeepContourDisplay));
                RaiseMarinerChanged();
            }
        }
    }

    private double _safetyContour;
    public double SafetyContour
    {
        get => _safetyContour;
        set
        {
            if (SetProperty(ref _safetyContour, value))
            {
                _settings.SafetyContour = value;
                OnPropertyChanged(nameof(SafetyContourDisplay));
                RaiseMarinerChanged();
            }
        }
    }

    public string SafetyContourDisplay
    {
        get => DepthFormatting.Format(_safetyContour, _selectedDepthUnit);
        set
        {
            if (DepthFormatting.TryParse(value ?? string.Empty, _selectedDepthUnit, out var m))
                SafetyContour = m;
        }
    }

    private double _safetyDepth;
    public double SafetyDepth
    {
        get => _safetyDepth;
        set
        {
            if (SetProperty(ref _safetyDepth, value))
            {
                _settings.SafetyDepth = value;
                OnPropertyChanged(nameof(SafetyDepthDisplay));
                RaiseMarinerChanged();
            }
        }
    }

    public string SafetyDepthDisplay
    {
        get => DepthFormatting.Format(_safetyDepth, _selectedDepthUnit);
        set
        {
            if (DepthFormatting.TryParse(value ?? string.Empty, _selectedDepthUnit, out var m))
                SafetyDepth = m;
        }
    }

    private double _shallowContour;
    public double ShallowContour
    {
        get => _shallowContour;
        set
        {
            if (SetProperty(ref _shallowContour, value))
            {
                _settings.ShallowContour = value;
                OnPropertyChanged(nameof(ShallowContourDisplay));
                RaiseMarinerChanged();
            }
        }
    }

    public string ShallowContourDisplay
    {
        get => DepthFormatting.Format(_shallowContour, _selectedDepthUnit);
        set
        {
            if (DepthFormatting.TryParse(value ?? string.Empty, _selectedDepthUnit, out var m))
                ShallowContour = m;
        }
    }

    private double _deepContour;
    public double DeepContour
    {
        get => _deepContour;
        set
        {
            if (SetProperty(ref _deepContour, value))
            {
                _settings.DeepContour = value;
                OnPropertyChanged(nameof(DeepContourDisplay));
                RaiseMarinerChanged();
            }
        }
    }

    public string DeepContourDisplay
    {
        get => DepthFormatting.Format(_deepContour, _selectedDepthUnit);
        set
        {
            if (DepthFormatting.TryParse(value ?? string.Empty, _selectedDepthUnit, out var m))
                DeepContour = m;
        }
    }

    private bool _fourShades;
    public bool FourShades
    {
        get => _fourShades;
        set { if (SetProperty(ref _fourShades, value)) { _settings.FourShades = value; RaiseMarinerChanged(); } }
    }

    private bool _shallowWaterDangers;
    public bool ShallowWaterDangers
    {
        get => _shallowWaterDangers;
        set { if (SetProperty(ref _shallowWaterDangers, value)) { _settings.ShallowWaterDangers = value; RaiseMarinerChanged(); } }
    }

    private bool _plainBoundaries;
    public bool PlainBoundaries
    {
        get => _plainBoundaries;
        set { if (SetProperty(ref _plainBoundaries, value)) { _settings.PlainBoundaries = value; RaiseMarinerChanged(); } }
    }

    private bool _simplifiedSymbols;
    public bool SimplifiedSymbols
    {
        get => _simplifiedSymbols;
        set { if (SetProperty(ref _simplifiedSymbols, value)) { _settings.SimplifiedSymbols = value; RaiseMarinerChanged(); } }
    }

    private bool _fullLightLines;
    public bool FullLightLines
    {
        get => _fullLightLines;
        set { if (SetProperty(ref _fullLightLines, value)) { _settings.FullLightLines = value; RaiseMarinerChanged(); } }
    }

    private bool _radarOverlay;
    public bool RadarOverlay
    {
        get => _radarOverlay;
        set { if (SetProperty(ref _radarOverlay, value)) { _settings.RadarOverlay = value; RaiseMarinerChanged(); } }
    }

    private bool _ignoreScaleMinimum;
    public bool IgnoreScaleMinimum
    {
        get => _ignoreScaleMinimum;
        set { if (SetProperty(ref _ignoreScaleMinimum, value)) { _settings.IgnoreScaleMinimum = value; RaiseMarinerChanged(); } }
    }

    private string _nationalLanguage = "";
    public string NationalLanguage
    {
        get => _nationalLanguage;
        set
        {
            var v = value ?? string.Empty;
            if (SetProperty(ref _nationalLanguage, v))
            {
                _settings.NationalLanguage = v;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>
    /// Builds an immutable <see cref="MarinerSettings"/> snapshot from the
    /// view-model's current values. Used by <see cref="MarinerSettingsProvider"/>.
    /// </summary>
    public MarinerSettings BuildMarinerSettings() => new()
    {
        SafetyContour = _safetyContour,
        SafetyDepth = _safetyDepth,
        ShallowContour = _shallowContour,
        DeepContour = _deepContour,
        DepthUnit = _selectedDepthUnit,
        FourShades = _fourShades,
        ShallowWaterDangers = _shallowWaterDangers,
        PlainBoundaries = _plainBoundaries,
        SimplifiedSymbols = _simplifiedSymbols,
        FullLightLines = _fullLightLines,
        RadarOverlay = _radarOverlay,
        IgnoreScaleMinimum = _ignoreScaleMinimum,
        NationalLanguage = ResolveLanguageCode(_nationalLanguage),
    };

    private static string ResolveLanguageCode(string stored)
    {
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;

        // Empty / null means "follow the OS UI culture". Map the current UI
        // culture's 3-letter ISO 639-2 code into the same form S-101 expects.
        // If the runtime can't supply a real code (e.g. invariant culture
        // returns "ivl"), fall back to empty so the executor skips the param
        // and the catalogue default applies.
        var code = CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName;
        if (string.IsNullOrEmpty(code) || code == "ivl")
            return string.Empty;
        return code;
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

        // Mariner settings — pull from JSON, falling back to MarinerSettings.Default.
        var def = MarinerSettings.Default;
        _safetyContour = settings.SafetyContour ?? def.SafetyContour;
        _safetyDepth = settings.SafetyDepth ?? def.SafetyDepth;
        _shallowContour = settings.ShallowContour ?? def.ShallowContour;
        _deepContour = settings.DeepContour ?? def.DeepContour;
        _selectedDepthUnit = Enum.TryParse<DepthUnit>(settings.DepthUnit, ignoreCase: true, out var du)
            ? du
            : def.DepthUnit;
        _fourShades = settings.FourShades ?? def.FourShades;
        _shallowWaterDangers = settings.ShallowWaterDangers ?? def.ShallowWaterDangers;
        _plainBoundaries = settings.PlainBoundaries ?? def.PlainBoundaries;
        _simplifiedSymbols = settings.SimplifiedSymbols ?? def.SimplifiedSymbols;
        _fullLightLines = settings.FullLightLines ?? def.FullLightLines;
        _radarOverlay = settings.RadarOverlay ?? def.RadarOverlay;
        _ignoreScaleMinimum = settings.IgnoreScaleMinimum ?? def.IgnoreScaleMinimum;
        _nationalLanguage = settings.NationalLanguage ?? def.NationalLanguage;
    }
}
