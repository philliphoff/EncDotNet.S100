using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Routing.Persistence;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Tests.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Library's rows and workflows (#1394) over a test's library, loader and
/// downloader, wired as the app wires them.
/// </summary>
internal sealed class TestLibrary
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryLoader _loader;
    private readonly ILibraryDownloader _downloader;

    /// <summary>Builds the rows and workflows.</summary>
    /// <param name="library">The library.</param>
    /// <param name="loader">The loader.</param>
    /// <param name="downloader">The downloader.</param>
    /// <param name="time">The clock rows read.</param>
    /// <param name="timeFormat">The Local/UTC setting rows read; UTC when <see langword="null"/>.</param>
    /// <param name="notifications">Where the download workflow announces unpacked packages; a headless service when <see langword="null"/>.</param>
    public TestLibrary(
        CollectionLibrary library,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        TimeProvider? time = null,
        ITimeFormatProvider? timeFormat = null,
        INotificationService? notifications = null)
    {
        _library = library;
        _loader = loader;
        _downloader = downloader;
        Notifications = notifications ?? TestNotifications.Create();
        Names = new LibrarySourceNames(library);
        Open = new OpenLibraryItemsWorkflow(loader, Names);
        Download = new DownloadLibraryItemsWorkflow(library, downloader, loader, Open, Notifications);
        Rows = new LibraryRows(library, loader, downloader, Download, time ?? TimeProvider.System, timeFormat ?? new UtcFormat());
        MarkFinal = new MarkFinalStormsWorkflow(library, loader, downloader, Open, time ?? TimeProvider.System);
        LoadLatest = new LoadLatestWeatherRunWorkflow(library, downloader, Rows, Download, Open, MarkFinal);
        PinRun = new PinWeatherRunWorkflow(library, downloader);
        Storms = new StormAdvisoryWatch(library, MarkFinal, LoadLatest, Notifications);
        Routes = new RoutesService(new ClockViewTime(TimeProvider.System));
        OpenAsRoute = new OpenLibraryItemAsRouteWorkflow(Open, new LibraryRouteOpener(
            loader,
            new ReceivedRoutesService(
                new FakeDatasetLoaderService(),
                Routes,
                new CheckSettingsStore(),
                closeDataset: null,
                notifications: null,
                follow: null,
                read: ReceivedRoutesService.ReadDataset),
            () => { },
            Notifications));
    }

    public INotificationService Notifications { get; }

    public LibrarySourceNames Names { get; }

    public OpenLibraryItemsWorkflow Open { get; }

    public DownloadLibraryItemsWorkflow Download { get; }

    public LibraryRows Rows { get; }

    public OpenLibraryItemAsRouteWorkflow OpenAsRoute { get; }

    public MarkFinalStormsWorkflow MarkFinal { get; }

    public LoadLatestWeatherRunWorkflow LoadLatest { get; }

    public PinWeatherRunWorkflow PinRun { get; }

    public StormAdvisoryWatch Storms { get; }

    /// <summary>A weather run monitor over these workflows, on <paramref name="time"/>'s minute clock.</summary>
    public WeatherRunMonitor Monitor(TimeProvider time, Action<Action> dispatch) =>
        new(_library, Rows, LoadLatest, Storms, time, dispatch);

    /// <summary>The routes Open as route opens items into.</summary>
    public RoutesService Routes { get; }

    /// <summary>
    /// A Library panel over <paramref name="library"/>, with the rows and
    /// workflows the app gives it.
    /// </summary>
    public static LibraryPanelViewModel Panel(
        CollectionLibrary library,
        ILibraryImporter importer,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        Action<Action> dispatch,
        Action<Action> coalesce,
        TimeProvider time,
        Func<CollectionSource, FeedHealth?>? feedHealth,
        INotificationService? notifications,
        GlobalTimeService? viewTime,
        ITimeFormatProvider? timeFormat,
        LibrarySync? sync) =>
        new TestLibrary(library, loader, downloader, time, timeFormat, notifications)
            .CreatePanel(importer, dispatch, coalesce, time, feedHealth, viewTime, timeFormat, sync);

    /// <summary>A Library panel, and its tree and details, over these rows and workflows.</summary>
    public LibraryPanelViewModel CreatePanel(
        ILibraryImporter importer,
        Action<Action> dispatch,
        Action<Action> coalesce,
        TimeProvider time,
        Func<CollectionSource, FeedHealth?>? feedHealth,
        GlobalTimeService? viewTime,
        ITimeFormatProvider? timeFormat,
        LibrarySync? sync)
    {
        var tree = new LibraryTreeViewModel(
            _library, importer, new LibraryNodeStatus(_downloader, Download, sync, feedHealth, time), timeFormat, dispatch, time);
        var details = new LibraryDetailsViewModel(_library, _downloader, Open, Download, OpenAsRoute, LoadLatest, PinRun, viewTime);
        return new(_library, tree, details, _loader, _downloader, Rows, Open, Download,
            dispatch, coalesce, time, viewTime, timeFormat);
    }

    private sealed class UtcFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
