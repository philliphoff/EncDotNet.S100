using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>tiles serve --manage-datasets</c> (issue #1494): datasets added
/// and removed while serving, the tiles of those still served staying cached.
/// </summary>
public sealed class TilesServeManageDatasetsTests : IDisposable
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // Aids to navigation in Chesapeake Bay, far from the S-57 cell (Boston).
    private static string S125 => Path.Combine(AppContext.BaseDirectory, "TestData", "S125", "aton_point.gml");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-manage-datasets-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public TilesServeManageDatasetsTests() => Directory.CreateDirectory(_root);

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

    private string Copy(string file)
    {
        var copy = Path.Combine(_root, Path.GetFileName(file));
        File.Copy(file, copy, overwrite: true);
        return copy;
    }

    private static TilesServeCommand.Settings SettingsFor(string input, string? cacheDirectory = null) => new()
    {
        Input = input,
        MinZoom = 14,
        MaxZoom = 14,
        Parallel = 2,
        // Single-tile blocks, so the cell's tiles are never in a block the area's edge clips.
        Metatile = 1,
        CacheDirectory = cacheDirectory,
        ManageDatasets = true,
    };

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
            line =>
            {
                lock (_log)
                    _log.Add(line);
            });
        datasets = new ServedDatasets(
            source,
            changes => TilesServeCommand.OpenRendered(settings, out var changed, styles, changes, managed) == 0 ? changed : null,
            changes => TileRenderSession.Fingerprint(settings, changes));
        return (source, datasets);
    }

    /// <summary>
    /// A tile that draws, in a whole block away from the edges of the area, so
    /// its block stays the same when the area grows.
    /// </summary>
    private static async Task<(int X, int Y, byte[] Tile)> InnerTileAsync(RenderedTileSource source, int metatile = 1)
    {
        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(14, area.MinY, area.MaxY);
        foreach (int x in XyzTileGrid.Columns(14, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
            {
                if (RenderedTileSource.BlockFor(area, metatile, 14, x, y) is not { } block
                    || block.Columns != metatile || block.Rows != metatile)
                {
                    continue;
                }

                if (await source.ReadAsync(14, x, y, null, null, TestContext.Current.CancellationToken) is { } tile)
                    return (x, y, tile);
            }
        }

        throw new InvalidOperationException("No tile in a whole block drew anything.");
    }

    [Fact]
    public void Changes_add_and_remove_datasets_and_undo_each_other()
    {
        var a = Path.GetFullPath(Path.Combine(_root, "a.000"));
        var b = Path.GetFullPath(Path.Combine(_root, "b.000"));

        var added = DatasetChanges.None.WithAdded(a);
        Assert.Equal([a], added.Added);
        Assert.Same(added, added.WithAdded(a));
        Assert.True(added.WithRemoved(a).IsEmpty);

        var removed = DatasetChanges.None.WithRemoved(b);
        Assert.Equal([b], removed.Removed);
        Assert.True(removed.WithAdded(b).IsEmpty);
    }

    [Fact]
    public void The_fingerprint_without_changes_is_unchanged_and_changes_change_it()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var settings = SettingsFor(Copy(S57));
        var unchanged = TileRenderSession.Fingerprint(settings);

        Assert.Equal(unchanged, TileRenderSession.Fingerprint(settings, DatasetChanges.None));
        var added = TileRenderSession.Fingerprint(settings, DatasetChanges.None.WithAdded(Copy(S125)));
        Assert.NotEqual(unchanged, added);
        Assert.NotEqual(added, TileRenderSession.Fingerprint(settings, DatasetChanges.None.WithRemoved(settings.Input!)));
    }

    [Fact]
    public async Task An_added_dataset_draws_while_the_existing_tiles_stay_cached()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Copy(S57);
        var atons = Copy(S125);
        var (source, datasets) = Managed(SettingsFor(cell));
        await using var _ = source;
        var ct = TestContext.Current.CancellationToken;

        var before = source.Current;
        var (x, y, tile) = await InnerTileAsync(before);
        Assert.Equal(1, before.BlocksRendered);

        var added = await datasets.AddAsync([atons]);
        Assert.True(added.Succeeded, added.Error);
        var after = source.Current;
        Assert.NotSame(before, after);
        Assert.Equal([cell, atons], datasets.Datasets.Select(d => d.Path));
        Assert.Equal(before.Layout.ReferenceLatitude, after.Layout.ReferenceLatitude);

        // The area now reaches the aids to navigation, and TileJSON says so.
        Assert.True(after.Layout.Area.MinX < before.Layout.Area.MinX);
        Assert.True(after.Layout.Area.MinY < before.Layout.Area.MinY);
        var bounds = source.ToTileJson(null)["bounds"]!.AsArray();
        Assert.True((double)bounds[1]! < 37.1);

        // The cell's tile is served from the cache, not rendered again…
        Assert.Equal(tile, await source.ReadAsync(14, x, y, null, null, ct));
        Assert.Equal(0, after.BlocksRendered);

        // …and the added dataset's tiles draw.
        var (lon, lat) = (-76.0133, 36.95);
        var (mx, my) = WebMercator.FromLonLat(lon, lat);
        int n = XyzTileGrid.TilesPerAxis(14);
        int ax = (int)Math.Floor((mx + XyzTileGrid.Extent) / XyzTileGrid.TileWorldSize(14));
        int ay = (int)Math.Floor((XyzTileGrid.Extent - my) / XyzTileGrid.TileWorldSize(14));
        Assert.InRange(ax, 0, n - 1);
        Assert.NotNull(await source.ReadAsync(14, ax, ay, null, null, ct));
        Assert.Equal(1, after.BlocksRendered);

        // Removing it again keeps the cell's tile cached too.
        var removed = await datasets.RemoveAsync([atons]);
        Assert.True(removed.Succeeded, removed.Error);
        Assert.True(datasets.Changes.IsEmpty);
        Assert.Equal(tile, await source.ReadAsync(14, x, y, null, null, ct));
        Assert.Equal(0, source.Current.BlocksRendered);
        Assert.Null(await source.ReadAsync(14, ax, ay, null, null, ct));
    }

    [Fact]
    public async Task A_dataset_added_over_a_cached_tile_renders_it_again()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = Copy(S57);
        var overlapping = Path.Combine(_root, "other", "US5MA1BO.000");
        Directory.CreateDirectory(Path.GetDirectoryName(overlapping)!);
        File.Copy(S57, overlapping);
        var (source, datasets) = Managed(SettingsFor(cell));
        await using var _ = source;

        var (x, y, _) = await InnerTileAsync(source.Current);
        Assert.True((await datasets.AddAsync([overlapping])).Succeeded);

        Assert.NotNull(await source.ReadAsync(14, x, y, null, null, TestContext.Current.CancellationToken));
        Assert.Equal(1, source.Current.BlocksRendered);
    }

    [Fact]
    public async Task Disk_cached_tiles_are_kept_per_dataset()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Copy(S57);
        var atons = Copy(S125);
        var cache = Path.Combine(_root, "cache");
        int x, y;
        byte[] tile;

        // One server renders the cell's tile alone…
        var (first, _) = Managed(SettingsFor(cell, cache));
        await using (first)
        {
            (x, y, tile) = await InnerTileAsync(first.Current);
        }

        // …and another, its memory empty, with the aids to navigation added, reads it from disk.
        var (second, datasets) = Managed(SettingsFor(cell, cache));
        await using (second)
        {
            Assert.True((await datasets.AddAsync([atons])).Succeeded);
            Assert.Equal(tile, await second.ReadAsync(14, x, y, null, null, TestContext.Current.CancellationToken));
            Assert.Equal(0, second.Current.BlocksRendered);
        }
    }

    [Fact]
    public async Task A_change_that_fails_keeps_the_datasets_served()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = Copy(S57);
        var (source, datasets) = Managed(SettingsFor(cell));
        await using var _ = source;
        var before = source.Current;

        var text = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(text, "not a dataset", TestContext.Current.CancellationToken);
        Assert.Equal(400, (await datasets.AddAsync([text])).Status);
        Assert.Equal(400, (await datasets.AddAsync([Path.Combine(_root, "missing.000")])).Status);
        Assert.Equal(404, (await datasets.RemoveAsync([text])).Status);
        Assert.Equal(409, (await datasets.RemoveAsync([cell])).Status);

        // Adding what is served already changes nothing.
        Assert.Equal(200, (await datasets.AddAsync([cell])).Status);
        Assert.Same(before, source.Current);
        Assert.True(datasets.Changes.IsEmpty);
    }

    [Theory]
    [InlineData("{\"paths\": [\"/a\", \"/b\"]}", 2)]
    [InlineData("{\"path\": \"/a\"}", 1)]
    [InlineData("{\"paths\": []}", 0)]
    [InlineData("{\"paths\": [1]}", 0)]
    [InlineData("[\"/a\"]", 0)]
    [InlineData("not json", 0)]
    public void Request_bodies_name_the_paths(string body, int count)
    {
        bool parsed = ServedDatasets.TryParsePaths(Encoding.UTF8.GetBytes(body), out var paths, out _);
        Assert.Equal(count > 0, parsed);
        Assert.Equal(count, paths.Count);
    }

    [Fact]
    public async Task The_datasets_endpoint_adds_and_removes_datasets_under_the_token()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Copy(S57);
        var atons = Copy(S125);
        var (source, datasets) = Managed(SettingsFor(cell));
        await using var _ = source;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct, datasets: datasets);
        using var http = new HttpClient();
        var uri = new Uri(server.BaseUri, ServedDatasets.Path);

        // Only under the token.
        using (var outside = await http.PostAsJsonAsync(new Uri($"http://127.0.0.1:{server.Port}/datasets"), new { paths = new[] { atons } }, ct))
            Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Single(datasets.Datasets);

        // Not JSON: refused, as a form posted from another site would be.
        using (var form = await http.PostAsync(uri, new StringContent("{\"paths\": [\"" + atons + "\"]}", Encoding.UTF8, "text/plain"), ct))
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode);

        using (var add = await http.PostAsJsonAsync(uri, new { paths = new[] { atons } }, ct))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            var json = JsonNode.Parse(await add.Content.ReadAsStringAsync(ct))!;
            Assert.Equal([cell, atons], json["datasets"]!.AsArray().Select(d => (string?)d!["path"]));
            Assert.Equal("S-125", (string?)json["datasets"]![1]!["spec"]);
        }

        // The documents describe the new set.
        var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Contains("S-125", (string?)tileJson["description"], StringComparison.Ordinal);

        using (var remove = new HttpRequestMessage(HttpMethod.Delete, uri) { Content = JsonContent.Create(new { path = atons }) })
        using (var response = await http.SendAsync(remove, ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
            Assert.Single(json["datasets"]!.AsArray());
        }

        using (var bad = await http.PostAsJsonAsync(uri, new { paths = new[] { Path.Combine(_root, "missing.000") } }, ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.NotNull(JsonNode.Parse(await bad.Content.ReadAsStringAsync(ct))!["error"]);
        }
    }

    [Fact]
    public async Task Without_manage_datasets_datasets_cannot_be_changed()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        Assert.Equal(0, TilesServeCommand.OpenRendered(SettingsFor(Copy(S57)), out var rendered));
        using var source = rendered!;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        // Not found, or not allowed where a GET describes the datasets (issue #1504).
        using var add = await http.PostAsJsonAsync(new Uri(server.BaseUri, ServedDatasets.Path), new { paths = new[] { Copy(S125) } }, ct);
        Assert.Contains(add.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        using var remove = new HttpRequestMessage(HttpMethod.Delete, new Uri(server.BaseUri, ServedDatasets.Path)) { Content = JsonContent.Create(new { path = "x" }) };
        Assert.Contains((await http.SendAsync(remove, ct)).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Single(source.Session.Paths);
    }

    [Fact]
    public async Task The_dataset_metadata_lists_the_changed_set()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Copy(S57);
        var atons = Copy(S125);
        var (source, datasets) = Managed(SettingsFor(cell));
        await using var _ = source;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct, datasets: datasets);
        using var http = new HttpClient();
        var uri = new Uri(server.BaseUri, ServedDatasets.Path);

        async Task<string[]> FilesAsync()
        {
            var json = JsonNode.Parse(await http.GetStringAsync(uri, ct))!;
            return [.. json["datasets"]!.AsArray().Select(d => (string)d!["file"]!)];
        }

        Assert.Equal(["US5MA1BO.000"], await FilesAsync());

        using (var add = await http.PostAsJsonAsync(uri, new { paths = new[] { atons } }, ct))
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(["US5MA1BO.000", "aton_point.gml"], await FilesAsync());

        using (var remove = new HttpRequestMessage(HttpMethod.Delete, uri) { Content = JsonContent.Create(new { path = cell }) })
        using (var response = await http.SendAsync(remove, ct))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["aton_point.gml"], await FilesAsync());
    }

    [Fact]
    public async Task A_change_publishes_a_new_revision()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Copy(S57);
        var atons = Copy(S125);
        var (source, datasets) = Managed(SettingsFor(cell));
        await using var _ = source;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct, datasets: datasets);
        using var http = new HttpClient();
        var healthz = new Uri($"http://127.0.0.1:{server.Port}{ServeHost.HealthPath}");

        async Task<(string? TileJson, string? Health)> RevisionsAsync()
        {
            var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
            using var health = await http.GetAsync(healthz, ct);
            health.EnsureSuccessStatusCode();
            return ((string?)tileJson["s100"]!["revision"], health.Headers.GetValues(ServeHost.RevisionHeader).Single());
        }

        var before = await RevisionsAsync();
        Assert.NotNull(before.TileJson);
        Assert.Equal(before.TileJson, before.Health);

        using (var add = await http.PostAsJsonAsync(new Uri(server.BaseUri, ServedDatasets.Path), new { paths = new[] { atons } }, ct))
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var added = await RevisionsAsync();
        Assert.NotEqual(before.TileJson, added.TileJson);
        Assert.Equal(added.TileJson, added.Health);

        // Removing it serves the start-up set, at its revision, again.
        Assert.True((await datasets.RemoveAsync([atons])).Succeeded);
        Assert.Equal(before, await RevisionsAsync());

        // An unchanged set keeps its revision.
        Assert.Equal(200, (await datasets.AddAsync([cell])).Status);
        Assert.Equal(before, await RevisionsAsync());
    }

    [Fact]
    public void Manage_datasets_does_not_apply_to_a_built_tile_set()
    {
        var tileSet = Path.Combine(_root, "tiles");
        Directory.CreateDirectory(tileSet);
        File.WriteAllText(Path.Combine(tileSet, XyzDirectoryTileSink.TileJsonFileName), """{"format":"png"}""");

        var result = new TilesServeCommand.Settings { Input = tileSet, ManageDatasets = true }.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--manage-datasets applies only when rendering datasets", result.Message);
    }
}
