using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Settings → Chart display: basemap, scale indicators, mariner settings,
/// water level adjustment and the route's weather limits (#845, #1398).
/// </summary>
/// <remarks>
/// Depth values are stored in metres; the <c>*Display</c> strings show them
/// in the selected depth unit, and the service reads what the user types.
/// </remarks>
internal sealed class ChartSettingsPageViewModel : SettingsPageViewModel
{
    /// <summary>Creates the page.</summary>
    /// <param name="settings">The settings it shows and edits.</param>
    /// <param name="tideStationList">The station list line under the tide predictions switch.</param>
    public ChartSettingsPageViewModel(ViewerSettingsService settings, TideStationListViewModel tideStationList)
        : base(settings)
    {
        ArgumentNullException.ThrowIfNull(tideStationList);
        TideStationList = tideStationList;
        AvailableWaterLevelAdjustments =
        [
            new(Services.Depth.WaterLevelAdjustmentMode.Off, Strings.Settings_WaterLevelAdjustment_Off),
            new(Services.Depth.WaterLevelAdjustmentMode.AtViewTime, Strings.Settings_WaterLevelAdjustment_AtViewTime),
        ];
    }

    /// <inheritdoc />
    public override SettingsCategory Category => SettingsCategory.Chart;

    /// <inheritdoc />
    public override string Title => Strings.Settings_Category_Chart;

    /// <inheritdoc />
    public override int Order => 30;

    /// <inheritdoc />
    public override string AutomationId => "Settings.Category.Chart";

    /// <summary>The depth units offered.</summary>
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
    /// in <see cref="ViewerSettingsService.BuildMarinerSettings"/>).
    /// </summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } = BuildLanguageOptions();

    /// <summary>The selectable basemap modes.</summary>
    public static BasemapMode[] AvailableBasemapModes { get; } =
        [BasemapMode.None, BasemapMode.Offline, BasemapMode.Online];

    /// <summary>The water level adjustment choices.</summary>
    public IReadOnlyList<WaterLevelAdjustmentOption> AvailableWaterLevelAdjustments { get; }

    /// <summary>The Check against choices: the 34, 50 and 64 kt radii.</summary>
    public IReadOnlyList<CycloneRadiusOption> AvailableCycloneCheckAgainst { get; } =
        [.. Enum.GetValues<Navigation.TropicalCycloneRadius>().Order().Select(CycloneRadiusOption.Of)];

    /// <summary>The Must review inside choices: 64 kt down to Check against, then Never.</summary>
    public IReadOnlyList<CycloneRadiusOption> AvailableCycloneMustReviewInside =>
    [
        .. Enum.GetValues<Navigation.TropicalCycloneRadius>().Where(r => (int)r >= Settings.CycloneCheckAgainstKnots).OrderDescending().Select(CycloneRadiusOption.Of),
        new CycloneRadiusOption(0, Strings.Settings_CycloneNever),
    ];

    /// <inheritdoc cref="ViewerSettingsService.SelectedDepthUnit" />
    public DepthUnit SelectedDepthUnit
    {
        get => Settings.SelectedDepthUnit;
        set => Settings.SelectedDepthUnit = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.FourShades" />
    public bool FourShades
    {
        get => Settings.FourShades;
        set => Settings.FourShades = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.ShallowWaterDangers" />
    public bool ShallowWaterDangers
    {
        get => Settings.ShallowWaterDangers;
        set => Settings.ShallowWaterDangers = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.PlainBoundaries" />
    public bool PlainBoundaries
    {
        get => Settings.PlainBoundaries;
        set => Settings.PlainBoundaries = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SimplifiedSymbols" />
    public bool SimplifiedSymbols
    {
        get => Settings.SimplifiedSymbols;
        set => Settings.SimplifiedSymbols = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.FullLightLines" />
    public bool FullLightLines
    {
        get => Settings.FullLightLines;
        set => Settings.FullLightLines = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.RadarOverlay" />
    public bool RadarOverlay
    {
        get => Settings.RadarOverlay;
        set => Settings.RadarOverlay = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.IgnoreScaleMinimum" />
    public bool IgnoreScaleMinimum
    {
        get => Settings.IgnoreScaleMinimum;
        set => Settings.IgnoreScaleMinimum = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.NationalLanguage" />
    public string NationalLanguage
    {
        get => Settings.NationalLanguage;
        set => Settings.NationalLanguage = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.WaterLevelAdjustment" />
    public Services.Depth.WaterLevelAdjustmentMode WaterLevelAdjustment
    {
        get => Settings.WaterLevelAdjustment;
        set => Settings.WaterLevelAdjustment = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.WaterLevelApplyUndeclaredDatum" />
    public bool WaterLevelApplyUndeclaredDatum
    {
        get => Settings.WaterLevelApplyUndeclaredDatum;
        set => Settings.WaterLevelApplyUndeclaredDatum = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.UseTidePredictions" />
    public bool UseTidePredictions
    {
        get => Settings.UseTidePredictions;
        set => Settings.UseTidePredictions = value;
    }

    /// <summary>The station list's date and refresh, under the tide predictions switch (D15).</summary>
    public TideStationListViewModel TideStationList { get; }

    /// <inheritdoc cref="ViewerSettingsService.CycloneCheckAgainstKnots" />
    public int CycloneCheckAgainstKnots
    {
        get => Settings.CycloneCheckAgainstKnots;
        set => Settings.CycloneCheckAgainstKnots = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.CycloneReportClosestApproach" />
    public bool CycloneReportClosestApproach
    {
        get => Settings.CycloneReportClosestApproach;
        set => Settings.CycloneReportClosestApproach = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.CycloneMustReviewInsideKnots" />
    public int CycloneMustReviewInsideKnots
    {
        get => Settings.CycloneMustReviewInsideKnots;
        set => Settings.CycloneMustReviewInsideKnots = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.CycloneMappingText" />
    public string CycloneMappingText => Settings.CycloneMappingText;

    /// <inheritdoc cref="ViewerSettingsService.SelectedBasemapMode" />
    public BasemapMode SelectedBasemapMode
    {
        get => Settings.SelectedBasemapMode;
        set => Settings.SelectedBasemapMode = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.ShowOutOfScaleExtentIndicators" />
    public bool ShowOutOfScaleExtentIndicators
    {
        get => Settings.ShowOutOfScaleExtentIndicators;
        set => Settings.ShowOutOfScaleExtentIndicators = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.ShowOverscaleIndication" />
    public bool ShowOverscaleIndication
    {
        get => Settings.ShowOverscaleIndication;
        set => Settings.ShowOverscaleIndication = value;
    }

    /// <summary>The safety contour as typed and shown, in the depth unit.</summary>
    public string SafetyContourDisplay
    {
        get => DepthFormatting.Format(Settings.SafetyContour, Settings.SelectedDepthUnit);
        set
        {
            if (Settings.TryParseDepth(value, out var m))
                Settings.SafetyContour = m;
        }
    }

    /// <summary>The safety depth as typed and shown, in the depth unit.</summary>
    public string SafetyDepthDisplay
    {
        get => DepthFormatting.Format(Settings.SafetyDepth, Settings.SelectedDepthUnit);
        set
        {
            if (Settings.TryParseDepth(value, out var m))
                Settings.SafetyDepth = m;
        }
    }

    /// <summary>The shallow contour as typed and shown, in the depth unit.</summary>
    public string ShallowContourDisplay
    {
        get => DepthFormatting.Format(Settings.ShallowContour, Settings.SelectedDepthUnit);
        set
        {
            if (Settings.TryParseDepth(value, out var m))
                Settings.ShallowContour = m;
        }
    }

    /// <summary>The deep contour as typed and shown, in the depth unit.</summary>
    public string DeepContourDisplay
    {
        get => DepthFormatting.Format(Settings.DeepContour, Settings.SelectedDepthUnit);
        set
        {
            if (Settings.TryParseDepth(value, out var m))
                Settings.DeepContour = m;
        }
    }

    /// <summary>True when chart-wide water level adjustment is on.</summary>
    public bool IsWaterLevelAdjustmentOn => Settings.WaterLevelAdjustment != Services.Depth.WaterLevelAdjustmentMode.Off;

    /// <summary>The wind limit as the Settings box shows it, in knots; empty when off.</summary>
    public string RouteMaxWindDisplay
    {
        get => Settings.RouteMaxWindKnots is { } k ? k.ToString("0.#", CultureInfo.CurrentCulture) : string.Empty;
        set
        {
            if (ViewerSettingsService.TryParseRouteMaxWind(value, out var knots))
                Settings.RouteMaxWindKnots = knots;
            else
                OnPropertyChanged(nameof(RouteMaxWindDisplay));
        }
    }

    /// <summary>"Wave height limit (m)" or "(ft)", by the depth unit.</summary>
    public string RouteMaxWaveLabel => string.Format(
        CultureInfo.CurrentCulture, Strings.Settings_RouteMaxWaveFormat, Settings.WavesInMetres ? "m" : "ft");

    /// <summary>The wave limit as the Settings box shows it, in metres or feet; empty when off.</summary>
    public string RouteMaxWaveDisplay
    {
        get => Settings.RouteMaxWaveHeightMetres is { } m
            ? (Settings.WavesInMetres ? m : Quantities.Length.FromMetres(m).TotalFeet).ToString("0.#", CultureInfo.CurrentCulture)
            : string.Empty;
        set
        {
            if (Settings.TryParseRouteMaxWave(value, out var metres))
                Settings.RouteMaxWaveHeightMetres = metres;
            else
                OnPropertyChanged(nameof(RouteMaxWaveDisplay));
        }
    }

    /// <inheritdoc />
    protected override void OnSettingChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(ViewerSettingsService.SelectedDepthUnit):
                // Re-emit display strings so bound TextBoxes refresh.
                OnPropertyChanged(nameof(SafetyContourDisplay));
                OnPropertyChanged(nameof(SafetyDepthDisplay));
                OnPropertyChanged(nameof(ShallowContourDisplay));
                OnPropertyChanged(nameof(DeepContourDisplay));
                OnPropertyChanged(nameof(RouteMaxWaveDisplay));
                OnPropertyChanged(nameof(RouteMaxWaveLabel));
                break;
            case nameof(ViewerSettingsService.SafetyContour):
                OnPropertyChanged(nameof(SafetyContourDisplay));
                break;
            case nameof(ViewerSettingsService.SafetyDepth):
                OnPropertyChanged(nameof(SafetyDepthDisplay));
                break;
            case nameof(ViewerSettingsService.ShallowContour):
                OnPropertyChanged(nameof(ShallowContourDisplay));
                break;
            case nameof(ViewerSettingsService.DeepContour):
                OnPropertyChanged(nameof(DeepContourDisplay));
                break;
            case nameof(ViewerSettingsService.WaterLevelAdjustment):
                OnPropertyChanged(nameof(IsWaterLevelAdjustmentOn));
                break;
            case nameof(ViewerSettingsService.RouteMaxWindKnots):
                OnPropertyChanged(nameof(RouteMaxWindDisplay));
                break;
            case nameof(ViewerSettingsService.RouteMaxWaveHeightMetres):
                OnPropertyChanged(nameof(RouteMaxWaveDisplay));
                break;
            case nameof(ViewerSettingsService.CycloneCheckAgainstKnots):
                OnPropertyChanged(nameof(AvailableCycloneMustReviewInside));
                break;
        }
    }

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
}

/// <summary>A water level adjustment choice in Mariner Settings.</summary>
/// <param name="Mode">The setting.</param>
/// <param name="DisplayName">Its label.</param>
internal sealed record WaterLevelAdjustmentOption(Services.Depth.WaterLevelAdjustmentMode Mode, string DisplayName);

/// <summary>A tropical cyclone radius choice in Settings (#1299): its knots (0 for Never) and "34 kt radius".</summary>
internal sealed record CycloneRadiusOption(int Knots, string DisplayName)
{
    /// <summary>The choice of a radius.</summary>
    public static CycloneRadiusOption Of(Navigation.TropicalCycloneRadius radius) =>
        new((int)radius, string.Format(CultureInfo.CurrentCulture, Strings.Settings_CycloneRadiusFormat, (int)radius));
}
