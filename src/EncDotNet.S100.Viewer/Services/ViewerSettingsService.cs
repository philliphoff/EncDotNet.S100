using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's settings (#1398): loads them from <see cref="ViewerSettings"/>,
/// validates and normalises each change, saves it, and raises a typed event
/// for the parts of the app that follow it. It also pushes the tiled renderer's
/// knobs into <see cref="ChartEngine.Options"/>. The Settings pages bind to it
/// through their own view models; the rest of the app reads it directly.
/// </summary>
/// <remarks>
/// It raises <see cref="INotifyPropertyChanged.PropertyChanged"/> for each
/// property, so a page shows a change made elsewhere (the palette menu, MCP,
/// the chrome theme resetting the palette).
/// </remarks>
internal sealed class ViewerSettingsService : ObservableObject, Depth.IWaterLevelAdjustmentSettings, PassageCheck.IRouteWeatherLimits
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

    private ChromeTheme _selectedChromeTheme;

    /// <summary>
    /// User-selected chrome theme (Light / Dark / S100Night).
    /// Persisted to <see cref="ViewerSettings.ChromeTheme"/>. The
    /// setter only updates persisted state and fires
    /// <see cref="ChromeThemeChanged"/>; the host (App.axaml.cs) is
    /// responsible for translating the change into an
    /// <see cref="IThemeService.SetTheme"/> call and for resetting
    /// <see cref="SelectedPalette"/> to the default for the new
    /// chrome. Splitting the responsibility keeps the settings
    /// free of an IThemeService dependency.
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
    /// settings file has been saved. <see cref="TimeFormatProvider"/>
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
                RaiseMarinerChanged();
            }
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
                RaiseMarinerChanged();
            }
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
                RaiseMarinerChanged();
            }
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
                RaiseMarinerChanged();
            }
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

    // ── Chart-wide water level adjustment (#1132) ──────────────────────────
    // A viewer setting, not a MarinerSettings member: which water level to
    // add to the charted depths is a host decision
    // (docs/design/chart-wide-water-level-adjustment.md §6).

    private Depth.WaterLevelAdjustmentMode _waterLevelAdjustment;

    /// <inheritdoc />
    public Depth.WaterLevelAdjustmentMode WaterLevelAdjustment
    {
        get => _waterLevelAdjustment;
        set
        {
            if (SetProperty(ref _waterLevelAdjustment, value))
            {
                _settings.WaterLevelAdjustment = value.ToString();
                RaiseWaterLevelAdjustmentChanged();
            }
        }
    }

    private bool _waterLevelApplyUndeclaredDatum;

    /// <inheritdoc />
    public bool WaterLevelApplyUndeclaredDatum
    {
        get => _waterLevelApplyUndeclaredDatum;
        set
        {
            if (SetProperty(ref _waterLevelApplyUndeclaredDatum, value))
            {
                _settings.WaterLevelApplyUndeclaredDatum = value;
                RaiseWaterLevelAdjustmentChanged();
            }
        }
    }

    private bool _useTidePredictions;

    /// <inheritdoc />
    public bool UseTidePredictions
    {
        get => _useTidePredictions;
        set
        {
            if (SetProperty(ref _useTidePredictions, value))
            {
                _settings.UseTidePredictions = value;
                RaiseWaterLevelAdjustmentChanged();
            }
        }
    }

    /// <inheritdoc />
    public event Action? WaterLevelAdjustmentChanged;

    private void RaiseWaterLevelAdjustmentChanged()
    {
        _settings.Save();
        WaterLevelAdjustmentChanged?.Invoke();
    }

    // ── Weather on a route (#1093) ─────────────────────────────────────────
    // Check route's weather limits: kept with the mariner settings, not with a
    // route, and raised on their own event so a change re-runs the route check
    // without re-portraying the chart.

    private double? _routeMaxWindKnots;

    /// <summary>The wind limit in knots, or <c>null</c> when wind isn't checked.</summary>
    public double? RouteMaxWindKnots
    {
        get => _routeMaxWindKnots;
        set
        {
            var v = value is { } x && double.IsFinite(x) && x > 0 ? x : (double?)null;
            if (SetProperty(ref _routeMaxWindKnots, v))
            {
                _settings.RouteMaxWindKnots = v;
                RaiseRouteWeatherLimitsChanged();
            }
        }
    }

    private double? _routeMaxWaveHeightMetres;

    /// <summary>The significant wave height limit in metres, or <c>null</c> when waves aren't checked.</summary>
    public double? RouteMaxWaveHeightMetres
    {
        get => _routeMaxWaveHeightMetres;
        set
        {
            var v = value is { } x && double.IsFinite(x) && x > 0 ? x : (double?)null;
            if (SetProperty(ref _routeMaxWaveHeightMetres, v))
            {
                _settings.RouteMaxWaveHeightMetres = v;
                RaiseRouteWeatherLimitsChanged();
            }
        }
    }

    /// <inheritdoc />
    public EncDotNet.S100.Navigation.WeatherLimits? Limits =>
        _routeMaxWindKnots is null && _routeMaxWaveHeightMetres is null
            ? null
            : new EncDotNet.S100.Navigation.WeatherLimits
            {
                MaxWind = _routeMaxWindKnots is { } k ? Quantities.Speed.FromKnots(k) : null,
                MaxWaveHeight = _routeMaxWaveHeightMetres is { } m ? Quantities.Length.FromMetres(m) : null,
            };

    /// <summary>Whether wave heights show in metres (the depth unit is metres) or in feet, as the findings do.</summary>
    public bool WavesInMetres => _selectedDepthUnit == DepthUnit.Metres;

    /// <summary>Reads a depth typed in the depth unit, in metres.</summary>
    /// <param name="text">The text as typed.</param>
    /// <param name="metres">The depth in metres.</param>
    /// <returns><see langword="true"/> when the text is a depth.</returns>
    public bool TryParseDepth(string? text, out double metres) =>
        DepthFormatting.TryParse(text ?? string.Empty, _selectedDepthUnit, out metres);

    /// <summary>Reads a wind limit typed in knots: empty turns it off.</summary>
    /// <param name="text">The text as typed.</param>
    /// <param name="knots">The limit, or <see langword="null"/> for off.</param>
    /// <returns><see langword="true"/> when the text is empty or a positive number.</returns>
    public static bool TryParseRouteMaxWind(string? text, out double? knots)
    {
        knots = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var k) && k > 0)
        {
            knots = k;
            return true;
        }

        return false;
    }

    /// <summary>Reads a wave limit typed in metres or feet (see <see cref="WavesInMetres"/>): empty turns it off.</summary>
    /// <param name="text">The text as typed.</param>
    /// <param name="metres">The limit in metres, or <see langword="null"/> for off.</param>
    /// <returns><see langword="true"/> when the text is empty or a positive number.</returns>
    public bool TryParseRouteMaxWave(string? text, out double? metres)
    {
        metres = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var h) && h > 0)
        {
            metres = WavesInMetres ? h : Quantities.Length.FromFeet(h).TotalMetres;
            return true;
        }

        return false;
    }

    // ── Tropical cyclones on a route (#1299, design note V39) ─────────────
    // The cyclone rule's settings: Check against (34/50/64 kt), Report closest
    // approach, and Must review inside (64/50/34 kt or Never, never outside
    // Check against). Raised on RouteWeatherLimitsChanged too.

    private Navigation.TropicalCycloneRadius _cycloneCheckAgainst = Navigation.TropicalCycloneRadius.Knots34;
    private bool _cycloneReportClosestApproach = true;
    private Navigation.TropicalCycloneRadius? _cycloneMustReviewInside = Navigation.TropicalCycloneRadius.Knots50;

    /// <summary>The radius checked against, in knots: 34, 50 or 64.</summary>
    public int CycloneCheckAgainstKnots
    {
        get => (int)_cycloneCheckAgainst;
        set
        {
            if (!Enum.IsDefined((Navigation.TropicalCycloneRadius)value) || (int)_cycloneCheckAgainst == value)
                return;
            _cycloneCheckAgainst = (Navigation.TropicalCycloneRadius)value;
            _settings.RouteCycloneCheckAgainstKnots = value;
            OnPropertyChanged();

            // Narrowing Check against past Must review inside moves it in too.
            if (_cycloneMustReviewInside is { } inside && inside < _cycloneCheckAgainst)
            {
                _cycloneMustReviewInside = _cycloneCheckAgainst;
                _settings.RouteCycloneMustReviewInsideKnots = value;
            }

            OnPropertyChanged(nameof(CycloneMustReviewInsideKnots));
            OnPropertyChanged(nameof(CycloneMappingText));
            RaiseRouteWeatherLimitsChanged();
        }
    }

    /// <summary>Whether the closest approach to each storm is reported.</summary>
    public bool CycloneReportClosestApproach
    {
        get => _cycloneReportClosestApproach;
        set
        {
            if (SetProperty(ref _cycloneReportClosestApproach, value))
            {
                _settings.RouteCycloneReportClosestApproach = value;
                RaiseRouteWeatherLimitsChanged();
            }
        }
    }

    /// <summary>The weakest radius that is Must review, in knots (34, 50 or 64), or 0 for never.</summary>
    public int CycloneMustReviewInsideKnots
    {
        get => _cycloneMustReviewInside is { } r ? (int)r : 0;
        set
        {
            Navigation.TropicalCycloneRadius? radius = value == 0 ? null : (Navigation.TropicalCycloneRadius)value;
            if ((radius is { } r && (!Enum.IsDefined(r) || r < _cycloneCheckAgainst)) || radius == _cycloneMustReviewInside)
                return;
            _cycloneMustReviewInside = radius;
            _settings.RouteCycloneMustReviewInsideKnots = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CycloneMappingText));
            RaiseRouteWeatherLimitsChanged();
        }
    }

    /// <summary>
    /// The line under the rows that spells out the mapping: "Inside the 50 kt
    /// and 64 kt radii: Must review. The 34 kt radius and the cone: Check."
    /// </summary>
    public string CycloneMappingText => CycloneMapping(Cyclones);

    /// <summary>The mapping line for a set of cyclone settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The line.</returns>
    internal static string CycloneMapping(Navigation.TropicalCycloneCheckSettings settings)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var tested = settings.TestedRadii;
        if (settings.EffectiveMustReviewInside is not { } inside)
            return Strings.Settings_CycloneMappingNever;
        static string Radii(IReadOnlyList<Navigation.TropicalCycloneRadius> radii) => radii.Count switch
        {
            1 => string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Settings_CycloneOneRadiusFormat, (int)radii[0]),
            2 => string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Settings_CycloneTwoRadiiFormat, (int)radii[0], (int)radii[1]),
            _ => string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Settings_CycloneThreeRadiiFormat, (int)radii[0], (int)radii[1], (int)radii[2]),
        };
        var must = tested.Where(r => r >= inside).ToList();
        var check = tested.Where(r => r < inside).ToList();
        var rest = check.Count == 0 ? Strings.Settings_CycloneConeOnly : string.Format(culture, Strings.Settings_CycloneRestFormat, Radii(check));
        return string.Format(culture, Strings.Settings_CycloneMappingFormat, Radii(must), rest);
    }

    /// <inheritdoc />
    public Navigation.TropicalCycloneCheckSettings Cyclones => new()
    {
        CheckAgainst = _cycloneCheckAgainst,
        ReportClosestApproach = _cycloneReportClosestApproach,
        MustReviewInside = _cycloneMustReviewInside,
    };

    /// <inheritdoc />
    public event Action? RouteWeatherLimitsChanged;

    private void RaiseRouteWeatherLimitsChanged()
    {
        _settings.Save();
        RouteWeatherLimitsChanged?.Invoke();
    }

    // ── Base-plane tiled-pipeline knobs ────────────────────────────────────
    // The tiled base-plane renderer's scene-mode and cache/prediction knobs. All
    // values mirror ChartEngine.Options (the renderer source of truth) and
    // persist to ViewerSettings; an explicit environment variable pins a knob (the
    // perf harness), in which case the matching *Editable flag is false and the
    // control is shown disabled.

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
                ChartEngine.Options.SceneMode = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the scene-mode selector is user-editable (not env-pinned).</summary>
    public bool SceneModeEditable => !ChartEngine.Options.SceneModeEnvExplicit;

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
            ChartEngine.Options.TileGutterDip = value;
            var effective = ChartEngine.Options.TileGutterDip;
            if (SetProperty(ref _tileGutterDip, effective))
            {
                _settings.TileGutterDip = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the gutter knob is user-editable (not env-pinned).</summary>
    public bool TileGutterDipEditable => !ChartEngine.Options.TileGutterDipEnvExplicit;

    private double _tileBudgetMb;
    /// <summary>
    /// Process-wide hot-cache native budget, in MB, shared by every layer's
    /// tiles. Applies at once. Disabled when pinned by
    /// <c>S100_VECTOR_TILE_BUDGET_MB</c>.
    /// </summary>
    public double TileBudgetMb
    {
        get => _tileBudgetMb;
        set
        {
            ChartEngine.Options.TileBudgetMb = value;
            var effective = ChartEngine.Options.TileBudgetMb;
            if (SetProperty(ref _tileBudgetMb, effective))
            {
                _settings.TileBudgetMb = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the in-memory budget knob is user-editable (not env-pinned).</summary>
    public bool TileBudgetMbEditable => !ChartEngine.Options.TileBudgetMbEnvExplicit;

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
                ChartEngine.Options.TilePredictionEnabled = value;
                _settings.TilePredictionEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the prediction knob is user-editable (not env-pinned).</summary>
    public bool TilePredictionEditable => !ChartEngine.Options.TilePredictionEnvExplicit;

    private bool _tileCrossBandPrewarmEnabled;
    /// <summary>
    /// Whether idle cross-band (±1) pre-warm is enabled (issue&#160;#428). Read
    /// every frame, so the change takes effect live. Not user-editable (see
    /// <see cref="TileCrossBandPrewarmEditable"/>) when pinned by
    /// <c>S100_VECTOR_TILE_XBAND</c> — env pinning can force it either on or off.
    /// </summary>
    public bool TileCrossBandPrewarmEnabled
    {
        get => _tileCrossBandPrewarmEnabled;
        set
        {
            ChartEngine.Options.TileCrossBandPrewarmEnabled = value;
            var effective = ChartEngine.Options.TileCrossBandPrewarmEnabled;
            if (SetProperty(ref _tileCrossBandPrewarmEnabled, effective))
            {
                _settings.TileCrossBandPrewarmEnabled = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the cross-band pre-warm knob is user-editable (not env-pinned).</summary>
    public bool TileCrossBandPrewarmEditable => !ChartEngine.Options.TileCrossBandPrewarmEnvExplicit;

    private bool _tileMetatileEnabled;
    /// <summary>
    /// Whether adjacent cold misses are batched into 2&#215;2 metatile jobs
    /// (issue&#160;#427). Read by each worker job, so the change takes effect
    /// live. Not editable when pinned by <c>S100_VECTOR_TILE_METATILE</c>.
    /// </summary>
    public bool TileMetatileEnabled
    {
        get => _tileMetatileEnabled;
        set
        {
            ChartEngine.Options.TileMetatileEnabled = value;
            var effective = ChartEngine.Options.TileMetatileEnabled;
            if (SetProperty(ref _tileMetatileEnabled, effective))
            {
                _settings.TileMetatileEnabled = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the metatile knob is user-editable (not env-pinned).</summary>
    public bool TileMetatileEditable => !ChartEngine.Options.TileMetatileEnvExplicit;

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
                ChartEngine.Options.TileDiskCacheEnabled = value;
                _settings.TileDiskCacheEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the disk-cache knob is user-editable (not env-pinned).</summary>
    public bool TileDiskCacheEditable => !ChartEngine.Options.TileDiskCacheEnvExplicit;

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
            ChartEngine.Options.TileDiskMb = value;
            var effective = ChartEngine.Options.TileDiskMb;
            if (SetProperty(ref _tileDiskMb, effective))
            {
                _settings.TileDiskMb = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the disk-budget knob is user-editable (not env-pinned).</summary>
    public bool TileDiskMbEditable => !ChartEngine.Options.TileDiskMbEnvExplicit;

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
                ChartEngine.Options.TileGpuResidencyEnabled = value;
                _settings.TileGpuResidencyEnabled = value;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the GPU-residency knob is user-editable (not env-pinned).</summary>
    public bool TileGpuResidencyEditable => !ChartEngine.Options.TileGpuResidencyEnvExplicit;

    private double _tileGpuBudgetMb;
    /// <summary>
    /// Process-wide GPU-residency budget, in MB, shared by every layer's
    /// textures. Applies at once. Disabled when pinned by
    /// <c>S100_VECTOR_TILE_GPU_MB</c>.
    /// </summary>
    public double TileGpuBudgetMb
    {
        get => _tileGpuBudgetMb;
        set
        {
            ChartEngine.Options.TileGpuBudgetMb = value;
            var effective = ChartEngine.Options.TileGpuBudgetMb;
            if (SetProperty(ref _tileGpuBudgetMb, effective))
            {
                _settings.TileGpuBudgetMb = effective;
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the GPU-budget knob is user-editable (not env-pinned).</summary>
    public bool TileGpuBudgetMbEditable => !ChartEngine.Options.TileGpuBudgetMbEnvExplicit;

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
            ChartEngine.Options.Profile = value;
            if (SetProperty(ref _performanceProfile, value))
            {
                _settings.PerformanceProfile = value.ToString();
                _tileBudgetMb = ChartEngine.Options.TileBudgetMb;
                _tileGpuBudgetMb = ChartEngine.Options.TileGpuBudgetMb;
                _tileDiskMb = ChartEngine.Options.TileDiskMb;
                // ApplyProfile re-derives the cross-band pre-warm default from the
                // new tier (off on LowEnd), so mirror it back or the toggle would
                // drift from the renderer after a profile switch (issue #428).
                _tileCrossBandPrewarmEnabled = ChartEngine.Options.TileCrossBandPrewarmEnabled;
                OnPropertyChanged(nameof(TileBudgetMb));
                OnPropertyChanged(nameof(TileGpuBudgetMb));
                OnPropertyChanged(nameof(TileDiskMb));
                OnPropertyChanged(nameof(TileCrossBandPrewarmEnabled));
                OnPropertyChanged(nameof(ResolvedProfileLabel));
                RaiseMarinerChanged();
            }
        }
    }

    /// <summary>Whether the profile dropdown is user-editable (not env-pinned).</summary>
    public bool PerformanceProfileEditable => !ChartEngine.Options.ProfileEnvExplicit;

    /// <summary>The concrete tier Auto resolves to on this host, for display.</summary>
    public string ResolvedProfileLabel => ChartEngine.Options.ResolvedProfile.ToString();



    /// <summary>
    /// Raised when <see cref="SelectedBasemapMode"/> changes so the host
    /// can swap the basemap layer live without a restart.
    /// </summary>
    public event Action<BasemapMode>? BasemapModeChanged;

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

    /// <summary>
    /// Raised when <see cref="ShowOutOfScaleExtentIndicators"/> changes so the
    /// extent-indicator overlay can be rebuilt without a relaunch.
    /// </summary>
    public event Action? ExtentIndicatorsChanged;

    private bool _portrayalPrewarmEnabled;
    /// <summary>
    /// Whether cells of load-as-you-pan sets are portrayed in the background so
    /// panning to them later is quick (#1266). Persisted to
    /// <see cref="ViewerSettings.PortrayalPrewarmEnabled"/>; the warmer reads the
    /// setting on every pass, so the change applies at once.
    /// </summary>
    public bool PortrayalPrewarmEnabled
    {
        get => _portrayalPrewarmEnabled;
        set
        {
            if (SetProperty(ref _portrayalPrewarmEnabled, value))
            {
                _settings.PortrayalPrewarmEnabled = value;
                _settings.Save();
            }
        }
    }

    private Scripting.LuaEngineKind _luaEngine;
    /// <summary>
    /// The Lua engine that runs the portrayal rules. Persisted to
    /// <see cref="ViewerSettings.LuaEngine"/>; the pipeline factories are
    /// built at startup, so a change applies after a restart. Disabled when
    /// pinned by <c>S100_LUA_ENGINE</c>.
    /// </summary>
    public Scripting.LuaEngineKind SelectedLuaEngine
    {
        get => _luaEngine;
        set
        {
            if (SetProperty(ref _luaEngine, value))
            {
                _settings.LuaEngine = Scripting.LuaEngineKinds.GetName(value);
                _settings.Save();
            }
        }
    }

    /// <summary>Whether the Lua-engine selector is user-editable (not env-pinned).</summary>
    public bool LuaEngineEditable => !ViewerLuaEngine.IsPinnedByEnvironment;

    private bool _showOutOfScaleExtentIndicators;
    /// <summary>
    /// Whether an accent border is drawn around the extent of a loaded dataset
    /// that has zoomed out past its display-scale minimum (issue #446).
    /// Persisted to <see cref="ViewerSettings.ShowOutOfScaleExtentIndicators"/>;
    /// changing it raises <see cref="ExtentIndicatorsChanged"/>.
    /// </summary>
    public bool ShowOutOfScaleExtentIndicators
    {
        get => _showOutOfScaleExtentIndicators;
        set
        {
            if (SetProperty(ref _showOutOfScaleExtentIndicators, value))
            {
                _settings.ShowOutOfScaleExtentIndicators = value;
                _settings.Save();
                ExtentIndicatorsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Raised when <see cref="ShowOverscaleIndication"/> changes so the overscale
    /// curtain overlay can be rebuilt without a relaunch.
    /// </summary>
    public event Action? OverscaleIndicationChanged;

    private bool _showOverscaleIndication;
    /// <summary>
    /// Whether the on-chart overscale curtain (subtle vertical-line pattern,
    /// S-52 / S-101 <c>AP(OVERSC01)</c>) is drawn over regions displayed beyond
    /// their compilation scale (issue #441, Form A). Persisted to
    /// <see cref="ViewerSettings.ShowOverscaleIndication"/>; changing it raises
    /// <see cref="OverscaleIndicationChanged"/>.
    /// </summary>
    public bool ShowOverscaleIndication
    {
        get => _showOverscaleIndication;
        set
        {
            if (SetProperty(ref _showOverscaleIndication, value))
            {
                _settings.ShowOverscaleIndication = value;
                _settings.Save();
                OverscaleIndicationChanged?.Invoke();
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
    /// current values. Used by <see cref="MarinerSettingsProvider"/>.
    /// </summary>
    public MarinerSettings BuildMarinerSettings() => new()
    {
        SafetyContour = Quantities.Depth.FromMetres(_safetyContour),
        SafetyDepth = Quantities.Depth.FromMetres(_safetyDepth),
        ShallowContour = Quantities.Depth.FromMetres(_shallowContour),
        DeepContour = Quantities.Depth.FromMetres(_deepContour),
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

    /// <summary>Loads the settings from <paramref name="settings"/> and applies the renderer's knobs.</summary>
    /// <param name="settings">The persisted settings, saved on each change.</param>
    public ViewerSettingsService(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
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
        _safetyContour = settings.SafetyContour ?? def.SafetyContour.TotalMetres;
        _safetyDepth = settings.SafetyDepth ?? def.SafetyDepth.TotalMetres;
        _shallowContour = settings.ShallowContour ?? def.ShallowContour.TotalMetres;
        _deepContour = settings.DeepContour ?? def.DeepContour.TotalMetres;
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
        _waterLevelAdjustment = Enum.TryParse<Depth.WaterLevelAdjustmentMode>(settings.WaterLevelAdjustment, out var waterLevelAdjustment)
            ? waterLevelAdjustment
            : Depth.WaterLevelAdjustmentMode.Off;
        _waterLevelApplyUndeclaredDatum = settings.WaterLevelApplyUndeclaredDatum ?? false;
        _useTidePredictions = settings.UseTidePredictions ?? false;
        _routeMaxWindKnots = settings.RouteMaxWindKnots is { } wind && double.IsFinite(wind) && wind > 0 ? wind : null;
        _routeMaxWaveHeightMetres = settings.RouteMaxWaveHeightMetres is { } wave && double.IsFinite(wave) && wave > 0 ? wave : null;
        _cycloneCheckAgainst = settings.RouteCycloneCheckAgainstKnots is { } against && Enum.IsDefined((Navigation.TropicalCycloneRadius)against)
            ? (Navigation.TropicalCycloneRadius)against
            : Navigation.TropicalCycloneRadius.Knots34;
        _cycloneReportClosestApproach = settings.RouteCycloneReportClosestApproach ?? true;
        _cycloneMustReviewInside = settings.RouteCycloneMustReviewInsideKnots switch
        {
            0 => null,
            { } inside when Enum.IsDefined((Navigation.TropicalCycloneRadius)inside) => (Navigation.TropicalCycloneRadius)inside,
            _ => Navigation.TropicalCycloneRadius.Knots50,
        };
        if (_cycloneMustReviewInside is { } stored && stored < _cycloneCheckAgainst)
            _cycloneMustReviewInside = _cycloneCheckAgainst;

        // Tiled base-plane scene mode: push the persisted preference into the
        // renderer (the write is ignored when pinned by an explicit env var — the
        // perf harness), then read it back so an env-pinned value displays the
        // effective (env) value.
        if (Enum.TryParse<VectorSceneMode>(settings.VectorSceneMode, ignoreCase: true, out var sceneMode))
        {
            ChartEngine.Options.SceneMode = sceneMode;
        }

        _sceneMode = ChartEngine.Options.SceneMode;

        // Apply the performance profile first so its derived budgets become the
        // baseline; an explicitly persisted budget/worker value below overrides.
        if (Enum.TryParse<PerformanceProfile>(settings.PerformanceProfile, ignoreCase: true, out var profile))
        {
            ChartEngine.Options.Profile = profile;
        }

        _performanceProfile = ChartEngine.Options.Profile;

        if (settings.TileGutterDip is { } tileGutter)
        {
            ChartEngine.Options.TileGutterDip = tileGutter;
        }

        _tileGutterDip = ChartEngine.Options.TileGutterDip;

        if (settings.TileBudgetMb is { } tileBudget)
        {
            ChartEngine.Options.TileBudgetMb = tileBudget;
        }

        _tileBudgetMb = ChartEngine.Options.TileBudgetMb;

        if (settings.TilePredictionEnabled is { } tilePredict)
        {
            ChartEngine.Options.TilePredictionEnabled = tilePredict;
        }

        _tilePredictionEnabled = ChartEngine.Options.TilePredictionEnabled;

        if (settings.TileCrossBandPrewarmEnabled is { } tileXBand)
        {
            ChartEngine.Options.TileCrossBandPrewarmEnabled = tileXBand;
        }

        _tileCrossBandPrewarmEnabled = ChartEngine.Options.TileCrossBandPrewarmEnabled;

        if (settings.TileMetatileEnabled is { } tileMetatile)
        {
            ChartEngine.Options.TileMetatileEnabled = tileMetatile;
        }

        _tileMetatileEnabled = ChartEngine.Options.TileMetatileEnabled;

        if (settings.TileDiskCacheEnabled is { } tileDisk)
        {
            ChartEngine.Options.TileDiskCacheEnabled = tileDisk;
        }

        _tileDiskCacheEnabled = ChartEngine.Options.TileDiskCacheEnabled;

        if (settings.TileDiskMb is { } tileDiskMb)
        {
            ChartEngine.Options.TileDiskMb = tileDiskMb;
        }

        _tileDiskMb = ChartEngine.Options.TileDiskMb;

        if (settings.TileGpuResidencyEnabled is { } tileGpu)
        {
            ChartEngine.Options.TileGpuResidencyEnabled = tileGpu;
        }

        _tileGpuResidencyEnabled = ChartEngine.Options.TileGpuResidencyEnabled;

        if (settings.TileGpuBudgetMb is { } tileGpuMb)
        {
            ChartEngine.Options.TileGpuBudgetMb = tileGpuMb;
        }

        _tileGpuBudgetMb = ChartEngine.Options.TileGpuBudgetMb;

        _basemapMode = settings.BasemapMode;
        _showOutOfScaleExtentIndicators = settings.ShowOutOfScaleExtentIndicators;
        _portrayalPrewarmEnabled = settings.PortrayalPrewarmEnabled;
        _luaEngine = settings.ResolveLuaEngine();
        _showOverscaleIndication = settings.ShowOverscaleIndication;
        _nationalLanguage = settings.NationalLanguage ?? def.NationalLanguage;

        _mcpEnabled = settings.McpEnabled;
        _mcpPort = settings.McpPort;

        _examinerLinksEnabled = settings.S100ExaminerLinksEnabled;
        _examinerBaseUrl = settings.S100ExaminerBaseUrl ?? ViewerSettings.DefaultS100ExaminerBaseUrl;

        var own = settings.OwnShip ?? new OwnShipSettings();
        _ownShipOverlayEnabled = settings.OwnShipOverlayEnabled;
        _ownShipLength = own.LengthMetres;
        _ownShipBeam = own.BeamMetres;
        _ownShipBowOffset = own.BowOffsetMetres;
        _ownShipPortOffset = own.PortOffsetMetres;


        var ais = settings.AisOverlay ?? new AisOverlaySettings();
        _aisEnabled = ais.Enabled;
        _aisApiKey = ais.ApiKey;
        _aisActivationViewportSpanDegrees = ais.ActivationViewportSpanDegrees;
    }

    /// <summary>
    /// Raised when an MCP-related setting changes so the
    /// <see cref="McpServerHost"/> can reconcile.
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
    // S-100 Feature Catalogue eXaminer deep-links (issue #442).
    // ---------------------------------------------------------------

    private bool _examinerLinksEnabled;
    /// <summary>
    /// Whether feature/attribute rows offer "open in S-100 Feature Catalogue
    /// eXaminer" deep-links. Persisted to
    /// <see cref="ViewerSettings.S100ExaminerLinksEnabled"/>.
    /// </summary>
    public bool ExaminerLinksEnabled
    {
        get => _examinerLinksEnabled;
        set
        {
            if (SetProperty(ref _examinerLinksEnabled, value))
            {
                _settings.S100ExaminerLinksEnabled = value;
                _settings.Save();
                ExaminerSettingsChanged?.Invoke();
            }
        }
    }

    private string _examinerBaseUrl;
    /// <summary>
    /// Base URL of the S-100 Feature Catalogue eXaminer used to build
    /// deep-links. Persisted to
    /// <see cref="ViewerSettings.S100ExaminerBaseUrl"/>.
    /// </summary>
    public string ExaminerBaseUrl
    {
        get => _examinerBaseUrl;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? ViewerSettings.DefaultS100ExaminerBaseUrl
                : value.Trim();
            if (SetProperty(ref _examinerBaseUrl, normalized))
            {
                _settings.S100ExaminerBaseUrl = normalized;
                _settings.Save();
                ExaminerSettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Raised when an examiner setting (<see cref="ExaminerLinksEnabled"/> or
    /// <see cref="ExaminerBaseUrl"/>) changes, so panels that surface
    /// "open in eXaminer" affordances can refresh their availability without
    /// waiting for a reload or the next pick (issue #442).
    /// </summary>
    public event Action? ExaminerSettingsChanged;

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
    /// wires this (via <see cref="ViewModels.Activities.AisOverlayVisibilitySource"/>)
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
    /// preference to <see cref="AisApiKey"/>.
    /// </summary>
    public string AisApiKeyEnvironmentVariable =>
        _settings.AisOverlay?.ApiKeyEnvironmentVariable
        ?? new AisOverlaySettings().ApiKeyEnvironmentVariable;

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
