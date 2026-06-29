using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using ShadUI;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class SettingsViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly IDataMaintenanceService? _dataMaintenance;
    private readonly IApplicationControlService? _applicationControl;
    private readonly DialogManager? _dialogManager;

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
                OnPropertyChanged(nameof(IsPaletteDay));
                OnPropertyChanged(nameof(IsPaletteDusk));
                OnPropertyChanged(nameof(IsPaletteNight));
            }
        }
    }

    /// <summary>True when the active S-100 map palette is Day. Drives the toolbar palette flyout's RadioButton state.</summary>
    public bool IsPaletteDay => _selectedPalette == PaletteType.Day;

    /// <summary>True when the active S-100 map palette is Dusk.</summary>
    public bool IsPaletteDusk => _selectedPalette == PaletteType.Dusk;

    /// <summary>True when the active S-100 map palette is Night.</summary>
    public bool IsPaletteNight => _selectedPalette == PaletteType.Night;

    /// <summary>Sets the S-100 map palette to Day. Bound from the toolbar palette flyout.</summary>
    public ICommand SetPaletteDayCommand { get; }

    /// <summary>Sets the S-100 map palette to Dusk. Bound from the toolbar palette flyout.</summary>
    public ICommand SetPaletteDuskCommand { get; }

    /// <summary>Sets the S-100 map palette to Night. Bound from the toolbar palette flyout.</summary>
    public ICommand SetPaletteNightCommand { get; }

    public event Action<PaletteType>? PaletteChanged;

    public static ChromeTheme[] AvailableChromeThemes { get; } =
        [ChromeTheme.Light, ChromeTheme.Dark, ChromeTheme.S100Night, ChromeTheme.S100Dusk];

    private ChromeTheme _selectedChromeTheme;

    /// <summary>
    /// User-selected chrome theme (Light / Dark / S100Night).
    /// Persisted to <see cref="ViewerSettings.ChromeTheme"/>. The
    /// setter only updates persisted state and fires
    /// <see cref="ChromeThemeChanged"/>; the host (App.axaml.cs) is
    /// responsible for translating the change into an
    /// <see cref="IThemeService.SetTheme"/> call and for resetting
    /// <see cref="SelectedPalette"/> to the default for the new
    /// chrome. Splitting the responsibility keeps SettingsViewModel
    /// free of an IThemeService dependency so existing tests keep
    /// constructing it with just a <see cref="ViewerSettings"/>.
    /// </summary>
    public ChromeTheme SelectedChromeTheme
    {
        get => _selectedChromeTheme;
        set
        {
            if (SetProperty(ref _selectedChromeTheme, value))
            {
                _settings.ChromeTheme = value.ToString();
                _settings.Save();
                ChromeThemeChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Raised after the user picks a new chrome theme. Listeners are
    /// expected to (a) apply the variant via
    /// <see cref="IThemeService.SetTheme"/> and (b) write
    /// <see cref="SelectedPalette"/> back to
    /// <see cref="ChromeThemes.GetDefaultPaletteFor"/> so the map
    /// follows. Users can then override the map palette manually for
    /// inspection.
    /// </summary>
    public event Action<ChromeTheme>? ChromeThemeChanged;

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

    private bool _vectorSnapshotEnabled;
    /// <summary>
    /// Whether the raster vector-layer snapshot fast path is enabled. The "best"
    /// default (on). Toggling pushes the value to
    /// <see cref="RenderingOptimizations.VectorSnapshotEnabled"/> and triggers a
    /// full re-render via <c>MarinerChanged</c> so layers are re-tagged.
    /// </summary>
    public bool VectorSnapshotEnabled
    {
        get => _vectorSnapshotEnabled;
        set
        {
            if (SetProperty(ref _vectorSnapshotEnabled, value))
            {
                _settings.VectorSnapshotEnabled = value;
                RenderingOptimizations.VectorSnapshotEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    private bool _vectorSnapshotPrebuildEnabled;
    /// <summary>
    /// Whether the off-thread snapshot prebuild is enabled. The "best" default
    /// (on); only meaningful when <see cref="VectorSnapshotEnabled"/> is on.
    /// </summary>
    public bool VectorSnapshotPrebuildEnabled
    {
        get => _vectorSnapshotPrebuildEnabled;
        set
        {
            if (SetProperty(ref _vectorSnapshotPrebuildEnabled, value))
            {
                _settings.VectorSnapshotPrebuildEnabled = value;
                RenderingOptimizations.VectorSnapshotPrebuildEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    private bool _vectorPathCacheEnabled;
    /// <summary>
    /// Whether the translation-invariant vector path cache is enabled. The "best"
    /// default (on).
    /// </summary>
    public bool VectorPathCacheEnabled
    {
        get => _vectorPathCacheEnabled;
        set
        {
            if (SetProperty(ref _vectorPathCacheEnabled, value))
            {
                _settings.VectorPathCacheEnabled = value;
                RenderingOptimizations.VectorPathCacheEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    private bool _geometrySimplificationEnabled;
    /// <summary>
    /// Whether resolution-aware <b>line</b> geometry simplification is enabled.
    /// The "best" default (on); requires <see cref="VectorPathCacheEnabled"/>.
    /// Polygons are always rendered vertex-exact.
    /// </summary>
    public bool GeometrySimplificationEnabled
    {
        get => _geometrySimplificationEnabled;
        set
        {
            if (SetProperty(ref _geometrySimplificationEnabled, value))
            {
                _settings.GeometrySimplificationEnabled = value;
                RenderingOptimizations.GeometrySimplificationEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    // ── Render subsystem (issue #331) ──────────────────────────────────────
    // The A/B base-plane render-subsystem switch and the "B" tiled-pipeline
    // optimization knobs. All values mirror RenderingOptimizations (the renderer
    // source of truth) and persist to ViewerSettings; an explicit environment
    // variable pins a knob (the perf A/B harness), in which case the matching
    // *Editable flag is false and the control is shown disabled.

    /// <summary>The selectable base-plane render subsystems (the A/B switch).</summary>
    public static RenderSubsystemKind[] AvailableRenderSubsystems { get; } =
        [RenderSubsystemKind.Mapsui, RenderSubsystemKind.TiledScene];

    /// <summary>The selectable scene modes within the TiledScene ("B") arm.</summary>
    public static VectorSceneMode[] AvailableSceneModes { get; } =
        [VectorSceneMode.Tiled, VectorSceneMode.Single];

    private RenderSubsystemKind _renderSubsystem;
    /// <summary>
    /// The active base-plane render subsystem — "A" (<see cref="RenderSubsystemKind.Mapsui"/>)
    /// vs the "B" (<see cref="RenderSubsystemKind.TiledScene"/>).
    /// Read per-render, so switching rebinds the active subsystem on the next
    /// re-render. Disabled when pinned by <c>S100_RENDER_SUBSYSTEM</c>.
    /// </summary>
    public RenderSubsystemKind SelectedRenderSubsystem
    {
        get => _renderSubsystem;
        set
        {
            if (SetProperty(ref _renderSubsystem, value))
            {
                _settings.RenderSubsystem = value.ToString();
                RenderingOptimizations.RenderSubsystem = value;
                OnPropertyChanged(nameof(MapsuiSelected));
                OnPropertyChanged(nameof(TiledSceneSelected));
                OnPropertyChanged(nameof(TiledModeActive));
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the subsystem switch is user-editable (not env-pinned).</summary>
    public bool RenderSubsystemEditable => !RenderingOptimizations.RenderSubsystemEnvExplicit;

    /// <summary>
    /// True when the "A" (<see cref="RenderSubsystemKind.Mapsui"/>) arm is
    /// selected — gates the snapshot / path-cache / geometry-simplification
    /// optimization group so only the active subsystem's knobs are shown.
    /// </summary>
    public bool MapsuiSelected => _renderSubsystem == RenderSubsystemKind.Mapsui;

    /// <summary>True when the "B" (TiledScene) arm is selected — gates the knob panel.</summary>
    public bool TiledSceneSelected => _renderSubsystem == RenderSubsystemKind.TiledScene;

    /// <summary>True when the tiled base plane is active (B arm + tiled scene mode) — gates the tiled knobs.</summary>
    public bool TiledModeActive => TiledSceneSelected && _sceneMode == VectorSceneMode.Tiled;

    private VectorSceneMode _sceneMode;
    /// <summary>
    /// Within the "B" arm, the base-plane scene mode — <see cref="VectorSceneMode.Tiled"/>
    /// (Phase-2 default) vs <see cref="VectorSceneMode.Single"/> (Phase-1 single
    /// surface). Read at layer build, so a change applies on the next re-render.
    /// Disabled when pinned by <c>S100_VECTOR_SCENE_MODE</c>.
    /// </summary>
    public VectorSceneMode SelectedSceneMode
    {
        get => _sceneMode;
        set
        {
            if (SetProperty(ref _sceneMode, value))
            {
                _settings.VectorSceneMode = value.ToString();
                RenderingOptimizations.SceneMode = value;
                OnPropertyChanged(nameof(TiledModeActive));
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the scene-mode selector is user-editable (not env-pinned).</summary>
    public bool SceneModeEditable => !RenderingOptimizations.SceneModeEnvExplicit;

    private double _tileGutterDip;
    /// <summary>
    /// Tiled-base-plane gutter, in DIP. Applies to newly-rasterised tiles; pair a
    /// live change with a dataset reload for a clean result. Disabled when pinned
    /// by <c>S100_VECTOR_TILE_GUTTER</c>.
    /// </summary>
    public double TileGutterDip
    {
        get => _tileGutterDip;
        set
        {
            RenderingOptimizations.TileGutterDip = value;
            var effective = RenderingOptimizations.TileGutterDip;
            if (SetProperty(ref _tileGutterDip, effective))
            {
                _settings.TileGutterDip = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the gutter knob is user-editable (not env-pinned).</summary>
    public bool TileGutterDipEditable => !RenderingOptimizations.TileGutterDipEnvExplicit;

    private double _tileBudgetMb;
    /// <summary>
    /// Per-layer hot-cache native budget, in MB. Captured per layer when its tile
    /// state is created, so a change applies on the next dataset reload. Disabled
    /// when pinned by <c>S100_VECTOR_TILE_BUDGET_MB</c>.
    /// </summary>
    public double TileBudgetMb
    {
        get => _tileBudgetMb;
        set
        {
            RenderingOptimizations.TileBudgetMb = value;
            var effective = RenderingOptimizations.TileBudgetMb;
            if (SetProperty(ref _tileBudgetMb, effective))
            {
                _settings.TileBudgetMb = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the in-memory budget knob is user-editable (not env-pinned).</summary>
    public bool TileBudgetMbEditable => !RenderingOptimizations.TileBudgetMbEnvExplicit;

    private bool _tilePredictionEnabled;
    /// <summary>
    /// Whether speculative prediction / pre-warm is enabled. Read every frame, so
    /// the change takes effect live. Disabled when pinned by
    /// <c>S100_VECTOR_TILE_PREDICT</c>.
    /// </summary>
    public bool TilePredictionEnabled
    {
        get => _tilePredictionEnabled;
        set
        {
            if (SetProperty(ref _tilePredictionEnabled, value))
            {
                RenderingOptimizations.TilePredictionEnabled = value;
                _settings.TilePredictionEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the prediction knob is user-editable (not env-pinned).</summary>
    public bool TilePredictionEditable => !RenderingOptimizations.TilePredictionEnvExplicit;

    private bool _tileDiskCacheEnabled;
    /// <summary>
    /// Whether the warm disk tile cache is enabled. The shared cache is created
    /// once per process, so a change applies on restart. Disabled when pinned by
    /// <c>S100_VECTOR_TILE_DISK</c>.
    /// </summary>
    public bool TileDiskCacheEnabled
    {
        get => _tileDiskCacheEnabled;
        set
        {
            if (SetProperty(ref _tileDiskCacheEnabled, value))
            {
                RenderingOptimizations.TileDiskCacheEnabled = value;
                _settings.TileDiskCacheEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the disk-cache knob is user-editable (not env-pinned).</summary>
    public bool TileDiskCacheEditable => !RenderingOptimizations.TileDiskCacheEnvExplicit;

    private double _tileDiskMb;
    /// <summary>
    /// Warm disk tile-cache budget, in MB. Read when the shared disk cache is
    /// created (once per process), so a change applies on restart. Disabled when
    /// pinned by <c>S100_VECTOR_TILE_DISK_MB</c>.
    /// </summary>
    public double TileDiskMb
    {
        get => _tileDiskMb;
        set
        {
            RenderingOptimizations.TileDiskMb = value;
            var effective = RenderingOptimizations.TileDiskMb;
            if (SetProperty(ref _tileDiskMb, effective))
            {
                _settings.TileDiskMb = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the disk-budget knob is user-editable (not env-pinned).</summary>
    public bool TileDiskMbEditable => !RenderingOptimizations.TileDiskMbEnvExplicit;

    private bool _tileGpuResidencyEnabled;
    /// <summary>
    /// Whether GPU texture residency is enabled. Read every frame, so the change
    /// takes effect live (inert on a software surface). Disabled when pinned by
    /// <c>S100_VECTOR_TILE_GPU</c>.
    /// </summary>
    public bool TileGpuResidencyEnabled
    {
        get => _tileGpuResidencyEnabled;
        set
        {
            if (SetProperty(ref _tileGpuResidencyEnabled, value))
            {
                RenderingOptimizations.TileGpuResidencyEnabled = value;
                _settings.TileGpuResidencyEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the GPU-residency knob is user-editable (not env-pinned).</summary>
    public bool TileGpuResidencyEditable => !RenderingOptimizations.TileGpuResidencyEnvExplicit;

    private double _tileGpuBudgetMb;
    /// <summary>
    /// Per-layer GPU-residency budget, in MB. Sized when the resident-texture
    /// cache is first created, so a change applies on the next dataset reload.
    /// Disabled when pinned by <c>S100_VECTOR_TILE_GPU_MB</c>.
    /// </summary>
    public double TileGpuBudgetMb
    {
        get => _tileGpuBudgetMb;
        set
        {
            RenderingOptimizations.TileGpuBudgetMb = value;
            var effective = RenderingOptimizations.TileGpuBudgetMb;
            if (SetProperty(ref _tileGpuBudgetMb, effective))
            {
                _settings.TileGpuBudgetMb = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the GPU-budget knob is user-editable (not env-pinned).</summary>
    public bool TileGpuBudgetMbEditable => !RenderingOptimizations.TileGpuBudgetMbEnvExplicit;

    private int _tileWorkerCount;
    /// <summary>
    /// Concurrent tile-rasterisation workers per layer. More workers drain a cold
    /// pan's visible-miss queue in parallel; sized by the profile (one on low-end
    /// hosts). Applies on the next dataset reload. Disabled when pinned by
    /// <c>S100_VECTOR_TILE_WORKERS</c>.
    /// </summary>
    public int TileWorkerCount
    {
        get => _tileWorkerCount;
        set
        {
            RenderingOptimizations.TileWorkerCount = value;
            var effective = RenderingOptimizations.TileWorkerCount;
            if (SetProperty(ref _tileWorkerCount, effective))
            {
                _settings.TileWorkerCount = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the worker-count knob is user-editable (not env-pinned).</summary>
    public bool TileWorkerCountEditable => !RenderingOptimizations.TileWorkerCountEnvExplicit;

    /// <summary>Selectable performance profiles for the profile dropdown.</summary>
    public IReadOnlyList<PerformanceProfile> PerformanceProfiles { get; } =
        new[] { PerformanceProfile.Auto, PerformanceProfile.HighEnd, PerformanceProfile.Balanced, PerformanceProfile.LowEnd };

    private PerformanceProfile _performanceProfile;
    /// <summary>
    /// The performance profile. <see cref="PerformanceProfile.Auto"/> sizes tile
    /// budgets + worker cap from detected cores + RAM; the explicit tiers pin
    /// them. Switching recomputes any non-env-pinned budget default and applies
    /// on the next dataset reload.
    /// </summary>
    public PerformanceProfile SelectedPerformanceProfile
    {
        get => _performanceProfile;
        set
        {
            RenderingOptimizations.Profile = value;
            if (SetProperty(ref _performanceProfile, value))
            {
                _settings.PerformanceProfile = value.ToString();
                _tileBudgetMb = RenderingOptimizations.TileBudgetMb;
                _tileGpuBudgetMb = RenderingOptimizations.TileGpuBudgetMb;
                _tileDiskMb = RenderingOptimizations.TileDiskMb;
                _tileWorkerCount = RenderingOptimizations.TileWorkerCount;
                OnPropertyChanged(nameof(TileBudgetMb));
                OnPropertyChanged(nameof(TileGpuBudgetMb));
                OnPropertyChanged(nameof(TileDiskMb));
                OnPropertyChanged(nameof(TileWorkerCount));
                OnPropertyChanged(nameof(ResolvedProfileLabel));
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the profile dropdown is user-editable (not env-pinned).</summary>
    public bool PerformanceProfileEditable => !RenderingOptimizations.ProfileEnvExplicit;

    /// <summary>The concrete tier Auto resolves to on this host, for display.</summary>
    public string ResolvedProfileLabel => RenderingOptimizations.ResolvedProfile.ToString();



    /// <summary>
    /// Raised when <see cref="SelectedBasemapMode"/> changes so the host
    /// can swap the basemap layer live without a restart.
    /// </summary>
    public event Action<BasemapMode>? BasemapModeChanged;

    /// <summary>Selectable basemap modes for the settings combo box.</summary>
    public static BasemapMode[] AvailableBasemapModes { get; } =
        [BasemapMode.None, BasemapMode.Offline, BasemapMode.Online];

    private BasemapMode _basemapMode;
    /// <summary>
    /// Which basemap is shown beneath the chart data (issue #295).
    /// Persisted to <see cref="ViewerSettings.BasemapMode"/>; changing it
    /// raises <see cref="BasemapModeChanged"/> so the map host swaps the
    /// layer without a relaunch.
    /// </summary>
    public BasemapMode SelectedBasemapMode
    {
        get => _basemapMode;
        set
        {
            if (SetProperty(ref _basemapMode, value))
            {
                _settings.BasemapMode = value;
                _settings.Save();
                BasemapModeChanged?.Invoke(value);
            }
        }
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

    public SettingsViewModel(
        ViewerSettings settings,
        IDataMaintenanceService? dataMaintenance = null,
        IApplicationControlService? applicationControl = null,
        DialogManager? dialogManager = null)
    {
        _settings = settings;
        _dataMaintenance = dataMaintenance;
        _applicationControl = applicationControl;
        _dialogManager = dialogManager;
        _accentColor = Color.TryParse(settings.AccentColor, out var c) ? c : Color.Parse("#007ACC");
        _selectedPalette = Enum.TryParse<PaletteType>(settings.ColorProfile, ignoreCase: true, out var p) ? p : PaletteType.Day;
        _selectedChromeTheme = Enum.TryParse<ChromeTheme>(settings.ChromeTheme, ignoreCase: true, out var ct) ? ct : ChromeTheme.Light;
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
        _vectorSnapshotEnabled = settings.VectorSnapshotEnabled ?? true;
        _vectorSnapshotPrebuildEnabled = settings.VectorSnapshotPrebuildEnabled ?? true;
        _vectorPathCacheEnabled = settings.VectorPathCacheEnabled ?? true;
        // Migrate the legacy line-only key forward to the unified geometry knob.
        _geometrySimplificationEnabled =
            settings.GeometrySimplificationEnabled ?? settings.LineSimplificationEnabled ?? true;

        // Push the persisted render-optimization preferences into the renderer.
        // Writes are ignored for any knob pinned by an explicit environment
        // variable (the perf A/B harness), so harness runs stay faithful.
        RenderingOptimizations.VectorSnapshotEnabled = _vectorSnapshotEnabled;
        RenderingOptimizations.VectorSnapshotPrebuildEnabled = _vectorSnapshotPrebuildEnabled;
        RenderingOptimizations.VectorPathCacheEnabled = _vectorPathCacheEnabled;
        RenderingOptimizations.GeometrySimplificationEnabled = _geometrySimplificationEnabled;

        // Render subsystem (issue #331): push the persisted A/B + tiled-knob
        // preferences into the renderer (each write is ignored for any knob
        // pinned by an explicit env var — the perf A/B harness), then read the
        // value back so an env-pinned knob displays the effective (env) value.
        if (Enum.TryParse<RenderSubsystemKind>(settings.RenderSubsystem, ignoreCase: true, out var subsystem))
        {
            RenderingOptimizations.RenderSubsystem = subsystem;
        }

        _renderSubsystem = RenderingOptimizations.RenderSubsystem;

        if (Enum.TryParse<VectorSceneMode>(settings.VectorSceneMode, ignoreCase: true, out var sceneMode))
        {
            RenderingOptimizations.SceneMode = sceneMode;
        }

        _sceneMode = RenderingOptimizations.SceneMode;

        // Apply the performance profile first so its derived budgets become the
        // baseline; an explicitly persisted budget/worker value below overrides.
        if (Enum.TryParse<PerformanceProfile>(settings.PerformanceProfile, ignoreCase: true, out var profile))
        {
            RenderingOptimizations.Profile = profile;
        }

        _performanceProfile = RenderingOptimizations.Profile;

        if (settings.TileGutterDip is { } tileGutter)
        {
            RenderingOptimizations.TileGutterDip = tileGutter;
        }

        _tileGutterDip = RenderingOptimizations.TileGutterDip;

        if (settings.TileBudgetMb is { } tileBudget)
        {
            RenderingOptimizations.TileBudgetMb = tileBudget;
        }

        _tileBudgetMb = RenderingOptimizations.TileBudgetMb;

        if (settings.TilePredictionEnabled is { } tilePredict)
        {
            RenderingOptimizations.TilePredictionEnabled = tilePredict;
        }

        _tilePredictionEnabled = RenderingOptimizations.TilePredictionEnabled;

        if (settings.TileDiskCacheEnabled is { } tileDisk)
        {
            RenderingOptimizations.TileDiskCacheEnabled = tileDisk;
        }

        _tileDiskCacheEnabled = RenderingOptimizations.TileDiskCacheEnabled;

        if (settings.TileDiskMb is { } tileDiskMb)
        {
            RenderingOptimizations.TileDiskMb = tileDiskMb;
        }

        _tileDiskMb = RenderingOptimizations.TileDiskMb;

        if (settings.TileGpuResidencyEnabled is { } tileGpu)
        {
            RenderingOptimizations.TileGpuResidencyEnabled = tileGpu;
        }

        _tileGpuResidencyEnabled = RenderingOptimizations.TileGpuResidencyEnabled;

        if (settings.TileGpuBudgetMb is { } tileGpuMb)
        {
            RenderingOptimizations.TileGpuBudgetMb = tileGpuMb;
        }

        _tileGpuBudgetMb = RenderingOptimizations.TileGpuBudgetMb;

        if (settings.TileWorkerCount is { } tileWorkers)
        {
            RenderingOptimizations.TileWorkerCount = tileWorkers;
        }

        _tileWorkerCount = RenderingOptimizations.TileWorkerCount;

        _basemapMode = settings.BasemapMode;
        _nationalLanguage = settings.NationalLanguage ?? def.NationalLanguage;

        _mcpEnabled = settings.McpEnabled;
        _mcpPort = settings.McpPort;
        ResetMcpPortCommand = new RelayCommand(() => McpPort = 0);

        var own = settings.OwnShip ?? new OwnShipSettings();
        _ownShipOverlayEnabled = settings.OwnShipOverlayEnabled;
        _ownShipLength = own.LengthMetres;
        _ownShipBeam = own.BeamMetres;
        _ownShipBowOffset = own.BowOffsetMetres;
        _ownShipPortOffset = own.PortOffsetMetres;

        SetPaletteDayCommand = new RelayCommand(() => SelectedPalette = PaletteType.Day);
        SetPaletteDuskCommand = new RelayCommand(() => SelectedPalette = PaletteType.Dusk);
        SetPaletteNightCommand = new RelayCommand(() => SelectedPalette = PaletteType.Night);

        ResetAllSettingsCommand = new RelayCommand(ConfirmResetAllSettings);
        ClearCachesCommand = new RelayCommand(ConfirmClearCaches);

        var ais = settings.AisOverlay ?? new AisOverlaySettings();
        _aisEnabled = ais.Enabled;
        _aisApiKey = ais.ApiKey;
        _aisActivationViewportSpanDegrees = ais.ActivationViewportSpanDegrees;
    }

    /// <summary>
    /// Command bound to the "Reset to auto" button in Settings.
    /// Clears the persisted MCP port so the next bind picks an
    /// ephemeral port (which the host then persists back).
    /// </summary>
    public ICommand ResetMcpPortCommand { get; }

    /// <summary>
    /// Command bound to the "Reset all settings" button. Shows a
    /// confirmation dialog; on confirm it performs a full clean-slate reset
    /// (settings + crash markers + caches) and restarts the viewer.
    /// </summary>
    public ICommand ResetAllSettingsCommand { get; }

    /// <summary>
    /// Command bound to the "Clear caches" button. Shows a confirmation
    /// dialog; on confirm it deletes the on-disk caches (settings are kept)
    /// and restarts so in-memory caches are dropped too.
    /// </summary>
    public ICommand ClearCachesCommand { get; }

    private void ConfirmResetAllSettings()
    {
        if (_dialogManager is null || _dataMaintenance is null || _applicationControl is null)
        {
            return;
        }

        _dialogManager
            .CreateDialog(Strings.Settings_ResetAll_ConfirmTitle, Strings.Settings_ResetAll_ConfirmMessage)
            .WithPrimaryButton(
                Strings.Settings_ResetAll_ConfirmButton,
                () =>
                {
                    _dataMaintenance.ResetAll();
                    _applicationControl.Restart();
                },
                DialogButtonStyle.Destructive)
            .WithCancelButton(Strings.Settings_Cancel)
            .Dismissible()
            .Show();
    }

    private void ConfirmClearCaches()
    {
        if (_dialogManager is null || _dataMaintenance is null || _applicationControl is null)
        {
            return;
        }

        _dialogManager
            .CreateDialog(Strings.Settings_ClearCaches_ConfirmTitle, Strings.Settings_ClearCaches_ConfirmMessage)
            .WithPrimaryButton(
                Strings.Settings_ClearCaches_ConfirmButton,
                () =>
                {
                    _dataMaintenance.ClearCaches();
                    _applicationControl.Restart();
                },
                DialogButtonStyle.Destructive)
            .WithCancelButton(Strings.Settings_Cancel)
            .Dismissible()
            .Show();
    }

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

    /// <summary>
    /// Raised when <see cref="OwnShipOverlayEnabled"/> changes. The
    /// viewer wires this to the singleton <c>OwnShipSource.IsEnabled</c>
    /// so the simulated own-ship overlay appears / disappears live.
    /// </summary>
    public event Action<bool>? OwnShipOverlayEnabledChanged;

    private bool _ownShipOverlayEnabled;
    /// <summary>
    /// Whether the simulated ("mocked") own-ship position overlay is
    /// shown. Defaults to <see langword="false"/>. Persisted to
    /// <see cref="ViewerSettings.OwnShipOverlayEnabled"/>.
    /// </summary>
    public bool OwnShipOverlayEnabled
    {
        get => _ownShipOverlayEnabled;
        set
        {
            if (SetProperty(ref _ownShipOverlayEnabled, value))
            {
                _settings.OwnShipOverlayEnabled = value;
                _settings.Save();
                OwnShipOverlayEnabledChanged?.Invoke(value);
            }
        }
    }

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

    /// <summary>
    /// Raised when <see cref="AisEnabled"/> changes. The activity bar
    /// wires this (via <see cref="Activities.AisOverlayVisibilitySource"/>)
    /// so the Vessels tab appears / disappears live with the overlay
    /// opt-in.
    /// </summary>
    public event Action<bool>? AisEnabledChanged;

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
                AisEnabledChanged?.Invoke(value);
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

    private double? _aisActivationViewportSpanDegrees;
    /// <summary>
    /// Activation threshold (in degrees of latitude AND longitude)
    /// for the AIS subscription. While the visible viewport's
    /// lat-span or lon-span is wider than this, no traffic is fetched
    /// from aisstream.io. <see langword="null"/> disables the gate
    /// entirely (subscribe immediately on viewer launch). Values
    /// <c>&lt;= 0</c> are normalised to <see langword="null"/> so
    /// users can't accidentally configure a gate that never opens.
    /// </summary>
    public double? AisActivationViewportSpanDegrees
    {
        get => _aisActivationViewportSpanDegrees;
        set
        {
            var normalised = value is { } v && v > 0 ? value : null;
            if (SetProperty(ref _aisActivationViewportSpanDegrees, normalised))
            {
                EnsureAisOverlaySettings();
                _settings.AisOverlay!.ActivationViewportSpanDegrees = normalised;
                _settings.Save();
            }
        }
    }
}