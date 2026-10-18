using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Navigational warnings panel (#932): grouping, status at the view time,
/// filters, list states, opening a row, and the MCP tools over it.
/// </summary>
public sealed class NavigationalWarningsPanelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ViewAt = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public NavigationalWarningsPanelTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class FakeProcessor(string spec, params (NavigationalWarningInfo Warning, GeoPosition? At)[] parts) : IDatasetProcessor
    {
        public string ProductSpec => spec;
        public SpecRef Spec { get; } = new(spec, default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];

        public IReadOnlyList<NavigationalWarningFeature> GetNavigationalWarnings() =>
            parts.Select((p, i) => new NavigationalWarningFeature($"part.{i}", i, p.Warning)).ToList();

        public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal) => new()
        {
            Primitive = parts[ordinal].At is null ? S100GeometryType.None : S100GeometryType.Point,
            Points = parts[ordinal].At is { } at ? [at] : [],
        };
    }

    private sealed class RecordingPick : IPickService
    {
        public List<(IDatasetProcessor Processor, int Ordinal, string Dataset)> Opened { get; } = [];
        public void HandlePick(Mapsui.MapInfo? mapInfo, IReadOnlyList<DynamicPickHit>? dynamicHits = null) { }
        public void PresentGeographicPick(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features) { }
        public bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference) => false;
        public bool OpenFeature(IDatasetProcessor processor, string featureRef, string datasetFileName) => false;

        public bool OpenFeatureAt(IDatasetProcessor processor, int ordinal, string datasetFileName)
        {
            Opened.Add((processor, ordinal, datasetFileName));
            return true;
        }
    }

    private sealed class ViewTime(DateTimeOffset current) : IViewTime
    {
        public DateTimeOffset Current { get; } = current;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;
        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static NavigationalWarningInfo Warning(
        string series, string number, string type, string area,
        DateTimeOffset from, DateTimeOffset? to = null, DateTimeOffset? published = null) => new()
        {
            Series = series,
            Number = number,
            WarningType = type,
            GeneralArea = area,
            Issuer = "AMSA",
            PublicationTime = published ?? from,
            Validity = [new NavigationalWarningValidity(from, to)],
            Texts = [new NavigationalWarningText("eng", $"{series} {number}")],
        };

    private static DateTimeOffset Oct(int day, int hour = 0) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>AMSA-like set: two NAVAREA X parts, a text-only bulletin, an AUSCOAST part, one ended and one starting later.</summary>
    private sealed class Fixture
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public RecordingPick Pick { get; } = new();
        public RouteFindingsTests.RecordingViewport Viewport { get; } = new();
        public DatasetEntry Entry { get; } = new("/tmp/amsa.gml", "S-124");
        public NavigationalWarningsService Service { get; }
        public NavigationalWarningsViewModel Panel { get; }

        public Fixture(bool viewTime = false, FakeProcessor? processor = null)
        {
            processor ??= new FakeProcessor("S-124",
                (Warning("NAVAREA X", "112/26", "Drifting hazard", "Coral Sea", Oct(13, 6)), new GeoPosition(-15.2, 158.3)),
                (Warning("NAVAREA X", "110/26", "Warnings in force", "Weekly bulletin", Oct(12, 5)), null),
                (Warning("NAVAREA X", "111/26", "Military exercises", "Timor Sea", Oct(13), Oct(24, 23)), new GeoPosition(-11.0, 128.0)),
                (Warning("AUSCOAST", "314/26", "Survey operations", "Bass Strait", Oct(12), Oct(31)), new GeoPosition(-39.0, 141.7)),
                (Warning("NAVAREA X", "108/26", "Survey operations", "Arafura Sea", Oct(1), Oct(19, 23)), new GeoPosition(-10.0, 136.0)),
                (Warning("AUSCOAST", "320/26", "Dredging", "Port Phillip", Oct(18), Oct(30)), new GeoPosition(-38.1, 144.9)));
            Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [Entry] = processor };
            Service = new NavigationalWarningsService(Loader);
            Panel = new NavigationalWarningsViewModel(
                Service,
                Pick,
                viewTime: new ViewTime(viewTime ? ViewAt : Now),
                timeFormat: new Utc(),
                viewport: new StaticCapabilityAccessor<IMapViewportController>(Viewport),
                loader: Loader,
                clock: new FixedClock(Now));
        }

        public NavigationalWarningRowItem Row(string number) =>
            Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Number == number);
    }

    private static string Shape(NavigationalWarningsViewModel panel) => string.Join(" | ", panel.Items.Select(i => i switch
    {
        NavigationalWarningGroupItem g => $"[{g.Name} {g.Count} {g.Source}]",
        NavigationalWarningRowItem r => r.Number,
        _ => "?",
    }));

    [Fact]
    public void Lists_parts_grouped_by_series_with_the_newest_number_first()
    {
        var f = new Fixture();

        Assert.Equal(
            "[AUSCOAST 1 AMSA] | 314/26 | [NAVAREA X 4 AMSA] | 112/26 | 111/26 | 110/26 | 108/26 | [Not in force now 1 ]",
            Shape(f.Panel).Replace("Not in force at view time", "Not in force now", StringComparison.Ordinal));
        Assert.Equal("5 in force", f.Panel.InForceText);
        Assert.Equal("1 text-only", f.Panel.TextOnlyText);
    }

    [Fact]
    public void Text_only_row_says_no_location_and_carries_the_document_glyph()
    {
        var row = new Fixture().Row("110/26");

        Assert.True(row.IsTextOnly);
        Assert.Equal(FluentIcons.Common.Icon.Document, row.Glyph);
        Assert.Equal("No location · Weekly bulletin · from 12 Oct", row.SubText);
        Assert.Equal("Coral Sea · from 13 Oct", new Fixture().Row("112/26").SubText);
        Assert.Equal("Timor Sea · 13 Oct – 24 Oct", new Fixture().Row("111/26").SubText);
    }

    [Fact]
    public void Text_only_count_filters_the_list()
    {
        var f = new Fixture();

        f.Panel.ToggleTextOnlyCommand.Execute(null);

        Assert.Equal("[NAVAREA X 1 AMSA] | 110/26", Shape(f.Panel));
        f.Panel.ToggleTextOnlyCommand.Execute(null);
        Assert.Equal(5, f.Panel.Items.OfType<NavigationalWarningRowItem>().Count());
    }

    [Fact]
    public void Parts_not_in_force_at_view_time_drop_into_a_collapsed_dimmed_group()
    {
        var f = new Fixture(viewTime: true);

        // At 20 Oct 12:00, 108/26 has ended and 320/26 has started.
        Assert.Equal(
            "[AUSCOAST 2 AMSA] | 320/26 | 314/26 | [NAVAREA X 3 AMSA] | 112/26 | 111/26 | 110/26 | [Not in force at view time 1 ]",
            Shape(f.Panel));
        Assert.Equal("20 Oct 12:00", f.Panel.ViewTimeText);

        f.Panel.ToggleNotInForceCommand.Execute(null);
        var ended = f.Row("108/26");
        Assert.False(ended.IsInForce);
        Assert.Equal(0.62, ended.RowOpacity);
        Assert.Equal("Ended 19 Oct · NAVAREA X", ended.SubText);
    }

    [Fact]
    public void Now_reads_status_at_the_clock_not_the_view_time()
    {
        var f = new Fixture(viewTime: true);

        f.Panel.IsNowFilter = true;
        f.Panel.IsNotInForceExpanded = true;

        Assert.Equal(string.Empty, f.Panel.ViewTimeText);
        Assert.Equal("Starts 18 Oct · AUSCOAST", f.Row("320/26").SubText);
        Assert.True(f.Row("108/26").IsInForce);
        Assert.Contains(f.Panel.Items.OfType<NavigationalWarningGroupItem>(), g => g.Name == "Not in force now");
    }

    [Theory]
    [InlineData("314", "314/26")]
    [InlineData("military", "111/26")]
    [InlineData("bulletin", "110/26")]
    public void Search_matches_number_type_or_area(string query, string expected)
    {
        var f = new Fixture();

        f.Panel.Query = query;

        Assert.Equal(expected, Assert.Single(f.Panel.Items.OfType<NavigationalWarningRowItem>()).Number);
    }

    [Fact]
    public void Search_with_no_match_shows_the_no_matches_state()
    {
        var f = new Fixture();

        f.Panel.Query = "zzz";

        Assert.True(f.Panel.IsNoMatchesState);
        Assert.False(f.Panel.HasList);
    }

    [Fact]
    public void Sort_by_published_puts_the_most_recent_first()
    {
        var f = new Fixture();

        f.Panel.SortIndex = 1;

        Assert.Equal(NavigationalWarningsSort.Published, f.Panel.Sort);
        var navarea = f.Panel.Items.SkipWhile(i => i is not NavigationalWarningGroupItem { Name: "NAVAREA X" }).Skip(1)
            .TakeWhile(i => i is NavigationalWarningRowItem).Cast<NavigationalWarningRowItem>().Select(r => r.Number);
        Assert.Equal(["112/26", "111/26", "110/26", "108/26"], navarea);
    }

    [Fact]
    public void Opening_an_in_force_part_with_a_location_opens_its_card_and_frames_it()
    {
        var f = new Fixture();

        f.Panel.SelectedItem = f.Row("112/26");

        var opened = Assert.Single(f.Pick.Opened);
        Assert.Equal(0, opened.Ordinal);
        Assert.Equal("amsa.gml", opened.Dataset);
        var zoom = Assert.Single(f.Viewport.Zooms);
        Assert.Equal(2 * NavigationalWarningsViewModel.PointFrameHalfMetres, zoom.Width, 3);
        Assert.Null(f.Panel.MapNote);
    }

    [Fact]
    public void Opening_a_part_not_in_force_opens_its_card_but_leaves_the_map()
    {
        var f = new Fixture(viewTime: true);
        f.Panel.IsNotInForceExpanded = true;

        f.Panel.SelectedItem = f.Row("108/26");

        Assert.Single(f.Pick.Opened);
        Assert.Empty(f.Viewport.Zooms);
        Assert.Equal("NAVAREA X 108/26 isn't in force at view time, so the map wasn't moved.", f.Panel.MapNote);
        Assert.False(f.Panel.IsMapNoteTextOnly);
    }

    [Fact]
    public void Opening_a_text_only_part_shows_the_in_map_note()
    {
        var f = new Fixture();

        f.Panel.SelectedItem = f.Row("110/26");

        Assert.Equal(1, Assert.Single(f.Pick.Opened).Ordinal);
        Assert.Empty(f.Viewport.Zooms);
        Assert.Equal("NAVAREA X 110/26 has no location, so nothing is drawn for it.", f.Panel.MapNote);
        Assert.True(f.Panel.IsMapNoteTextOnly);
        Assert.True(f.Panel.HasMapNote);
    }

    [Fact]
    public void Group_headers_are_not_selectable()
    {
        var f = new Fixture();

        f.Panel.SelectedItem = f.Panel.Items[0];

        Assert.Null(f.Panel.SelectedItem);
        Assert.Empty(f.Pick.Opened);
    }

    [Fact]
    public void No_S124_datasets_shows_the_no_sources_state()
    {
        var loader = new FakeDatasetLoaderService();
        var panel = new NavigationalWarningsViewModel(new NavigationalWarningsService(loader));
        var openedLibrary = false;
        panel.OpenLibraryRequested += (_, _) => openedLibrary = true;

        Assert.True(panel.IsNoSourcesState);
        Assert.False(panel.HasList);
        Assert.False(panel.HasSummary);
        panel.OpenLibraryCommand.Execute(null);
        Assert.True(openedLibrary);
    }

    [Fact]
    public void Nothing_in_force_offers_to_show_the_parts_not_in_force()
    {
        var f = new Fixture(processor: new FakeProcessor("S-124",
            (Warning("NAVAREA X", "100/26", "Wreck", "Coral Sea", Oct(1), Oct(5)), new GeoPosition(-15, 150)),
            (Warning("NAVAREA X", "101/26", "Wreck", "Coral Sea", Oct(2), Oct(6)), new GeoPosition(-15, 151))));

        Assert.True(f.Panel.IsNoneInForceState);
        Assert.Equal("No warnings in force at view time.", f.Panel.NoneInForceText);
        Assert.Equal("Show 2 not in force", f.Panel.ShowNotInForceText);
        Assert.Empty(f.Panel.Items.OfType<NavigationalWarningRowItem>());

        f.Panel.ShowNotInForceCommand.Execute(null);
        Assert.Equal(2, f.Panel.Items.OfType<NavigationalWarningRowItem>().Count());
    }

    [Fact]
    public void Loading_S124_dataset_shows_the_loading_state()
    {
        var loader = new FakeDatasetLoaderService();
        var datasets = new DatasetsViewModel(loader);
        var entry = new DatasetEntry("/tmp/navwarn.gml", "S-124") { IsLoading = true };
        datasets.Entries.Add(entry);
        var panel = new NavigationalWarningsViewModel(new NavigationalWarningsService(loader, datasets));

        Assert.True(panel.IsLoadingState);
        Assert.False(panel.IsNoSourcesState);

        entry.IsLoading = false;
        Assert.False(panel.IsLoadingState);
        Assert.True(panel.IsNoSourcesState);
    }

    [Fact]
    public void Service_rebuilds_when_a_dataset_loads()
    {
        var f = new Fixture();
        var other = new DatasetEntry("/tmp/ccg.gml", "S-124");
        f.Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor>(f.Loader.Processors)
        {
            [other] = new FakeProcessor("S-124", (Warning("P", "477/20", "Buoy", "Vancouver Harbour", Oct(1)), new GeoPosition(49.3, -122.9))),
        };

        f.Loader.RaiseLoaded(other);

        Assert.Equal(7, f.Service.Warnings.Count);
        Assert.Contains(f.Panel.Items.OfType<NavigationalWarningGroupItem>(), g => g.Name == "P");
    }

    [Fact]
    public async Task List_tool_reports_each_part_with_its_status_and_geometry()
    {
        var f = new Fixture(viewTime: true);
        var tool = new ListNavigationalWarningsTool(f.Panel, f.Service, new InlineInvoker());

        var result = await tool.InvokeAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var dto));
        Assert.Equal("view_time", dto!.Time);
        Assert.Equal(ViewAt, dto.StatusTime);
        Assert.Equal((5, 1, 1), (dto.InForce, dto.NotInForce, dto.TextOnly));
        Assert.True(dto.HasSources);
        var bulletin = dto.Warnings.Single(w => w.Number == "110/26");
        Assert.Equal(("none", true, null), (bulletin.Geometry, bulletin.TextOnly, bulletin.Bounds));
        Assert.Equal("NAVAREA X 110/26", bulletin.Label);
        Assert.Equal(["eng"], bulletin.Languages);
        var hazard = dto.Warnings.Single(w => w.Number == "112/26");
        Assert.Equal("point", hazard.Geometry);
        Assert.Equal(-15.2, hazard.Bounds!.MinLatitude, 6);
        Assert.Equal("ended", dto.Warnings.Single(w => w.Number == "108/26").Status);
    }

    [Fact]
    public async Task Select_tool_opens_a_hidden_part_and_rejects_unknown_ids()
    {
        var f = new Fixture(viewTime: true);
        var tool = new SelectNavigationalWarningTool(f.Panel, f.Service, new InlineInvoker());

        var result = await tool.InvokeAsync("amsa.gml", "part.4", TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var dto));
        Assert.True(f.Panel.IsNotInForceExpanded);
        Assert.True(dto!.Warnings.Single(w => w.FeatureId == "part.4").Selected);
        Assert.Contains("isn't in force", dto.MapNote, StringComparison.Ordinal);

        var missing = await tool.InvokeAsync("amsa.gml", "nope", TestContext.Current.CancellationToken);
        Assert.True(missing.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }
}
