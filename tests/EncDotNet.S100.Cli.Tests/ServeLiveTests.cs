using System.Net;
using System.Net.Http.Json;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Web;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> web page's server state and live changes
/// (issue #1583, handoff D28–D31): the datasets found but not served, the
/// change events with dataset ids that adding, removing and reloading
/// datasets push, the clean-shutdown signal, and what the page makes of them.
/// </summary>
public sealed class ServeLiveTests : IDisposable
{
    // Off Nantucket.
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // A small S-101 cell; cut short, it's recognised but doesn't open (the S-57 reader is more forgiving).
    private static string S101 => Path.Combine(AppContext.BaseDirectory, "TestData", "S101", "101AA00DS0003.000");

    // Aids to navigation in Chesapeake Bay.
    private static string S125 => Path.Combine(AppContext.BaseDirectory, "TestData", "S125", "aton_point.gml");

    // A Part 15 set: six protected datasets and one S-104 that isn't.
    private static string ProtectedMixed => Path.Combine(AppContext.BaseDirectory, "TestData", "ProtectedMixed");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "serve-live-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private int _touches;

    public ServeLiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Place(string file, string? name = null)
    {
        var copy = Path.Combine(_root, name ?? Path.GetFileName(file));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(file, copy, overwrite: true);
        return copy;
    }

    /// <summary>A copy of the S-101 cell cut off part-way, so it's recognised but doesn't open.</summary>
    private string Broken(string name)
    {
        var path = Path.Combine(_root, name);
        Cut(path);
        return path;
    }

    private static void Cut(string path) => File.WriteAllBytes(path, File.ReadAllBytes(S101).AsSpan(0, 3000).ToArray());

    private void Touch(string file) =>
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(++_touches));

    private static TilesServeCommand.Settings SettingsFor(string? input = null, string? from = null, bool manage = false) => new()
    {
        Input = input,
        ExchangeSet = from,
        MinZoom = 14,
        MaxZoom = 14,
        Parallel = 2,
        Metatile = 1,
        ManageDatasets = manage,
    };

    private void Log(string line)
    {
        lock (_log)
            _log.Add(line);
    }

    /// <summary>A refreshing source that checks only when asked, as <c>tiles serve --refresh</c> sets one up.</summary>
    private RefreshingTileSource Refreshing(TilesServeCommand.Settings settings, Func<RenderedTileSource?>? open = null, TimeSpan? interval = null)
    {
        var fingerprint = TileRenderSession.Fingerprint(settings);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial));
        return new RefreshingTileSource(
            initial!,
            fingerprint,
            () => TileRenderSession.Fingerprint(settings),
            open ?? (() => TilesServeCommand.OpenRendered(settings, out var reopened) == 0 ? reopened : null),
            interval ?? Timeout.InfiniteTimeSpan,
            Log);
    }

    /// <summary>A managed source and its datasets, as <c>tiles serve --manage-datasets --refresh 0</c> sets them up.</summary>
    private (RefreshingTileSource Source, ServedDatasets Datasets) Managed(TilesServeCommand.Settings settings)
    {
        var managed = new ManagedDatasetTiles(64L * 1024 * 1024);
        var styles = new MarinerStyleRegistry();
        ServedDatasets? datasets = null;
        DatasetChanges Changes() => datasets?.Changes ?? DatasetChanges.None;
        var fingerprint = TileRenderSession.Fingerprint(settings);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial, styles, managed: managed));
        var source = new RefreshingTileSource(
            initial!,
            fingerprint,
            () => TileRenderSession.Fingerprint(settings, Changes()),
            () => TilesServeCommand.OpenRendered(settings, out var reopened, styles, Changes(), managed) == 0 ? reopened : null,
            Timeout.InfiniteTimeSpan,
            Log);
        datasets = new ServedDatasets(
            source,
            changes => TilesServeCommand.OpenRendered(settings, out var changed, styles, changes, managed) == 0 ? changed : null,
            changes => TileRenderSession.Fingerprint(settings, changes));
        return (source, datasets);
    }

    /// <summary>The updates a page hears of, in order.</summary>
    private static (List<DatasetsUpdate> Updates, IDisposable Subscription) Listen(IServePageData data)
    {
        var updates = new List<DatasetsUpdate>();
        var subscription = data.OnDatasetsUpdated(u =>
        {
            lock (updates)
                updates.Add(u);
        });
        return (updates, subscription);
    }

    // Not served (D31).

    [Fact]
    public void A_folder_keeps_the_datasets_it_skips_with_why()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S101), "Fixtures not present.");
        Place(S57);
        var broken = Broken("101ZZBROKEN.000");
        File.WriteAllText(Path.Combine(_root, "notes.h5"), "not a dataset");
        File.WriteAllBytes(Path.Combine(_root, "more.zip"), [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);

        Assert.Equal(0, TilesServeCommand.OpenRendered(SettingsFor(_root), out var source));
        using var _ = source!;
        var data = new TileSourcePageData(source!, hasToken: false);

        Assert.Equal(["US5MA1BO"], data.Datasets.Select(d => d.Id));
        var notServed = data.NotServed;
        Assert.Equal(3, notServed.Count);

        var failed = Assert.Single(notServed, s => s.Name == "101ZZBROKEN.000");
        Assert.Equal(SkippedDatasetKind.Failed, failed.Kind);
        Assert.Equal(Path.GetFullPath(broken), failed.Path);
        Assert.StartsWith("Failed to load: ", failed.Reason, StringComparison.Ordinal);
        Assert.EndsWith(".", failed.Reason, StringComparison.Ordinal);
        Assert.StartsWith("Skipped S-101 dataset that could not be opened: ", failed.Warning, StringComparison.Ordinal);

        var unknown = Assert.Single(notServed, s => s.Name == "notes.h5");
        Assert.Equal(SkippedDatasetKind.Unsupported, unknown.Kind);
        Assert.Equal("Skipped: not a dataset of a product s100 reads.", unknown.Reason);

        var archive = Assert.Single(notServed, s => s.Name == "more.zip");
        Assert.Equal(SkippedDatasetKind.Unsupported, archive.Kind);
        Assert.Contains("archive", archive.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exchange_set_keeps_its_protected_datasets_as_not_served()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(ProtectedMixed, "CATALOG.XML")), "Protected exchange set fixture not present.");

        Assert.Equal(0, TilesServeCommand.OpenRendered(SettingsFor(from: ProtectedMixed), out var source));
        using var _ = source!;
        var data = new TileSourcePageData(source!, hasToken: false);

        Assert.Equal(["S-104"], data.Datasets.Select(d => d.Product));
        Assert.Equal(6, data.NotServed.Count);
        Assert.All(data.NotServed, s =>
        {
            Assert.Equal(SkippedDatasetKind.Protected, s.Kind);
            Assert.Equal("Skipped: protected (S-100 Part 15). Serve it decrypted.", s.Reason);
            Assert.StartsWith("Skipped data-protected (encrypted) dataset: ", s.Warning, StringComparison.Ordinal);
        });
        Assert.Contains(data.NotServed, s => s.Name == "101ZZ00MIXSW.000");
    }

    [Fact]
    public void A_single_file_has_nothing_not_served_and_no_live_changes()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        Assert.Equal(0, TilesServeCommand.OpenRendered(SettingsFor(S57), out var source));
        using var _ = source!;
        var data = new TileSourcePageData(source!, hasToken: false);

        Assert.Empty(data.NotServed);
        Assert.Null(data.RefreshInterval);
        Assert.False(data.IsUpdating);
        Assert.Same(NoSubscription.Instance, data.OnDatasetsUpdated(_ => { }));
    }

    // Changes (D29).

    [Fact]
    public async Task Adding_and_removing_through_the_datasets_url_pushes_changes_with_ids()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Place(S57);
        var atons = Place(S125);
        var (source, datasets) = Managed(SettingsFor(cell, manage: true));
        await using var _ = source;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct, datasets: datasets);
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var data = new TileSourcePageData(source, hasToken: false);
        var (updates, subscription) = Listen(data);
        using var __ = subscription;
        var before = data.Revision;

        using (var added = await http.PostAsJsonAsync(ServedDatasets.Path, new { path = atons }, ct))
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        // Updating while the set opens, then the dataset added, by its /datasets id.
        Assert.Equal(2, updates.Count);
        Assert.True(updates[0].InProgress);
        Assert.Empty(updates[0].Changes);
        Assert.False(updates[1].InProgress);
        var change = Assert.Single(updates[1].Changes);
        Assert.Equal(new DatasetChange(DatasetChangeKind.Added, "aton_point", "aton_point.gml"), change);
        Assert.NotEqual(before, updates[1].Revision);
        Assert.Equal(data.Revision, updates[1].Revision);
        Assert.Contains(data.Datasets, d => d.Id == "aton_point");
        Assert.False(data.IsUpdating);

        using var request = new HttpRequestMessage(HttpMethod.Delete, ServedDatasets.Path) { Content = JsonContent.Create(new { path = atons }) };
        using (var removed = await http.SendAsync(request, ct))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        Assert.Equal(4, updates.Count);
        Assert.Equal(new DatasetChange(DatasetChangeKind.Removed, "aton_point", "aton_point.gml"), Assert.Single(updates[3].Changes));
        Assert.DoesNotContain(data.Datasets, d => d.Id == "aton_point");
    }

    [Fact]
    public async Task Removing_the_last_dataset_and_adding_it_back_pushes_removed_then_added()
    {
        Assert.SkipUnless(File.Exists(S57), "Fixtures not present.");
        var cell = Place(S57);
        var (source, datasets) = Managed(SettingsFor(cell, manage: true));
        await using var _ = source;
        var data = new TileSourcePageData(source, hasToken: false);
        var (updates, subscription) = Listen(data);
        using var __ = subscription;
        var id = Path.GetFileNameWithoutExtension(cell);
        var name = Path.GetFileName(cell);

        // Nothing served now (issue #1601): the cell is removed, and the revision moves on.
        var before = data.Revision;
        Assert.True((await datasets.RemoveAsync([cell])).Succeeded);
        Assert.Equal(2, updates.Count);
        Assert.True(updates[0].InProgress);
        Assert.False(updates[1].InProgress);
        Assert.Equal(new DatasetChange(DatasetChangeKind.Removed, id, name), Assert.Single(updates[1].Changes));
        Assert.NotEqual(before, updates[1].Revision);
        Assert.Equal(data.Revision, updates[1].Revision);
        Assert.Empty(data.Datasets);

        // Served again: added, from nothing.
        Assert.True((await datasets.AddAsync([cell])).Succeeded);
        Assert.Equal(4, updates.Count);
        Assert.Equal(new DatasetChange(DatasetChangeKind.Added, id, name), Assert.Single(updates[3].Changes));
        Assert.Equal(data.Revision, updates[3].Revision);
        Assert.False(data.IsUpdating);
    }

    [Fact]
    public async Task A_change_that_fails_ends_the_update_without_changes()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S101), "Fixtures not present.");
        var cell = Place(S57);
        var (source, datasets) = Managed(SettingsFor(cell, manage: true));
        await using var _ = source;
        var data = new TileSourcePageData(source, hasToken: false);
        var (updates, subscription) = Listen(data);
        using var __ = subscription;
        var text = Path.Combine(_root, "readme.txt");
        File.WriteAllText(text, "not a dataset");

        // Not recognised: refused before anything opens.
        Assert.Equal(400, (await datasets.AddAsync([text])).Status);
        Assert.Empty(updates);

        // Recognised but broken: opening fails, and the update ends with nothing changed.
        Assert.False((await datasets.AddAsync([Broken("101ZZBROKEN.000")])).Succeeded);
        Assert.Equal(2, updates.Count);
        Assert.True(updates[0].InProgress);
        Assert.False(updates[1].InProgress);
        Assert.Empty(updates[1].Changes);
        Assert.False(data.IsUpdating);
    }

    [Fact]
    public async Task A_file_reload_pushes_reloading_then_reloaded()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = Place(S57);
        await using var source = Refreshing(SettingsFor(cell), interval: TimeSpan.FromSeconds(5));
        var data = new TileSourcePageData(source, hasToken: false);
        Assert.Equal(TimeSpan.FromSeconds(5), data.RefreshInterval);
        var (updates, subscription) = Listen(data);
        using var _ = subscription;
        bool updatingWhileReloading = false;
        using var watch = data.OnDatasetsUpdated(u => updatingWhileReloading |= u.InProgress && data.IsUpdating);
        var before = data.Revision;

        Touch(cell);
        Assert.False(await source.CheckAsync());
        Assert.Empty(updates);
        Assert.True(await source.CheckAsync());

        Assert.Equal(2, updates.Count);
        Assert.True(updates[0].InProgress);
        Assert.Equal(new DatasetChange(DatasetChangeKind.Reloading, "US5MA1BO", "US5MA1BO.000"), Assert.Single(updates[0].Changes));
        Assert.Equal(before, updates[0].Revision);
        Assert.False(updates[1].InProgress);
        Assert.Equal(new DatasetChange(DatasetChangeKind.Reloaded, "US5MA1BO", "US5MA1BO.000"), Assert.Single(updates[1].Changes));
        Assert.NotEqual(before, updates[1].Revision);
        Assert.True(updatingWhileReloading);
        Assert.False(data.IsUpdating);
    }

    [Fact]
    public async Task A_reload_that_fails_pushes_the_failure_with_the_reason()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = Place(S57);
        await using var source = Refreshing(SettingsFor(cell), () => throw new InvalidDataException("Record 1 412 has a broken pointer (VRPT)"));
        var data = new TileSourcePageData(source, hasToken: false);
        var (updates, subscription) = Listen(data);
        using var _ = subscription;
        var before = data.Revision;

        Touch(cell);
        Assert.False(await source.CheckAsync());
        Assert.False(await source.CheckAsync());

        Assert.Equal(2, updates.Count);
        Assert.Equal(DatasetChangeKind.Reloading, Assert.Single(updates[0].Changes).Kind);
        var failed = Assert.Single(updates[1].Changes);
        Assert.Equal(DatasetChangeKind.Failed, failed.Kind);
        Assert.Equal("US5MA1BO", failed.Id);
        Assert.Equal("Record 1 412 has a broken pointer (VRPT). The previous version still serves.", failed.Reason);
        Assert.Equal(before, updates[1].Revision);
        Assert.Equal(before, data.Revision);
    }

    [Fact]
    public async Task A_folder_dataset_that_breaks_on_reload_is_a_failure_and_not_served()
    {
        Assert.SkipUnless(File.Exists(S101) && File.Exists(S125), "Fixtures not present.");
        var cell = Place(S101);
        Place(S125);
        await using var source = Refreshing(SettingsFor(_root));
        var data = new TileSourcePageData(source, hasToken: false);
        Assert.Empty(data.NotServed);
        var (updates, subscription) = Listen(data);
        using var _ = subscription;

        Cut(cell);
        Touch(cell);
        Assert.False(await source.CheckAsync());
        Assert.True(await source.CheckAsync());

        Assert.Equal(new DatasetChange(DatasetChangeKind.Reloading, "101AA00DS0003", "101AA00DS0003.000"), Assert.Single(updates[0].Changes));
        var failed = Assert.Single(updates[1].Changes);
        Assert.Equal(DatasetChangeKind.Failed, failed.Kind);
        Assert.Equal("101AA00DS0003", failed.Id);
        Assert.StartsWith("Failed to load: ", failed.Reason, StringComparison.Ordinal);
        Assert.EndsWith("It isn't served now.", failed.Reason, StringComparison.Ordinal);
        Assert.Equal(["aton_point"], data.Datasets.Select(d => d.Id));
        Assert.Equal("101AA00DS0003.000", Assert.Single(data.NotServed).Name);
    }

    [Fact]
    public async Task A_listener_that_throws_does_not_stop_the_others_or_the_change()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = Place(S57);
        await using var source = Refreshing(SettingsFor(cell));
        var data = new TileSourcePageData(source, hasToken: false);
        using var throwing = data.OnDatasetsUpdated(_ => throw new InvalidOperationException("boom"));
        var (updates, subscription) = Listen(data);

        Touch(cell);
        await source.CheckAsync();
        Assert.True(await source.CheckAsync());
        Assert.Equal(2, updates.Count);
        Assert.Contains(_log, l => l.Contains("boom", StringComparison.Ordinal));

        // Once disposed, a subscription hears no more.
        subscription.Dispose();
        Touch(cell);
        await source.CheckAsync();
        Assert.True(await source.CheckAsync());
        Assert.Equal(2, updates.Count);
    }

    // Server stopped (D28).

    [Fact]
    public async Task A_clean_stop_tells_the_pages_and_waits_for_them()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        Assert.Equal(0, TilesServeCommand.OpenRendered(SettingsFor(S57), out var source));
        using var _ = source!;
        using var stopping = new CancellationTokenSource();
        var data = new TileSourcePageData(source!, hasToken: false, stopping.Token);
        int told = 0;
        using var first = data.OnStopping(async () =>
        {
            await Task.Delay(100);
            Interlocked.Increment(ref told);
        });
        using var gone = data.OnStopping(() => throw new InvalidOperationException("The tab is gone."));
        var later = data.OnStopping(() =>
        {
            Interlocked.Increment(ref told);
            return Task.CompletedTask;
        });
        later.Dispose();

        await stopping.CancelAsync();

        // Cancelling returned only once the page had been told; the one that left wasn't.
        Assert.Equal(1, Volatile.Read(ref told));
    }

    // What the page makes of it (D28, D29).

    [Fact]
    public void The_pill_and_footer_give_the_revision_and_the_refresh_interval()
    {
        Assert.Equal(ServerState.Live, ServeLive.State(updating: false));
        Assert.Equal(ServerState.Updating, ServeLive.State(updating: true));
        Assert.Equal("revision 0878928 · checks files every 10 s", ServeLive.StateDetail("0878928abcdef", TimeSpan.FromSeconds(10)));
        Assert.Equal("revision abc", ServeLive.StateDetail("abc", null));
        Assert.Null(ServeLive.StateDetail(null, null));
        Assert.Equal("Checks files every 10 s · revision 0878928", ServeLive.Footer(true, "0878928abcdef", TimeSpan.FromSeconds(10)));
        Assert.Equal("Checks files every 5 min · revision 0878928", ServeLive.Footer(true, "0878928", TimeSpan.FromMinutes(5)));
        Assert.Equal("Doesn't check files for changes · revision 0878928", ServeLive.Footer(true, "0878928", null));
        Assert.Null(ServeLive.Footer(false, null, null));
        Assert.Equal("every 2.5 s", ServeLive.Every(TimeSpan.FromSeconds(2.5)));
    }

    [Fact]
    public void A_change_brings_toasts_for_what_was_added_removed_or_failed()
    {
        var update = new DatasetsUpdate(false, "r2",
        [
            new(DatasetChangeKind.Added, "101GB0050242F", "101GB0050242F.000"),
            new(DatasetChangeKind.Added, "nowhere", "nowhere.gml"),
            new(DatasetChangeKind.Removed, "101GB0040242C", "101GB0040242C.000"),
            new(DatasetChangeKind.Reloaded, "101GB0050242A", "101GB0050242A.000"),
            new(DatasetChangeKind.Failed, "101GB00BROKEN", "101GB00BROKEN.000", "Failed to load: it ends early. It isn't served now."),
        ]);

        var toasts = ServeLive.Toasts(update, id => id != "nowhere");

        Assert.Equal(4, toasts.Count);
        Assert.Equal(("Dataset added:", "101GB0050242F", "Show", "101GB0050242F"), (toasts[0].Title, toasts[0].Text, toasts[0].ActionLabel, toasts[0].DatasetId));
        Assert.Equal(("Dataset added:", "nowhere", (string?)null), (toasts[1].Title, toasts[1].Text, toasts[1].ActionLabel));
        Assert.Equal(("Dataset removed:", "101GB0040242C", "info"), (toasts[2].Title, toasts[2].Text, toasts[2].Tone));
        Assert.Equal(("Couldn't reload 101GB00BROKEN.", "Failed to load: it ends early. It isn't served now.", "error"), (toasts[3].Title, toasts[3].Text, toasts[3].Tone));

        // A reload that worked is quiet; many of a kind are counted in one.
        Assert.Empty(ServeLive.Toasts(new DatasetsUpdate(false, "r3", [new(DatasetChangeKind.Reloaded, "a", "a.000")]), _ => true));
        var many = ServeLive.Toasts(new DatasetsUpdate(false, "r4", [.. Enumerable.Range(0, 5).Select(i => new DatasetChange(DatasetChangeKind.Added, "d" + i, "d" + i + ".000"))]), _ => true);
        Assert.Equal("5 datasets added.", Assert.Single(many).Title);
        var whole = ServeLive.Toasts(DatasetsUpdate.Failed(new DatasetsUpdate(true, "r", []), "r", "Out of memory"), _ => true);
        Assert.Equal(("Couldn't reload the datasets.", "Out of memory. The previous version still serves."), (Assert.Single(whole).Title, whole[0].Text));
    }

    [Fact]
    public void Rows_are_tagged_reloading_while_they_reload_and_new_for_a_minute()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        var newUntil = new Dictionary<string, DateTime>(StringComparer.Ordinal)
        {
            ["added"] = now + ServeLive.NewFor,
            ["earlier"] = now.AddSeconds(-1),
        };
        var reloading = new DatasetsUpdate(true, "r", [new(DatasetChangeKind.Reloading, "a", "a.000"), new(DatasetChangeKind.Reloading, null, null)]);

        var tags = ServeLive.Tags(reloading, newUntil, now);

        Assert.Equal(new Dictionary<string, string> { ["added"] = "New", ["a"] = "Reloading" }, tags);
        Assert.DoesNotContain("earlier", newUntil.Keys);
        Assert.Equal(new Dictionary<string, string> { ["added"] = "New" }, ServeLive.Tags(null, newUntil, now));
        Assert.Empty(ServeLive.Tags(null, newUntil, now + ServeLive.NewFor));
    }

    [Fact]
    public async Task The_page_pins_what_is_not_served_and_replaces_the_reconnect_dialog()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S101), "Fixtures not present.");
        Place(S57);
        Broken("101ZZBROKEN.000");
        Assert.Equal(0, TilesServeCommand.OpenRendered(SettingsFor(_root), out var source));
        using var _ = source!;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source!, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        var html = WebUtility.HtmlDecode(await http.GetStringAsync(server.BaseUri, ct));

        // Not served, pinned above the list with why, counted on the rail and in the Problems filter (D31).
        Assert.Contains("<section class=\"not-served\" aria-label=\"Not served\">", html, StringComparison.Ordinal);
        Assert.Contains("Not served · 1", html, StringComparison.Ordinal);
        Assert.Contains(">101ZZBROKEN.000<", html, StringComparison.Ordinal);
        Assert.Contains(">Failed to load: ", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"rail-badge\" aria-label=\"1 not served\">1</span>", html, StringComparison.Ordinal);
        Assert.Contains("Problems · 1", html, StringComparison.Ordinal);

        // The pill's labels for the link's states, and the banners, which the browser shows (D28, D30).
        Assert.Contains("data-state=\"live\"", html, StringComparison.Ordinal);
        foreach (var (state, label) in new[] { ("reconnecting", "Reconnecting…"), ("disconnected", "Disconnected"), ("stopped", "Server stopped") })
        {
            Assert.Contains($"data-link-state=\"{state}\">{label}<", html, StringComparison.Ordinal);
            Assert.Contains($"data-link-banner=\"{state}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("Can't reach the server.", html, StringComparison.Ordinal);
        Assert.Contains("Live updates paused.", html, StringComparison.Ordinal);
        Assert.Contains("data-link-retry", html, StringComparison.Ordinal);
        Assert.Matches(@"<div id=""components-reconnect-modal"" hidden(="""")?></div>", html);
        Assert.Matches(@"<script src=""js/serve-live(\.[a-z0-9]+)?\.js""></script>", html);

        // Without --refresh, the footer says files aren't checked.
        Assert.Contains("Doesn't check files for changes · revision ", html, StringComparison.Ordinal);
    }
}
