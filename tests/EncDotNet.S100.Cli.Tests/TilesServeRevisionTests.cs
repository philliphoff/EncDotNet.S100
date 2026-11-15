using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the data revision of <c>tiles serve</c> (issue #1497): the
/// disk-cache fingerprint published in TileJSON, the WMS and WMTS
/// capabilities (<c>updateSequence</c>) and a <c>/healthz</c> header, and
/// changing when <c>--refresh</c> reloads changed datasets.
/// </summary>
public sealed class TilesServeRevisionTests : IDisposable
{
    private static readonly XNamespace Wms = "http://www.opengis.net/wms";
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-revision-" + Guid.NewGuid().ToString("N"));

    public TilesServeRevisionTests() => Directory.CreateDirectory(_root);

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

    /// <summary>A source whose revision the test sets, describing it as a rendered source does.</summary>
    private sealed class FakeSource : ITileSource
    {
        public string? Current { get; set; } = "0123456789abcdef0123456789abcdef";

        public string Path => "fake";

        public TileImageFormat Format => TileImageFormat.Png;

        public string? Revision => Current;

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(null);

        public JsonObject ToTileJson(string? palette, DateTime? time = null)
        {
            var s100 = new JsonObject { ["palette"] = "day" };
            if (Current is not null)
                s100["revision"] = Current;
            return new JsonObject
            {
                ["name"] = "Fake chart",
                ["minzoom"] = 1,
                ["maxzoom"] = 3,
                ["bounds"] = new JsonArray(-10.0, -5.0, 10.0, 5.0),
                ["tileSize"] = 256,
                ["s100"] = s100,
            };
        }
    }

    [Fact]
    public async Task The_revision_is_published_in_capabilities_and_the_health_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new FakeSource();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct);
        using var http = new HttpClient();

        var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        var wms = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        var wmts = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        using var health = await http.GetAsync($"http://127.0.0.1:{server.Port}/healthz", ct);

        Assert.Equal(source.Current, (string?)tileJson["s100"]!["revision"]);
        Assert.Equal(source.Current, (string?)wms.Root!.Attribute("updateSequence"));
        Assert.Equal(Wms + "WMS_Capabilities", wms.Root.Name);
        Assert.Equal(source.Current, (string?)wmts.Root!.Attribute("updateSequence"));
        Assert.Equal(Wmts + "Capabilities", wmts.Root.Name);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync(ct));
        Assert.Equal(source.Current, health.Headers.GetValues(ServeHost.RevisionHeader).Single());

        // The health check reads the revision as it is now, not at start-up.
        source.Current = "fedcba9876543210fedcba9876543210";
        using var later = await http.GetAsync($"http://127.0.0.1:{server.Port}/healthz", ct);
        Assert.Equal(source.Current, later.Headers.GetValues(ServeHost.RevisionHeader).Single());
    }

    [Fact]
    public async Task Without_a_revision_nothing_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new FakeSource { Current = null };
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        var wms = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        var wmts = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        using var health = await http.GetAsync($"http://127.0.0.1:{server.Port}/healthz", ct);

        Assert.Null(wms.Root!.Attribute("updateSequence"));
        Assert.Null(wmts.Root!.Attribute("updateSequence"));
        Assert.False(health.Headers.Contains(ServeHost.RevisionHeader));
        Assert.Equal("ok", await health.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public void A_built_tile_set_has_no_revision()
    {
        var xyz = Path.Combine(_root, "xyz");
        Directory.CreateDirectory(xyz);
        File.WriteAllText(Path.Combine(xyz, XyzDirectoryTileSink.TileJsonFileName), """{"format":"png"}""");

        ITileSource source = TileSource.Open(xyz);

        Assert.Null(source.Revision);
    }

    [Fact]
    public async Task Changing_a_dataset_changes_the_revision()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = Path.Combine(_root, Path.GetFileName(S57));
        File.Copy(S57, cell);
        var settings = new TilesServeCommand.Settings
        {
            Input = cell,
            MinZoom = 14,
            MaxZoom = 14,
            Parallel = 2,
            CacheDirectory = Path.Combine(_root, "cache"),
        };

        var fingerprint = TileRenderSession.Fingerprint(settings);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial));
        await using var source = new RefreshingTileSource(
            initial!,
            fingerprint,
            () => TileRenderSession.Fingerprint(settings),
            () => TilesServeCommand.OpenRendered(settings, out var reopened) == 0 ? reopened : null,
            Timeout.InfiniteTimeSpan,
            _ => { });

        // The revision is the disk-cache fingerprint, published in the TileJSON.
        var before = source.Revision;
        Assert.Equal(fingerprint, before);
        Assert.Equal(before, (string?)source.ToTileJson(palette: null)["s100"]!["revision"]);

        // Unchanged files: the same revision.
        Assert.False(await source.CheckAsync());
        Assert.Equal(before, source.Revision);

        // A new version of the cell: once reloaded, a new revision everywhere.
        File.SetLastWriteTimeUtc(cell, DateTime.UtcNow.AddMinutes(5));
        Assert.False(await source.CheckAsync());
        Assert.True(await source.CheckAsync());

        var after = source.Revision;
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
        Assert.Equal(TileRenderSession.Fingerprint(settings), after);
        Assert.Equal(after, (string?)source.ToTileJson(palette: null)["s100"]!["revision"]);
        Assert.Equal(after, (string?)XDocument.Parse(WmtsService.BuildCapabilities(source, new Uri("http://localhost/"))).Root!.Attribute("updateSequence"));
        Assert.Equal(after, (string?)XDocument.Parse(WmsService.BuildCapabilities(source, new Uri("http://localhost/"))).Root!.Attribute("updateSequence"));
    }

    [Fact]
    public void Changing_a_setting_that_changes_pixels_changes_the_revision()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var plain = new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, Parallel = 1 };
        var scaled = new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, Parallel = 1, SymbolScale = 2 };

        Assert.Equal(0, TilesServeCommand.OpenRendered(plain, out var first));
        Assert.Equal(0, TilesServeCommand.OpenRendered(scaled, out var second));
        using (first)
        using (second)
        {
            Assert.Equal(TileRenderSession.Fingerprint(plain), first!.Revision);
            Assert.NotEqual(first.Revision, second!.Revision);
        }
    }
}
