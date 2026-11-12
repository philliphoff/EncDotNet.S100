using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Builds Object Information as the app does (#1401), with stubs for what a
/// test doesn't care about: no warnings panel, the eXaminer off, Local times
/// in metres, a light theme and a view time of now.
/// </summary>
internal static class TestPickReport
{
    /// <summary>A pick report; picks published on <paramref name="picks"/> show in it.</summary>
    public static PickReportViewModel Create(
        IPickService? picks = null,
        INavigationalWarningNavigator? navigator = null,
        ObjectInformationSelection? selection = null,
        ExaminerLinks? examinerLinks = null,
        ITimeFormatProvider? timeFormat = null,
        IMarinerSettingsProvider? marinerSettings = null,
        IViewTime? viewTime = null,
        IThemeService? theme = null)
    {
        timeFormat ??= new TestStationCharts.FixedTimeFormat(TimeFormat.Local);
        return new PickReportViewModel(
            picks ?? new StubPickService(),
            TestStationCharts.Context(timeFormat: timeFormat, marinerSettings: marinerSettings, theme: theme),
            examinerLinks ?? Examiner(),
            new PickWarningCardsViewModel(
                navigator ?? new NoWarnings(),
                viewTime ?? new ClockViewTime(TimeProvider.System),
                timeFormat),
            selection ?? new ObjectInformationSelection());
    }

    /// <summary>eXaminer links over <paramref name="links"/>, or switched off.</summary>
    public static ExaminerLinks Examiner(IS100ExaminerLinkBuilder? links = null, IUrlOpener? opener = null) =>
        new(links ?? new S100ExaminerLinkBuilder(new ViewerSettings { S100ExaminerLinksEnabled = false }), opener ?? new NoUrlOpener());

    /// <summary>Shows a pick of a single feature, as a pick service would publish it.</summary>
    public static void SetPick(
        this PickReportViewModel report,
        string featureType,
        string? featureTypeName,
        string featureRef,
        string? datasetFileName,
        string? productSpec,
        IReadOnlyList<PickAttribute> attributes) =>
        report.SetPicks(
        [
            new PickHit
            {
                FeatureType = featureType,
                FeatureTypeName = featureTypeName,
                FeatureRef = featureRef,
                DatasetFileName = datasetFileName,
                ProductSpec = productSpec,
                Attributes = attributes,
            },
        ]);

    /// <summary>Shows a pick of <paramref name="hits"/>, as a pick service would publish it.</summary>
    public static void SetPicks(
        this PickReportViewModel report,
        IReadOnlyList<PickHit> hits,
        IReadOnlyList<DynamicPickHit>? dynamicHits = null,
        PickLocation? location = null,
        LocationDepthResult? locationDepth = null) =>
        report.SetPicks(new PickResult(hits, dynamicHits ?? [], location, locationDepth));

    /// <summary>A warnings panel with no warnings.</summary>
    private sealed class NoWarnings : INavigationalWarningNavigator
    {
        public IReadOnlyList<NavigationalWarningEntry> Warnings => [];
        public event EventHandler? WarningsChanged { add { } remove { } }
        public NavigationalWarningEntry? Find(string datasetId, string featureRef) => null;
        public bool Select(NavigationalWarningEntry entry) => false;
        public bool ZoomTo(NavigationalWarningEntry entry) => false;
    }

    private sealed class NoUrlOpener : IUrlOpener
    {
        public void Open(string url) { }
    }
}

/// <summary>Builds the context the station and depth charts follow, for tests.</summary>
internal static class TestStationCharts
{
    /// <summary>
    /// A chart context: no Timeline time unless <paramref name="time"/> has
    /// one, Local times, metres, and a light theme unless given.
    /// </summary>
    public static StationChartContext Context(
        GlobalTimeService? time = null,
        ITimeFormatProvider? timeFormat = null,
        IThemeService? theme = null,
        IMarinerSettingsProvider? marinerSettings = null) =>
        new(
            time ?? new GlobalTimeService(),
            timeFormat ?? new FixedTimeFormat(TimeFormat.Local),
            marinerSettings ?? new FixedMariner(),
            theme ?? new LightTheme());

    /// <summary>A time format that doesn't change.</summary>
    internal sealed class FixedTimeFormat(TimeFormat format) : ITimeFormatProvider
    {
        public TimeFormat Current => format;
        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedMariner : IMarinerSettingsProvider
    {
        public MarinerSettings Current => MarinerSettings.Default;
        public event Action<MarinerSettings>? Changed { add { } remove { } }
    }

    private sealed class LightTheme : IThemeService
    {
        public bool IsDarkTheme => false;
        public event EventHandler? ThemeChanged { add { } remove { } }
        public bool ToggleTheme() => false;
        public ChromeTheme Current => ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { }
    }
}
