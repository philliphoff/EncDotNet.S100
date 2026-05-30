using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
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

    public static TimeFormat[] AvailableTimeFormats { get; } =
    [
        EncDotNet.S100.Viewer.TimeFormat.Local,
        EncDotNet.S100.Viewer.TimeFormat.Utc,
    ];

    private TimeFormat _selectedTimeFormat;
    /// <summary>
    /// Display format used for every date/time the viewer surfaces.
    /// Persisted to <see cref="ViewerSettings.TimeFormat"/>.
    /// </summary>
    public TimeFormat SelectedTimeFormat
    {
        get => _selectedTimeFormat;
        set
        {
            if (SetProperty(ref _selectedTimeFormat, value))
            {
                _settings.TimeFormat = value.ToString();
                _settings.Save();
                TimeFormatChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Raised after <see cref="SelectedTimeFormat"/> changes and the
    /// settings file has been saved. <see cref="Services.TimeFormatProvider"/>
    /// listens for this and re-broadcasts to viewmodels.
    /// </summary>
    public event Action<TimeFormat>? TimeFormatChanged;

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
        _selectedTimeFormat = Enum.TryParse<TimeFormat>(settings.TimeFormat, ignoreCase: true, out var tf)
            ? tf
            : EncDotNet.S100.Viewer.TimeFormat.Local;

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

        _mcpEnabled = settings.McpEnabled;
        _mcpPort = settings.McpPort;
        ResetMcpPortCommand = new RelayCommand(() => McpPort = 0);

        var own = settings.OwnShip ?? new OwnShipSettings();
        _ownShipLength = own.LengthMetres;
        _ownShipBeam = own.BeamMetres;
        _ownShipBowOffset = own.BowOffsetMetres;
        _ownShipPortOffset = own.PortOffsetMetres;

        var ais = settings.AisOverlay ?? new AisOverlaySettings();
        _aisEnabled = ais.Enabled;
        _aisApiKey = ais.ApiKey;
    }

    /// <summary>
    /// Command bound to the "Reset to auto" button in Settings.
    /// Clears the persisted MCP port so the next bind picks an
    /// ephemeral port (which the host then persists back).
    /// </summary>
    public ICommand ResetMcpPortCommand { get; }

    /// <summary>
    /// Raised when an MCP-related setting changes so the
    /// <see cref="Services.McpServerHost"/> can reconcile.
    /// </summary>
    public event Action? McpSettingsChanged;

    private bool _mcpEnabled;
    /// <summary>
    /// Whether the embedded MCP server should be running. Persisted to
    /// <see cref="ViewerSettings.McpEnabled"/> and reconciled on change.
    /// </summary>
    public bool McpEnabled
    {
        get => _mcpEnabled;
        set
        {
            if (SetProperty(ref _mcpEnabled, value))
            {
                _settings.McpEnabled = value;
                _settings.Save();
                McpSettingsChanged?.Invoke();
            }
        }
    }

    private int _mcpPort;
    /// <summary>
    /// TCP port for the MCP server. 0 means pick an ephemeral port.
    /// Persisted to <see cref="ViewerSettings.McpPort"/>.
    /// </summary>
    public int McpPort
    {
        get => _mcpPort;
        set
        {
            if (value < 0) value = 0;
            if (value > 65535) value = 65535;
            if (SetProperty(ref _mcpPort, value))
            {
                _settings.McpPort = value;
                _settings.Save();
                McpSettingsChanged?.Invoke();
            }
        }
    }

    // ---------------------------------------------------------------
    // Own-vessel dimensions (own-ship symbology PR).
    // ---------------------------------------------------------------

    /// <summary>
    /// Raised after any own-vessel dimension changes and the settings
    /// file has been saved. The viewer wires this to
    /// <c>SettingsOwnShipVesselGeometryProvider.NotifyChanged</c> so
    /// the <c>OwnShipSource</c> re-publishes its current fix with
    /// the new dimensions.
    /// </summary>
    public event Action? OwnShipGeometryChanged;

    private void EnsureOwnShipSettings()
    {
        _settings.OwnShip ??= new OwnShipSettings();
    }

    private double _ownShipLength;
    /// <summary>Vessel length in metres. Clamped to (0, ∞).</summary>
    public double OwnShipLengthMetres
    {
        get => _ownShipLength;
        set
        {
            if (value <= 0) value = 1;
            if (SetProperty(ref _ownShipLength, value))
            {
                EnsureOwnShipSettings();
                _settings.OwnShip!.LengthMetres = value;
                if (_ownShipBowOffset > value) OwnShipBowOffsetMetres = value;
                _settings.Save();
                OwnShipGeometryChanged?.Invoke();
            }
        }
    }

    private double _ownShipBeam;
    /// <summary>Vessel beam in metres. Clamped to (0, ∞).</summary>
    public double OwnShipBeamMetres
    {
        get => _ownShipBeam;
        set
        {
            if (value <= 0) value = 1;
            if (SetProperty(ref _ownShipBeam, value))
            {
                EnsureOwnShipSettings();
                _settings.OwnShip!.BeamMetres = value;
                if (_ownShipPortOffset > value) OwnShipPortOffsetMetres = value;
                _settings.Save();
                OwnShipGeometryChanged?.Invoke();
            }
        }
    }

    private double _ownShipBowOffset;
    /// <summary>GPS antenna distance aft of bow, in metres.
    /// Clamped to [0, <see cref="OwnShipLengthMetres"/>].</summary>
    public double OwnShipBowOffsetMetres
    {
        get => _ownShipBowOffset;
        set
        {
            if (value < 0) value = 0;
            if (value > _ownShipLength) value = _ownShipLength;
            if (SetProperty(ref _ownShipBowOffset, value))
            {
                EnsureOwnShipSettings();
                _settings.OwnShip!.BowOffsetMetres = value;
                _settings.Save();
                OwnShipGeometryChanged?.Invoke();
            }
        }
    }

    private double _ownShipPortOffset;
    /// <summary>GPS antenna distance starboard of port side, in metres.
    /// Clamped to [0, <see cref="OwnShipBeamMetres"/>].</summary>
    public double OwnShipPortOffsetMetres
    {
        get => _ownShipPortOffset;
        set
        {
            if (value < 0) value = 0;
            if (value > _ownShipBeam) value = _ownShipBeam;
            if (SetProperty(ref _ownShipPortOffset, value))
            {
                EnsureOwnShipSettings();
                _settings.OwnShip!.PortOffsetMetres = value;
                _settings.Save();
                OwnShipGeometryChanged?.Invoke();
            }
        }
    }

    // ---------------------------------------------------------------
    // AIS overlay (PR-D3). Changes don't take effect until restart;
    // the source is registered as a singleton at app startup.
    // ---------------------------------------------------------------

    private void EnsureAisOverlaySettings()
    {
        _settings.AisOverlay ??= new AisOverlaySettings();
    }

    private bool _aisEnabled;
    /// <summary>
    /// User opt-in for the AIS overlay. Persisted to
    /// <see cref="AisOverlaySettings.Enabled"/>. Effective on next
    /// viewer restart.
    /// </summary>
    public bool AisEnabled
    {
        get => _aisEnabled;
        set
        {
            if (SetProperty(ref _aisEnabled, value))
            {
                EnsureAisOverlaySettings();
                _settings.AisOverlay!.Enabled = value;
                _settings.Save();
            }
        }
    }

    private string? _aisApiKey;
    /// <summary>
    /// aisstream.io API key persisted in <c>settings.json</c>. The
    /// env var named in
    /// <see cref="AisOverlaySettings.ApiKeyEnvironmentVariable"/>
    /// takes precedence when set; this field is the convenience
    /// fallback for users who don't want to manage env vars.
    /// </summary>
    public string? AisApiKey
    {
        get => _aisApiKey;
        set
        {
            // Treat blank/whitespace as null so the env-var fallback
            // path is taken cleanly when the user clears the field.
            var normalised = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetProperty(ref _aisApiKey, normalised))
            {
                EnsureAisOverlaySettings();
                _settings.AisOverlay!.ApiKey = normalised;
                _settings.Save();
            }
        }
    }

    /// <summary>
    /// Name of the env var that, when set, supplies the API key in
    /// preference to <see cref="AisApiKey"/>. Read-only in the UI;
    /// surfaced as a hint so users know which variable to set.
    /// </summary>
    public string AisApiKeyEnvironmentVariable =>
        _settings.AisOverlay?.ApiKeyEnvironmentVariable
        ?? new AisOverlaySettings().ApiKeyEnvironmentVariable;

    /// <summary>
    /// Localised hint shown beneath the API-key field. Renders the
    /// "env var is set, will be used" copy when the env var resolves
    /// at viewmodel-construction time, otherwise the "or set ENV"
    /// reminder.
    /// </summary>
    public string AisApiKeyHint
    {
        get
        {
            var envVar = AisApiKeyEnvironmentVariable;
            var envVal = Environment.GetEnvironmentVariable(envVar);
            return string.IsNullOrWhiteSpace(envVal)
                ? string.Format(CultureInfo.CurrentCulture, Strings.Settings_AisApiKey_EnvVarHint, envVar)
                : string.Format(CultureInfo.CurrentCulture, Strings.Settings_AisApiKey_EnvVarPresent, envVar);
        }
    }
}