using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// How fresh the Navigational warnings panel is (#932 slice 4): groups by
/// source with when it last answered, the stale notice and Retry, Refresh,
/// and which warnings are new (the seen-warnings bookkeeping): NEW until the
/// card is opened or 24 hours have passed (#997, S-98 §12.12.1).
/// </summary>
public sealed class NavigationalWarningsFreshnessTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid AmsaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid KhraId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navwarn-seen-" + Guid.NewGuid().ToString("N"));

    public NavigationalWarningsFreshnessTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeProcessor(params NavigationalWarningInfo[] parts) : IDatasetProcessor
    {
        public NavigationalWarningInfo[] Parts { get; set; } = parts;
        public string ProductSpec => "S-124";
        public SpecRef Spec { get; } = new("S-124", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];

        public IReadOnlyList<NavigationalWarningFeature> GetNavigationalWarnings() =>
            Parts.Select((p, i) => new NavigationalWarningFeature($"part.{i}", i, p)).ToList();

        public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal) => new()
        {
            Primitive = S100GeometryType.Point,
            Points = [new GeoPosition(-15.2, 158.3)],
        };
    }

    /// <summary>Sources by dataset, as the Library would resolve them.</summary>
    private sealed class FakeSources : INavigationalWarningSources
    {
        public Dictionary<DatasetEntry, NavigationalWarningSource> ByEntry { get; } = [];
        public List<Guid[]> Refreshed { get; } = [];

        public NavigationalWarningSource? SourceOf(DatasetEntry entry) => ByEntry.GetValueOrDefault(entry);

        public void Refresh(IReadOnlyCollection<Guid> sourceIds) => Refreshed.Add([.. sourceIds]);

        public event EventHandler? Changed;

        /// <summary>The source answered (or not) at <paramref name="at"/>.</summary>
        public void Set(DatasetEntry entry, NavigationalWarningSource source)
        {
            ByEntry[entry] = source;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class RecordingPick : IPickService
    {
        public void HandlePick(Mapsui.MapInfo? mapInfo, IReadOnlyList<DynamicPickHit>? dynamicHits = null) { }
        public void PresentGeographicPick(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features) { }
        public bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference) => false;
        public bool OpenFeature(IDatasetProcessor processor, string featureRef, string datasetFileName) => false;
        public bool OpenFeatureAt(IDatasetProcessor processor, int ordinal, string datasetFileName) => true;
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;
        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static NavigationalWarningInfo Warning(string series, string number, DateTimeOffset published, string text = "") => new()
    {
        Series = series,
        Number = number,
        WarningType = "Hazard",
        GeneralArea = "Coral Sea",
        Issuer = "AMSA",
        PublicationTime = published,
        Validity = [new NavigationalWarningValidity(published, null)],
        Texts = [new NavigationalWarningText("eng", $"{series} {number} {text}")],
    };

    private static DateTimeOffset Oct(int day, int hour = 0, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static NavigationalWarningSource Amsa(DateTimeOffset received, bool stale = false) => new(AmsaId, "AMSA", received, stale);

    private sealed class Fixture
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public FakeSources Sources { get; } = new();
        public FakeTimeProvider Clock { get; } = new(Now);
        public DatasetEntry AmsaEntry { get; } = new("/tmp/amsa/navarea-x.gml", "S-124");
        public DatasetEntry KhraEntry { get; } = new("/tmp/khra/navarea-xi.gml", "S-124");
        public DatasetEntry FileEntry { get; } = new("/tmp/opened.gml", "S-124");
        public FakeProcessor AmsaData { get; } = new(
            Warning("NAVAREA X", "112/26", Oct(13, 6)),
            Warning("NAVAREA X", "110/26", Oct(12, 5)));
        public NavigationalWarningSeenStore Seen { get; }
        public NavigationalWarningsService Service { get; }
        public NavigationalWarningsViewModel Panel { get; }

        public Fixture(string? seenPath = null, bool withFile = false, bool withKhra = false, Func<GeoPosition?>? ownShip = null)
        {
            // The expiry timer posts to the UI thread when there is one; tests run it inline.
            SynchronizationContext.SetSynchronizationContext(null);
            var processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [AmsaEntry] = AmsaData };
            if (withKhra)
                processors[KhraEntry] = new FakeProcessor(Warning("NAVAREA X", "201/26", Oct(13)));
            if (withFile)
                processors[FileEntry] = new FakeProcessor(Warning("NAVAREA X", "105/26", Oct(10)));
            Loader.Processors = processors;
            Sources.ByEntry[AmsaEntry] = Amsa(Oct(14, 7, 40));
            if (withKhra)
                Sources.ByEntry[KhraEntry] = new NavigationalWarningSource(KhraId, "KHRA", Oct(14, 8, 5), IsStale: false);
            Seen = new NavigationalWarningSeenStore(seenPath);
            Service = new NavigationalWarningsService(Loader, sources: Sources, seen: Seen, clock: Clock, ownShip: ownShip);
            Panel = new NavigationalWarningsViewModel(
                Service, new RecordingPick(), timeFormat: new Utc(), loader: Loader, clock: Clock);
        }

        /// <summary>AMSA refreshes at <paramref name="at"/>, now listing <paramref name="parts"/>.</summary>
        public void Refresh(DateTimeOffset at, params NavigationalWarningInfo[] parts)
        {
            AmsaData.Parts = parts;
            Clock.SetUtcNow(at);
            Sources.Set(AmsaEntry, Amsa(at));
        }

        public NavigationalWarningRowItem Row(string number) =>
            Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Number == number);
    }

    private static string Shape(NavigationalWarningsViewModel panel) => string.Join(" | ", panel.Items.Select(i => i switch
    {
        NavigationalWarningGroupItem g => $"[{g.Name} {g.Count} {g.Source}]",
        NavigationalWarningStaleItem s => $"!{s.Text}",
        NavigationalWarningRowItem r => r.IsNew ? r.Number + "*" : r.Number,
        _ => "?",
    }));

    [Fact]
    public void Group_header_names_the_source_and_when_it_last_answered()
    {
        var f = new Fixture();

        Assert.Equal("[NAVAREA X 2 AMSA · 07:40] | 112/26 | 110/26", Shape(f.Panel));
    }

    [Fact]
    public void A_series_appears_once_per_source_and_files_keep_their_issuer()
    {
        var f = new Fixture(withFile: true, withKhra: true);

        Assert.Equal(
            "[NAVAREA X 2 AMSA · 07:40] | 112/26 | 110/26 | [NAVAREA X 1 AMSA] | 105/26 | [NAVAREA X 1 KHRA · 08:05] | 201/26",
            Shape(f.Panel));
    }

    [Fact]
    public void A_receive_time_before_today_carries_its_date()
    {
        var source = new NavigationalWarningSource(AmsaId, "AMSA", Oct(12, 22, 15), IsStale: false);

        Assert.Equal("AMSA · 12 Oct 22:15", NavigationalWarningsViewModel.SourceText(source, TimeFormat.Utc, Now));
    }

    [Fact]
    public void An_unreachable_source_says_so_at_the_top_of_its_group_with_Retry()
    {
        var f = new Fixture(withFile: true);

        f.Sources.Set(f.AmsaEntry, Amsa(Oct(14, 7, 40), stale: true));

        Assert.Equal(
            "!AMSA hasn't answered since 07:40. You're seeing the warnings received then. | [NAVAREA X 2 AMSA · 07:40, stale] | 112/26 | 110/26 | [NAVAREA X 1 AMSA] | 105/26",
            Shape(f.Panel));
        var header = f.Panel.Items.OfType<NavigationalWarningGroupItem>().First();
        Assert.True(header.IsStale);

        var stale = f.Panel.Items.OfType<NavigationalWarningStaleItem>().Single();
        stale.RetryCommand.Execute(null);
        Assert.Equal([AmsaId], Assert.Single(f.Sources.Refreshed));

        // A stale notice isn't selectable.
        f.Panel.SelectedItem = stale;
        Assert.Null(f.Panel.SelectedItem);
    }

    [Fact]
    public void The_stale_notice_shows_once_per_source_across_its_series()
    {
        var f = new Fixture();
        f.AmsaData.Parts = [Warning("NAVAREA X", "112/26", Oct(13)), Warning("AUSCOAST", "314/26", Oct(13))];

        f.Sources.Set(f.AmsaEntry, Amsa(Oct(14, 7, 40), stale: true));

        Assert.Single(f.Panel.Items.OfType<NavigationalWarningStaleItem>());
        Assert.IsType<NavigationalWarningStaleItem>(f.Panel.Items[0]);
    }

    [Fact]
    public void Refresh_reads_the_sources_of_the_listed_warnings_and_is_off_without_one()
    {
        var f = new Fixture(withKhra: true);

        Assert.True(f.Panel.CanRefresh);
        Assert.True(f.Panel.RefreshCommand.CanExecute(null));
        f.Panel.RefreshCommand.Execute(null);
        Assert.Equal([AmsaId, KhraId], Assert.Single(f.Sources.Refreshed).Order());

        var files = new FakeDatasetLoaderService();
        var file = new DatasetEntry("/tmp/opened.gml", "S-124");
        files.Processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [file] = new FakeProcessor(Warning("NAVAREA X", "105/26", Oct(10))) };
        var panel = new NavigationalWarningsViewModel(new NavigationalWarningsService(files, sources: new FakeSources()), clock: new Clock(Now));
        Assert.False(panel.CanRefresh);
        Assert.False(panel.RefreshCommand.CanExecute(null));
        Assert.Equal(0, panel.RefreshSources());
    }

    [Fact]
    public void Nothing_is_new_from_a_source_seen_for_the_first_time()
    {
        var f = new Fixture();

        Assert.Equal(0, f.Panel.NewCount);
        Assert.False(f.Panel.HasNew);
        Assert.DoesNotContain("*", Shape(f.Panel), StringComparison.Ordinal);
    }

    [Fact]
    public void A_warning_the_latest_refresh_brought_is_new_until_its_card_is_opened()
    {
        var f = new Fixture();

        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)), Warning("NAVAREA X", "110/26", Oct(12, 5)));

        Assert.Equal("[NAVAREA X 3 AMSA · 10:00] | 113/26* | 112/26 | 110/26", Shape(f.Panel));
        Assert.Equal(1, f.Panel.NewCount);
        Assert.Equal("1 new", f.Panel.NewText);
        Assert.Equal(1, f.Panel.BadgeCount);
        Assert.EndsWith(", NEW", f.Row("113/26").AccessibleName, StringComparison.Ordinal);

        Assert.True(f.Panel.Open(f.Row("113/26")));

        Assert.False(f.Row("113/26").IsNew);
        Assert.Equal(0, f.Panel.BadgeCount);
    }

    [Fact]
    public void A_later_refresh_that_brings_it_again_unchanged_keeps_it_new()
    {
        var f = new Fixture();
        var w113 = Warning("NAVAREA X", "113/26", Oct(14, 9));
        f.Refresh(Oct(14, 10), w113, Warning("NAVAREA X", "112/26", Oct(13, 6)));
        Assert.True(f.Row("113/26").IsNew);

        f.Refresh(Oct(14, 12), w113, Warning("NAVAREA X", "112/26", Oct(13, 6)));

        Assert.True(f.Row("113/26").IsNew);
        Assert.Equal(1, f.Panel.NewCount);
    }

    [Fact]
    public void New_expires_24_hours_after_it_was_received_without_a_refresh()
    {
        var f = new Fixture();
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        var badges = new List<int>();
        f.Panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationalWarningsViewModel.BadgeCount))
                badges.Add(f.Panel.BadgeCount);
        };

        f.Clock.Advance(TimeSpan.FromHours(23.9));
        Assert.True(f.Row("113/26").IsNew);
        Assert.Equal(1, f.Panel.BadgeCount);

        // The timer, not a refresh, notices: the row, the summary and the badge follow.
        f.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.False(f.Row("113/26").IsNew);
        Assert.Equal(0, f.Panel.NewCount);
        Assert.False(f.Panel.HasNew);
        Assert.Equal(0, f.Panel.BadgeCount);
        Assert.Contains(0, badges);
        var newness = f.Service.Newness(f.Row("113/26").Entry);
        Assert.Equal(NavigationalWarningNewReason.Expired, newness.Reason);
        Assert.Equal(Oct(14, 10), newness.ReceivedAt);
        Assert.Equal(Oct(15, 10), newness.NewUntil);
    }

    [Fact]
    public void Opening_the_card_clears_new_before_it_expires_and_says_so()
    {
        var f = new Fixture();
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)));
        f.Clock.Advance(TimeSpan.FromHours(2));

        f.Panel.Open(f.Row("113/26"));

        var newness = f.Service.Newness(f.Row("113/26").Entry);
        Assert.Equal(NavigationalWarningNewReason.Opened, newness.Reason);
        Assert.Equal(Oct(14, 12), newness.OpenedAt);
        // Expiry later changes nothing: it was already read.
        f.Clock.Advance(TimeSpan.FromHours(30));
        Assert.Equal(NavigationalWarningNewReason.Opened, f.Service.Newness(f.Row("113/26").Entry).Reason);
    }

    [Fact]
    public void A_warning_reissued_with_new_content_is_new_again()
    {
        var f = new Fixture();

        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "112/26", Oct(14, 9), text: "AMENDED"), Warning("NAVAREA X", "110/26", Oct(12, 5)));

        Assert.True(f.Row("112/26").IsNew);
        Assert.False(f.Row("110/26").IsNew);
    }

    [Fact]
    public void An_old_warning_whose_dataset_loads_later_is_not_new()
    {
        var f = new Fixture();
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "112/26", Oct(13, 6)), Warning("NAVAREA X", "110/26", Oct(12, 5)));

        // Published a week before the previous refresh: the source listed it then; it just wasn't loaded.
        f.AmsaData.Parts = [.. f.AmsaData.Parts, Warning("NAVAREA X", "090/26", Oct(1))];
        f.Service.Refresh();

        Assert.False(f.Row("090/26").IsNew);
    }

    [Fact]
    public void A_stale_source_brings_nothing_new()
    {
        var f = new Fixture();

        f.AmsaData.Parts = [Warning("NAVAREA X", "113/26", Oct(14, 9)), .. f.AmsaData.Parts];
        f.Sources.Set(f.AmsaEntry, Amsa(Oct(14, 7, 40), stale: true));

        // Still the baseline refresh: a stale read doesn't start a new one.
        Assert.False(f.Row("113/26").IsNew);
    }

    [Fact]
    public void Files_opened_directly_are_never_new()
    {
        var f = new Fixture(withFile: true);
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)));

        Assert.False(f.Row("105/26").IsNew);
        Assert.Null(f.Row("105/26").Entry.Source);
    }

    [Fact]
    public void Seen_warnings_persist_across_restarts()
    {
        var path = Path.Combine(_dir, "navigational-warnings.json");
        var first = new Fixture(path);
        first.Refresh(Oct(14, 10), Warning("NAVAREA X", "114/26", Oct(14, 9)), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        first.Panel.Open(first.Row("114/26"));
        first.Seen.Flush();

        // A new session, before the source refreshes again.
        var second = new Fixture(path);
        second.AmsaData.Parts = first.AmsaData.Parts;
        second.Sources.Set(second.AmsaEntry, Amsa(Oct(14, 10)));

        Assert.True(File.Exists(path));
        Assert.False(second.Row("114/26").IsNew);
        Assert.True(second.Row("113/26").IsNew);
        Assert.Equal(Oct(14, 10), second.Service.Newness(second.Row("113/26").Entry).ReceivedAt);

        // A session started after the 24 hours ran out while the viewer was closed.
        var third = new Fixture(path);
        third.Clock.SetUtcNow(Oct(15, 11));
        third.AmsaData.Parts = first.AmsaData.Parts;
        third.Sources.Set(third.AmsaEntry, Amsa(Oct(14, 10)));
        Assert.False(third.Row("113/26").IsNew);
        Assert.Equal(NavigationalWarningNewReason.Expired, third.Service.Newness(third.Row("113/26").Entry).Reason);
    }

    [Fact]
    public void A_format_1_file_is_discarded_and_each_source_sets_a_new_baseline()
    {
        var path = Path.Combine(_dir, "navigational-warnings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """
            {
              "version": 1,
              "sources": {
                "11111111-1111-1111-1111-111111111111": {
                  "refreshedAt": "2026-10-14T07:40:00+00:00",
                  "previousRefreshedAt": "2026-10-13T07:40:00+00:00",
                  "baseline": false,
                  "known": { "NAVAREA X|112/26": { "stamp": "x", "seen": "2026-10-14T00:00:00+00:00" } },
                  "new": [ "NAVAREA X|112/26" ]
                }
              }
            }
            """);

        var f = new Fixture(path);

        Assert.Equal(0, f.Panel.NewCount);
        Assert.Equal(NavigationalWarningNewReason.Baseline, f.Service.Newness(f.Row("112/26").Entry).Reason);
        f.Seen.Flush();
        Assert.Contains("\"version\": 2", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Warnings_are_keyed_by_series_and_number_else_by_dataset_and_feature()
    {
        var entry = new DatasetEntry("/tmp/a.gml", "S-124");
        var numbered = new NavigationalWarningEntry(entry, "part.0", 0, Warning("NAVAREA X", "106/26", Oct(1)), S100GeometryType.None, null);
        var bare = numbered with { Warning = new NavigationalWarningInfo { Title = "No preamble" } };

        Assert.Equal("NAVAREA X|106/26", numbered.Key);
        Assert.Equal($"{entry.DisplayName}#part.0", bare.Key);
    }

    [Fact]
    public void Store_rule_baseline_then_new_until_opened_or_24_hours()
    {
        var store = new NavigationalWarningSeenStore(null);
        NavigationalWarningSeenStore.Observed W(string key, string stamp = "a", DateTimeOffset? published = null) => new(key, stamp, published ?? Oct(14));

        Assert.False(store.Observe("s", Oct(14, 7), [W("1"), W("2")], Oct(14, 7)));
        Assert.False(store.IsNew("s", "1", Oct(14, 7)));
        Assert.Equal(NavigationalWarningNewReason.Baseline, store.Newness("s", "1", Oct(14, 7)).Reason);
        // More of the baseline refresh (another dataset loading) is still the baseline.
        Assert.False(store.Observe("s", Oct(14, 7), [W("3")], Oct(14, 8)));
        Assert.False(store.IsNew("s", "3", Oct(14, 8)));

        Assert.True(store.Observe("s", Oct(14, 9), [W("1"), W("2"), W("3"), W("4")], Oct(14, 9)));
        Assert.True(store.IsNew("s", "4", Oct(14, 9)));
        Assert.Equal(new NavigationalWarningNewness(NavigationalWarningNewReason.New, Oct(14, 9), Oct(15, 9)), store.Newness("s", "4", Oct(14, 9)));
        // The same refresh observed again changes nothing.
        Assert.False(store.Observe("s", Oct(14, 9), [W("4")], Oct(14, 10)));

        Assert.True(store.MarkSeen("s", "4", Oct(14, 11)));
        Assert.False(store.MarkSeen("s", "4", Oct(14, 12)));
        Assert.False(store.IsNew("s", "4", Oct(14, 12)));

        // A later refresh doesn't clear NEW; 24 hours do.
        Assert.True(store.Observe("s", Oct(14, 11), [W("5")], Oct(14, 11)));
        Assert.False(store.Observe("s", Oct(14, 12), [W("5")], Oct(14, 12)));
        Assert.True(store.IsNew("s", "5", Oct(15, 10, 59)));
        Assert.False(store.IsNew("s", "5", Oct(15, 11)));
        Assert.Equal(NavigationalWarningNewReason.Expired, store.Newness("s", "5", Oct(15, 11)).Reason);
        Assert.False(store.MarkSeen("s", "5", Oct(15, 12)));

        // Published well before the previous refresh: its dataset just loaded late.
        Assert.False(store.Observe("s", Oct(14, 12), [W("6", published: Oct(1))], Oct(14, 13)));
        Assert.Equal(NavigationalWarningNewReason.PublishedEarlier, store.Newness("s", "6", Oct(14, 13)).Reason);
        Assert.Equal(NavigationalWarningNewReason.NotObserved, store.Newness("s", "7", Oct(14, 13)).Reason);
    }

    [Fact]
    public void Mark_new_since_resets_everything_received_since_for_24_hours()
    {
        var store = new NavigationalWarningSeenStore(null);
        NavigationalWarningSeenStore.Observed W(string key) => new(key, "a", Oct(14));
        store.Observe("s", Oct(14, 7), [W("base")], Oct(14, 7));
        store.Observe("s", Oct(14, 9), [W("base"), W("early")], Oct(14, 9));
        store.Observe("s", Oct(14, 13), [W("base"), W("early"), W("late")], Oct(14, 13));
        store.MarkSeen("s", "late", Oct(14, 14));

        // Handover at 16:00 for the watch that started at 12:00.
        Assert.Equal(1, store.MarkNewSince(Oct(14, 12), Oct(14, 16)));

        // Received before the watch began: untouched, still new from its own receipt.
        Assert.Null(store.Newness("s", "early", Oct(14, 16)).MarkedAt);
        Assert.Equal(Oct(15, 9), store.Newness("s", "early", Oct(14, 16)).NewUntil);
        var late = store.Newness("s", "late", Oct(14, 16));
        Assert.Equal(NavigationalWarningNewReason.New, late.Reason);
        Assert.Equal(Oct(14, 16), late.MarkedAt);
        Assert.Equal(Oct(15, 16), late.NewUntil);
        Assert.False(store.IsNew("s", "late", Oct(15, 16)));

        // Since before the baseline: the baseline's warnings count too.
        Assert.Equal(3, store.MarkNewSince(Oct(14), Oct(14, 17)));
        Assert.True(store.IsNew("s", "base", Oct(14, 17)));
    }

    [Fact]
    public void Mark_new_since_shows_in_the_panel()
    {
        var f = new Fixture();
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        f.Panel.Open(f.Row("113/26"));
        Assert.Equal(0, f.Panel.BadgeCount);
        var options = new NavigationalWarningNewOptionsViewModel(f.Service, clock: f.Clock);
        // Handover at 14:00: 113/26 came at 10:00, 112/26 with the 09:00 baseline.
        f.Clock.SetUtcNow(Oct(14, 14));

        Assert.Equal(1, options.MarkNewSince(TimeSpan.FromHours(4)));

        Assert.True(f.Row("113/26").IsNew);
        Assert.False(f.Row("112/26").IsNew);
        Assert.Equal(1, f.Panel.BadgeCount);
        Assert.Equal("1 new after handover.", options.MarkedText);
    }

    [Fact]
    public void The_received_within_filter_hides_new_on_older_warnings()
    {
        var f = new Fixture();
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        f.Refresh(Oct(14, 16), Warning("NAVAREA X", "114/26", Oct(14, 15)), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        var settings = new ViewerSettings();
        var options = new NavigationalWarningNewOptionsViewModel(f.Service, settings, f.Clock);
        Assert.Equal(2, f.Panel.NewCount);
        Assert.False(options.IsFiltering);

        options.WithinHours = 4;
        options.IsWithinHoursEnabled = true;

        Assert.True(options.IsFiltering);
        Assert.Equal(1, f.Panel.NewCount);
        Assert.True(f.Row("114/26").IsNew);
        Assert.Equal(NavigationalWarningNewReason.FilteredAge, f.Service.Newness(f.Row("113/26").Entry).Reason);
        Assert.Equal(4, settings.NavigationalWarningsNewWithinHours);

        // 114/26 ages out of the filter by the clock alone.
        f.Clock.Advance(TimeSpan.FromHours(4.1));
        Assert.Equal(0, f.Panel.NewCount);

        options.IsWithinHoursEnabled = false;
        Assert.Equal(2, f.Panel.NewCount);
        Assert.Null(settings.NavigationalWarningsNewWithinHours);
    }

    [Fact]
    public void The_distance_filter_hides_new_on_warnings_far_from_own_ship()
    {
        // The fixture's warnings are at 15.2°S 158.3°E.
        GeoPosition? ship = new GeoPosition(-15.2, 158.8);
        var f = new Fixture(ownShip: () => ship);
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        var settings = new ViewerSettings { NavigationalWarningsNewWithinNm = 20 };
        var options = new NavigationalWarningNewOptionsViewModel(f.Service, settings, f.Clock);

        // About 29 NM away: filtered.
        Assert.True(options.IsWithinNmEnabled);
        Assert.Equal(0, f.Panel.NewCount);
        Assert.Equal(NavigationalWarningNewReason.FilteredDistance, f.Service.Newness(f.Row("113/26").Entry).Reason);
        Assert.InRange(f.Service.DistanceNm(f.Row("113/26").Entry)!.Value, 28.5, 29.5);

        // The ship closes in; the timer's recheck follows it.
        ship = new GeoPosition(-15.2, 158.5);
        f.Clock.Advance(NavigationalWarningsService.RecheckEvery);
        Assert.Equal(1, f.Panel.NewCount);

        // Nothing to measure from: nothing is filtered.
        ship = null;
        options.WithinNm = 1;
        Assert.Equal(1, f.Panel.NewCount);
    }

    [Fact]
    public void Proximity_measures_points_lines_and_areas_against_a_position_and_a_route()
    {
        var area = new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Surface,
            ExteriorRing = [new(50, -1), new(50, 0), new(51, 0), new(51, -1), new(50, -1)],
        };
        Assert.Equal(0, NavigationalWarningProximity.DistanceNm(area, new GeoPosition(50.5, -0.5)));
        Assert.InRange(NavigationalWarningProximity.DistanceNm(area, new GeoPosition(52, -0.5))!.Value, 59.9, 60.1);
        // A route crossing the area's edge, with no waypoint inside.
        Assert.Equal(0, NavigationalWarningProximity.DistanceNm(area, [new GeoPosition(50.5, -2), new GeoPosition(50.5, 1)]));
        Assert.InRange(NavigationalWarningProximity.DistanceNm(area, [new GeoPosition(49, -2), new GeoPosition(49, 1)])!.Value, 59.9, 60.1);

        var point = new S100FeatureGeometry { Primitive = S100GeometryType.Point, Points = [new(0, 179.9)] };
        // Across the antimeridian: 0.2° of longitude at the equator.
        Assert.InRange(NavigationalWarningProximity.DistanceNm(point, new GeoPosition(0, -179.9))!.Value, 11.9, 12.1);
    }

    [Fact]
    public void List_tool_reports_new_sources_and_stale()
    {
        var f = new Fixture(withFile: true);
        f.Refresh(Oct(14, 10), Warning("NAVAREA X", "113/26", Oct(14, 9)), Warning("NAVAREA X", "112/26", Oct(13, 6)));
        f.Sources.Set(f.AmsaEntry, Amsa(Oct(14, 10), stale: true));

        var dto = NavigationalWarningsToolBase.ToDto(f.Panel, f.Service);

        Assert.Equal(1, dto.New);
        Assert.True(dto.CanRefresh);
        var source = Assert.Single(dto.Sources);
        Assert.Equal("AMSA", source.Name);
        Assert.True(source.Stale);
        Assert.Equal("AMSA · 10:00, stale", source.Header);
        var w113 = dto.Warnings.Single(w => w.Number == "113/26");
        Assert.True(w113.New);
        Assert.Equal("new", w113.NewReason);
        Assert.Equal(Oct(14, 10), w113.ReceivedAt);
        Assert.Equal(Oct(15, 10), w113.NewUntil);
        Assert.Equal("baseline", dto.Warnings.Single(w => w.Number == "112/26").NewReason);
        Assert.Equal("no_source", dto.Warnings.Single(w => w.Number == "105/26").NewReason);
        Assert.Null(dto.NewFilter.ReceivedWithinHours);
        Assert.Null(dto.NewFilter.WithinNauticalMiles);
        Assert.Equal(AmsaId, dto.Warnings.Single(w => w.Number == "113/26").SourceId);
        Assert.Null(dto.Warnings.Single(w => w.Number == "105/26").SourceId);
    }
}
