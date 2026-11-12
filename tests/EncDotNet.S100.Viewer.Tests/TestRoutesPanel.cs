using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Builds the Routes panel as the app does (#1400): over its Check route
/// content and exchange footer, with inert stand-ins (no datasets, no route
/// checks, no dialogs, no map, no warnings panel) for what a test doesn't pass.
/// </summary>
internal static class TestRoutesPanel
{
    /// <summary>A warnings panel with no warnings, for a findings list that doesn't link to one.</summary>
    public static INavigationalWarningNavigator NoWarnings { get; } = new NoNavigator();

    /// <summary>A Routes panel over <paramref name="routes"/>.</summary>
    public static RoutesPanelViewModel Create(
        RoutesService routes,
        PassageCheckService? check = null,
        RouteFindingsService? findings = null,
        INavigationalWarningNavigator? navigator = null,
        RouteExchangeService? exchange = null,
        IRouteDialogs? dialogs = null,
        IMarinerSettingsProvider? marinerSettings = null,
        ITimeFormatProvider? timeFormat = null,
        ICapabilityAccessor<IMapViewportController>? viewport = null)
    {
        var formatting = Formatting(timeFormat, marinerSettings);
        return new RoutesPanelViewModel(
            routes,
            viewport ?? new NoViewport(),
            formatting,
            Check(routes, check, findings, navigator, formatting),
            new RouteExchangeFooterViewModel(routes, exchange ?? Exchange(routes), dialogs ?? new NoDialogs()));
    }

    /// <summary>The Check route content over <paramref name="check"/>, or over a check with no datasets and no route checks.</summary>
    public static PassageCheckViewModel Check(
        RoutesService routes,
        PassageCheckService? check = null,
        RouteFindingsService? findings = null,
        INavigationalWarningNavigator? navigator = null,
        DisplayFormatting? formatting = null)
    {
        check ??= new PassageCheckService(
            routes,
            new PassageCheckFixture.FixedCatalog([]),
            PassageCheckFixture.SerialPost,
            new FixedWeatherLimits(),
            PassageCheckFixture.NewSources(),
            checks: () => [],
            warnings: null,
            mariner: null,
            options: null,
            debounce: TimeSpan.Zero);
        findings ??= new RouteFindingsService(
            check, routes, new PassageCheckFixture.FixedCatalog([]), viewport: null, mariner: null, timeFormat: null, warnings: null);
        return new PassageCheckViewModel(
            check,
            routes,
            formatting ?? Formatting(),
            findings,
            new RouteFindingsViewModel(findings, navigator ?? NoWarnings));
    }

    /// <summary>The display formatting over the given providers: Local times and the default mariner settings (metres) unless passed.</summary>
    public static DisplayFormatting Formatting(ITimeFormatProvider? timeFormat = null, IMarinerSettingsProvider? marinerSettings = null) =>
        new(timeFormat ?? new FixedTimeFormat(TimeFormat.Local), marinerSettings ?? new DefaultMariner());

    private static RouteExchangeService Exchange(RoutesService routes) =>
        new(
            routes,
            new ReceivedRoutesService(
                new FakeDatasetLoaderService(),
                routes,
                checkSettings: new Routing.Persistence.CheckSettingsStore(),
                closeDataset: null,
                notifications: null,
                follow: null,
                read: ReceivedRoutesService.ReadDataset),
            formats: Routing.Exchange.RouteFormatRegistry.Default,
            openDataset: null,
            findDataset: null);

    private sealed class FixedTimeFormat(TimeFormat format) : ITimeFormatProvider
    {
        public TimeFormat Current => format;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class DefaultMariner : IMarinerSettingsProvider
    {
        public MarinerSettings Current => MarinerSettings.Default;

        public event Action<MarinerSettings>? Changed { add { } remove { } }
    }

    private sealed class NoViewport : ICapabilityAccessor<IMapViewportController>
    {
        public IMapViewportController? Current => null;
    }

    /// <summary>Dialogs that pick no file and show nothing.</summary>
    private sealed class NoDialogs : IRouteDialogs
    {
        public Task<string?> PickImportFileAsync(IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);

        public Task<string?> PickExportFileAsync(string suggestedName, string formatName, string extension) => Task.FromResult<string?>(null);

        public void Show<T>(T dialog, double maxWidth)
            where T : class
        {
        }

        public void Close(object dialog)
        {
        }
    }

    private sealed class NoNavigator : INavigationalWarningNavigator
    {
        public IReadOnlyList<NavigationalWarningEntry> Warnings => [];

        public event EventHandler? WarningsChanged { add { } remove { } }

        public NavigationalWarningEntry? Find(string datasetId, string featureRef) => null;

        public bool Select(NavigationalWarningEntry entry) => false;

        public bool ZoomTo(NavigationalWarningEntry entry) => false;
    }
}
