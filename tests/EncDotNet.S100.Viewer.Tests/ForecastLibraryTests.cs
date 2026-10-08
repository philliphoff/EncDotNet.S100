using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// S-100 forecast feeds in the Library (#685, NOAA's S-111; handoff A3, B2–B5,
/// B7, C1, C3–C5): one row per model with its run and time left, a newer run as
/// an update, and Expired once a downloaded run's window has passed.
/// </summary>
public sealed class ForecastLibraryTests : IDisposable
{
    private static readonly Uri ModelsUri = new("https://noaa-s111-pds.s3.amazonaws.com/ed1.0.1/model_forecast_guidance/");

    private static readonly ForecastModel Cbofs = new("cbofs", "Chesapeake Bay", 6, 48);

    private static readonly ForecastModel Nyofs = new("nyofs", "Port of New York and New Jersey", 6, 54);

    /// <summary>The latest cbofs run the catalogue lists; nyofs's latest is an hour later.</summary>
    private static readonly DateTimeOffset Run = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private readonly LibraryTestContext _context = new();
    private readonly FakeTimeProvider _time = new(Run.AddHours(1));
    private readonly RunDownloader _downloader;
    private readonly CollectionLibrary _library;

    public ForecastLibraryTests()
    {
        _downloader = new RunDownloader(Path.Combine(_context.Root, "downloads"));
        _library = _context.CreateService(new CollectionIndexer([new RunIndexer()]));
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private static CollectionItem Tile(ForecastModel model, string cell, DateTimeOffset run) => new()
    {
        Key = $"{model.Id}/111US00_{model.Id.ToUpperInvariant()}_{cell}",
        ProductSpec = "S-111",
        Name = $"111US00_{model.Id.ToUpperInvariant()}_{cell}",
        Title = model.Name,
        Bounds = new GeoBounds(37.5, -76.8, 37.8, -76.5),
        Location = new RemoteItemLocation(new Uri(ModelsUri, $"{model.Id}/x/{cell}.h5"), 500_000, run),
        Properties = new Dictionary<string, string>
        {
            [S100ForecastFeedIndexer.ModelProperty] = model.Id,
            [S100ForecastFeedIndexer.RunProperty] = run.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            [S100ForecastFeedIndexer.ValidToProperty] = run.AddHours(model.HorizonHours).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["notForNavigation"] = "true",
        },
    };

    private async Task<LibraryPanelViewModel> PanelAsync(Services.GlobalTimeService? viewTime = null, Services.ITimeFormatProvider? timeFormat = null)
    {
        _library.Initialize();
        _library.AddCollection("S-111", [new S100ForecastFeedSource(Guid.NewGuid(), null, ModelsUri, [Cbofs, Nyofs])]);
        await _library.WhenIdle();
        var panel = new LibraryPanelViewModel(
            _library, new NullImporter(), new NullLoader(), _downloader, action => action(), time: _time, viewTime: viewTime, timeFormat: timeFormat);
        panel.Sync();
        panel.SelectedNode = Source(panel);
        return panel;
    }

    private static LibraryNodeViewModel Source(LibraryPanelViewModel panel) => Assert.Single(Assert.Single(panel.Nodes).Children);

    private static LibraryItemViewModel Model(LibraryPanelViewModel panel, string id) =>
        panel.Items.Single(i => i.IsModelHeader && i.Name == id);

    [Fact]
    public async Task Each_model_is_one_row_with_its_run_and_time_left_expanding_to_untagged_tiles()
    {
        using var panel = await PanelAsync();

        Assert.Equal(["cbofs", "nyofs"], panel.Items.Select(i => i.Name));
        var cbofs = Model(panel, "cbofs");
        Assert.True(cbofs.IsNameMono);
        Assert.Equal("Chesapeake Bay", cbofs.Subtitle);
        Assert.StartsWith("S-111 · run ", cbofs.Summary, StringComparison.Ordinal);
        Assert.EndsWith("· 2 tiles · " + LibraryItemViewModel.FormatBytes(1_000_000), cbofs.Summary, StringComparison.Ordinal);
        Assert.True(cbofs.HasForecastWindow);
        Assert.Equal("47 h left", cbofs.TimeLeftText);
        Assert.InRange(cbofs.WindowElapsed, 0.02, 0.03);
        Assert.False(cbofs.IsWindowEnded);
        Assert.Equal(LibraryPrimaryAvailability.Online, cbofs.PrimaryAvailability);
        Assert.Empty(cbofs.Tags);
        Assert.Contains("nothing local", Source(panel).StatusLine, StringComparison.Ordinal);

        cbofs.ToggleCommand!.Execute(null);

        var tiles = panel.Items.Where(i => i.IsForecast && !i.IsModelHeader).ToArray();
        Assert.Equal(2, tiles.Length);
        Assert.All(tiles, t =>
        {
            Assert.Empty(t.Tags);
            Assert.False(t.HasForecastWindow);
            Assert.Null(t.Subtitle);
            Assert.Equal("S-111 · " + LibraryItemViewModel.FormatBytes(500_000), t.Summary);
        });
        Assert.Equal("Run", tiles[0].Details[0].Fields[0].Label);
    }

    [Fact]
    public async Task A_newer_run_is_an_update_that_replaces_the_whole_run()
    {
        _downloader.Have("111US00_CBOFS_US4VA1DD", Run.AddHours(-6));
        _downloader.Have("111US00_CBOFS_US4VA1DE", Run.AddHours(-6));
        using var panel = await PanelAsync();

        var cbofs = Model(panel, "cbofs");
        Assert.Equal(LibraryAvailability.Outdated, cbofs.Availability);
        Assert.Equal(LibraryPrimaryAvailability.Update, cbofs.PrimaryAvailability);
        Assert.Equal(["New run"], cbofs.Tags.Select(t => t.Text));
        Assert.Equal("41 h left", cbofs.TimeLeftText);  // the downloaded 12:00Z run's window

        Assert.Equal("1 new run · " + LibraryItemViewModel.FormatBytes(1_000_000), panel.BulkSummary);
        Assert.Equal("cbofs 18:00Z replaces 12:00Z", panel.BulkScope);
        Assert.Equal("Update 1", panel.DownloadListedText);
        Assert.Equal(2, panel.UpdatesCount);
        Assert.Contains("newer run for 1 model", Source(panel).StatusLine, StringComparison.Ordinal);
        Assert.Equal(LibraryNodeStatusKind.Warning, Source(panel).StatusKind);

        panel.DownloadListedCommand.Execute(null);

        Assert.Equal(["111US00_CBOFS_US4VA1DD", "111US00_CBOFS_US4VA1DE"], _downloader.Requested.Order());
    }

    [Fact]
    public async Task A_downloaded_run_expires_when_its_window_ends_and_the_bar_offers_to_check()
    {
        _downloader.Have("111US00_NYOFS_US4NY1AP", Run.AddHours(1));
        using var panel = await PanelAsync();
        var nyofs = Model(panel, "nyofs");
        Assert.Equal(LibraryAvailability.Local, nyofs.Availability);
        Assert.Contains("1 of 2 runs local · 54 h left", Source(panel).StatusLine, StringComparison.Ordinal);
        Assert.False(panel.IsCheckForRuns);

        // 55 h after the 19:00Z run: an hour past its 54 h window. The minute clock re-evaluates.
        _time.Advance(TimeSpan.FromHours(55));

        nyofs = Model(panel, "nyofs");
        Assert.Equal(LibraryAvailability.Expired, nyofs.Availability);
        Assert.Equal(LibraryPrimaryAvailability.Expired, nyofs.PrimaryAvailability);
        Assert.Equal(["Expired"], nyofs.Tags.Select(t => t.Text));
        Assert.True(nyofs.IsWindowEnded);
        Assert.Equal("Ended 1 h ago", nyofs.TimeLeftText);
        nyofs.ToggleCommand!.Execute(null);
        var tile = panel.Items.Single(i => i.Name == "111US00_NYOFS_US4NY1AP");
        Assert.Equal(LibraryAvailability.Expired, tile.Availability);
        Assert.True(tile.CanLoad);  // an expired run still loads, for looking back
        Assert.Empty(tile.Tags);
        Assert.Equal(1, panel.UpdatesCount);
        Assert.True(panel.IsCheckForRuns);
        Assert.Equal("1 run expired", panel.BulkSummary);
        Assert.False(panel.HasDownloadable);
        Assert.Contains("1 runs expired · Refresh to look for new runs", Source(panel).StatusLine, StringComparison.Ordinal);
        Assert.Equal(LibraryNodeStatusKind.Error, Source(panel).StatusKind);
    }

    [Fact]
    public async Task Forecast_sources_are_tagged_AWS()
    {
        using var panel = await PanelAsync();

        Assert.Equal("AWS", Source(panel).KindTag);
    }

    [Fact]
    public async Task The_models_step_lists_models_notes_overlaps_and_builds_the_source()
    {
        var known = KnownCatalogueSources.Find("noaa-s111")!;
        IReadOnlyList<ForecastModelSummary> Summaries(IReadOnlyList<ForecastModel> models) => models.Select(m => m.Id switch
        {
            "cbofs" => new ForecastModelSummary(m, Run, 58, 25_000_000, 12_000_000, new GeoBounds(36.5, -77.5, 39.7, -75.5))
            {
                TileBounds = [new GeoBounds(37.5, -76.8, 37.8, -76.5), new GeoBounds(37.8, -76.8, 38.1, -76.5)],
            },
            "rtofs_east" => new ForecastModelSummary(m, Run, 21, 6_000_000, 17_000_000, new GeoBounds(19, -98, 46, -60))
            {
                TileBounds = [new GeoBounds(33.6, -81.6, 38.4, -76.8), new GeoBounds(33.6, -76.8, 38.4, -72)],
            },
            "dbofs" => new ForecastModelSummary(m, null, 0, null, null, null, "404 Not Found"),
            "tbofs" => new ForecastModelSummary(m, Run, 13, 5_000_000, 3_000_000, new GeoBounds(27, -83, 28.5, -82))
            {
                TileBounds = [new GeoBounds(27.5, -82.8, 27.8, -82.5)],
            },
            _ => new ForecastModelSummary(m, Run, 5, 2_000_000, 1_000_000, new GeoBounds(-60, 100, -59, 101)),
        }).ToArray();
        var vm = new AddToLibraryDialogViewModel(_library, null,
            loadForecastModels: (uri, models, _) => Task.FromResult(Summaries(models)));

        vm.Initialize(known, targetCollectionId: null);
        Assert.Equal(AddToLibraryKind.S100Forecast, vm.Kind);
        Assert.True(vm.IsS100Forecast);
        Assert.True(vm.HasReviewUse);
        Assert.Contains("Refresh looks for a newer run", vm.ReviewUpdates, StringComparison.Ordinal);

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.Equal(14, vm.ForecastModels.Count);
        var cbofs = vm.ForecastModels.Single(o => o.Value == "cbofs");
        Assert.Equal("Chesapeake Bay", cbofs.Label);
        Assert.Equal("cbofs · every 6 h · 58 tiles · " + LibraryItemViewModel.FormatBytes(25_000_000), cbofs.Detail);
        // rtofs_east's tiles cover cbofs's, not tbofs's (outside them); nothing covers rtofs_east.
        Assert.Equal("Overlaps cbofs", vm.ForecastModels.Single(o => o.Value == "rtofs_east").Note);
        Assert.Null(cbofs.Note);
        Assert.Null(vm.ForecastModels.Single(o => o.Value == "tbofs").Note);
        Assert.Equal("rtofs_east · daily · 21 tiles · " + LibraryItemViewModel.FormatBytes(6_000_000),
            vm.ForecastModels.Single(o => o.Value == "rtofs_east").Detail);
        Assert.Equal("dbofs · catalogue unavailable", vm.ForecastModels.Single(o => o.Value == "dbofs").Detail);

        vm.SelectedForecastShape = vm.ForecastShapes[1];
        Assert.Equal("cbofs · every 6 h · one file · " + LibraryItemViewModel.FormatBytes(12_000_000), cbofs.Detail);

        cbofs.IsSelected = true;
        Assert.Equal("1 models · " + LibraryItemViewModel.FormatBytes(12_000_000) + " per run", vm.SelectionSummary);
        Assert.Equal("NOAA S-111 Surface currents — Chesapeake Bay", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<S100ForecastFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(known.CatalogUri, source.ModelsUri);
        Assert.Equal([Cbofs], source.Models);
        Assert.Equal(ForecastShape.Regional, source.Shape);
    }

    // ── the Timeline link (#711) ───────────────────────────────────────

    private Services.LibraryTimeSource TimeSource(LibraryPanelViewModel panel) =>
        new(panel, _library, new NullLoader(), _time, action => action());

    [Fact]
    public async Task The_timeline_knows_each_tiles_window_online_on_disk_and_a_newer_run()
    {
        _downloader.Have("111US00_CBOFS_US4VA1DD", Run.AddHours(-6));
        using var panel = await PanelAsync();

        var entries = TimeSource(panel).Entries;

        var dd = entries.Where(e => e.Name == "111US00_CBOFS_US4VA1DD").OrderBy(e => e.Start).ToArray();
        Assert.Equal(2, dd.Length);
        Assert.Equal((Services.LibraryTimedState.OnDisk, Run.AddHours(-6).UtcDateTime, Run.AddHours(42).UtcDateTime), (dd[0].State, dd[0].Start, dd[0].End));
        Assert.False(dd[0].IsNewRun);
        Assert.Equal((Services.LibraryTimedState.Online, Run.UtcDateTime, Run.AddHours(48).UtcDateTime), (dd[1].State, dd[1].Start, dd[1].End));
        Assert.True(dd[1].IsNewRun);
        Assert.Equal(500_000, dd[1].SizeBytes);
        Assert.Equal("cbofs/US4VA1DD", dd[1].MatchKey);

        var de = Assert.Single(entries, e => e.Name == "111US00_CBOFS_US4VA1DE");
        Assert.Equal(Services.LibraryTimedState.Online, de.State);
        Assert.False(de.IsNewRun);
        var ny = Assert.Single(entries, e => e.Model == "nyofs");
        Assert.Equal((Run.AddHours(1).UtcDateTime, Run.AddHours(55).UtcDateTime), (ny.Start, ny.End));
        Assert.EndsWith(":nyofs/111US00_NYOFS_US4NY1AP", ny.ItemId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_on_disk_whose_window_ended_is_expired_on_the_timeline()
    {
        _downloader.Have("111US00_CBOFS_US4VA1DD", Run);
        _time.Advance(TimeSpan.FromHours(60));
        using var panel = await PanelAsync();

        var entry = Assert.Single(TimeSource(panel).Entries, e => e.Name == "111US00_CBOFS_US4VA1DD");

        Assert.Equal(Services.LibraryTimedState.OnDisk, entry.State);
        Assert.True(entry.IsExpired);
    }

    [Fact]
    public async Task The_at_time_facet_lists_what_is_valid_at_the_view_time()
    {
        var viewTime = new Services.GlobalTimeService();
        var samples = Enumerable.Range(0, 49).Select(h => Run.UtcDateTime.AddHours(h)).ToArray();
        viewTime.ApplySnapshot(new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[2],
            Samples = samples,
            CoverageSegments = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset("111US00_CBOFS_US4VA1DD", samples[0], samples[^1])],
        });
        using var panel = await PanelAsync(viewTime);

        Assert.True(panel.HasViewTime);
        Assert.Equal(3, panel.AtViewTimeCount);

        viewTime.SetCurrentTime(Run.UtcDateTime.AddHours(50));
        Assert.Equal(1, panel.AtViewTimeCount);
        panel.IsAtViewTime = true;
        Assert.Equal(["nyofs"], panel.Items.Where(i => i.IsModelHeader).Select(i => i.Name));
        // The toggle narrows the segments too.
        Assert.Equal(1, panel.AllCount);
        Assert.Equal(1, panel.OnlineCount);
    }

    [Fact]
    public async Task Reveal_selects_a_tile_and_opens_its_model()
    {
        using var panel = await PanelAsync();
        panel.FilterText = "nyofs";
        LibraryItemViewModel? revealed = null;
        panel.Revealed += (_, item) => revealed = item;
        var source = Source(panel).Source!.Id;

        Assert.True(panel.Reveal(source, "cbofs/111US00_CBOFS_US4VA1DE"));

        Assert.Equal("111US00_CBOFS_US4VA1DE", panel.SelectedItem?.Name);
        Assert.Same(panel.SelectedItem, revealed);
        Assert.Equal(string.Empty, panel.FilterText);
        Assert.False(panel.Reveal(source, "cbofs/nothing"));
    }

    [Fact]
    public async Task A_row_shows_its_window_on_the_timeline_and_goes_to_its_run()
    {
        var viewTime = new Services.GlobalTimeService();
        var samples = Enumerable.Range(0, 3).Select(h => Run.UtcDateTime.AddHours(h)).ToArray();
        viewTime.ApplySnapshot(new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset("x", samples[0], samples[^1])],
        });
        using var panel = await PanelAsync(viewTime);
        (DateTime, DateTime)? shown = null;
        DateTime? went = null;
        panel.ShowOnTimelineRequested += (_, window) => shown = window;
        panel.GoToTimeRequested += (_, at) => went = at;

        panel.SelectedItem = Model(panel, "nyofs");
        Assert.True(panel.HasValidWindow);
        panel.ShowOnTimelineCommand.Execute(null);
        panel.GoToRunStartCommand.Execute(null);

        Assert.Equal((Run.AddHours(1).UtcDateTime, Run.AddHours(55).UtcDateTime), shown);
        Assert.Equal(Run.AddHours(1).UtcDateTime, went);
    }

    [Theory]
    [InlineData("view_time", true, null)]
    [InlineData("2026-09-30T20:00:00Z", false, "2026-09-30T20:00:00Z")]
    public void Query_library_items_takes_validAt(string validAt, bool atViewTime, string? at)
    {
        var (query, error) = QueryLibraryItemsTool.Parse(
            new QueryLibraryItemsRequest(null, null, null, null, null, null, null, null, null, null, null, null, validAt));

        Assert.Null(error);
        Assert.Equal(atViewTime, query!.ValidAtViewTime);
        Assert.Equal(at is null ? null : DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal), query.ValidAt);
        Assert.NotNull(QueryLibraryItemsTool.Parse(
            new QueryLibraryItemsRequest(null, null, null, null, null, null, null, null, null, null, null, null, "soon")).Error);
    }

    [Fact]
    public async Task Run_times_follow_the_users_Local_or_UTC_setting()
    {
        var format = new SwitchableFormat { Current = TimeFormat.Local };
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        _time.SetLocalTimeZone(tokyo);
        using var panel = await PanelAsync(timeFormat: format);
        var cbofs = Model(panel, "cbofs");
        var local = TimeZoneInfo.ConvertTimeFromUtc(Run.UtcDateTime, tokyo);
        var c = System.Globalization.CultureInfo.CurrentCulture;

        Assert.Contains($"run {local.ToString("d", c)} {local.ToString("t", c)}", cbofs.Summary, StringComparison.Ordinal);

        var changed = new List<string?>();
        cbofs.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        format.Switch(TimeFormat.Utc);

        Assert.Contains(nameof(LibraryItemViewModel.Summary), changed);
        Assert.Contains($"run {Run.UtcDateTime.ToString("d", c)} 18:00Z", cbofs.Summary, StringComparison.Ordinal);
    }

    private sealed class SwitchableFormat : Services.ITimeFormatProvider
    {
        public TimeFormat Current { get; set; }

        public event Action<TimeFormat>? TimeFormatChanged;

        public void Switch(TimeFormat format)
        {
            Current = format;
            TimeFormatChanged?.Invoke(format);
        }
    }

    private sealed class RunIndexer : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is S100ForecastFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIndex(source.Id, Run.AddHours(1), null,
            [
                Tile(Cbofs, "US4VA1DD", Run),
                Tile(Cbofs, "US4VA1DE", Run),
                Tile(Nyofs, "US4NY1AP", Run.AddHours(1)),
            ], [])
            { PublishedAt = Run.AddHours(1) });
    }

    /// <summary>Tracks which tiles are downloaded (as real files), from which run.</summary>
    private sealed class RunDownloader(string root) : ILibraryDownloader
    {
        private readonly Dictionary<string, DateTimeOffset> _runs = [];

        public List<string> Requested { get; } = [];

        public event EventHandler? Changed { add { } remove { } }

        public void Have(string name, DateTimeOffset run)
        {
            Directory.CreateDirectory(Path.Combine(root, name));
            File.WriteAllBytes(Path.Combine(root, name, name + ".h5"), [1]);
            _runs[name] = run;
        }

        public CollectionItem Localize(CollectionItem item) => _runs.ContainsKey(item.Name)
            ? item with { Location = new LocalItemLocation(Path.Combine(root, item.Name), item.Name + ".h5", []) }
            : item;

        public bool IsOutdated(CollectionItem item) =>
            _runs.TryGetValue(item.Name, out var local) && (item.Location as RemoteItemLocation)?.LastModified > local;

        public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => _runs.TryGetValue(item.Name, out var local) ? local : null;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Requested.AddRange(items.Select(i => i.Name));
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    private sealed class NullLoader : ILibraryLoader
    {
        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => default;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryLoadResult(0, 0));
    }

    private sealed class NullImporter : ILibraryImporter
    {
        public Task AddFolderAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddOnlineCatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddSharedFeedAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddKnownCatalogueAsync(KnownCatalogueSource source, Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddCollectionManifestAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source) => Task.CompletedTask;

        public Task AddPathAsync(string path, Guid? targetCollectionId) => Task.CompletedTask;

        public bool IsInLibrary(string path) => false;
    }
}
