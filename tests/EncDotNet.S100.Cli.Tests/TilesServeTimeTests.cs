using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for choosing the time step of time-varying datasets (S-104, S-111) by
/// URL in <c>tiles serve</c> (issue #865): the <c>t</c> query parameter, its
/// snapping to the datasets' steps, TileJSON's time list, the bounded scene
/// cache, and vector portrayal reused across time steps.
/// </summary>
public sealed class TilesServeTimeTests
{
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private static readonly DateTime First = new(2026, 3, 20, 19, 0, 0, DateTimeKind.Utc);

    private static DateTime Hours(int hours) => First.AddHours(hours);

    private static RenderedTileSource OpenRendered(TilesServeCommand.Settings settings)
    {
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    /// <summary>A tile that draws something at the first time step, two levels below the minimum zoom.</summary>
    private static async Task<(int Zoom, int X, int Y)> DrawnTileAsync(RenderedTileSource source)
    {
        int zoom = source.Layout.MinZoom + 2;
        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
        foreach (int x in XyzTileGrid.Columns(zoom, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
            {
                if (await source.ReadAsync(zoom, x, y, null, First, TestContext.Current.CancellationToken) is not null)
                    return (zoom, x, y);
            }
        }

        throw new InvalidOperationException("No tile drew anything.");
    }

    [Fact]
    public async Task Each_time_step_renders_its_own_tiles()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S111, Parallel = 2 });
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(48, source.Times.Count);
        Assert.Equal(First, source.Times[0]);
        Assert.Equal(DateTimeKind.Utc, source.Times[0].Kind);

        var (zoom, x, y) = await DrawnTileAsync(source);
        var first = await source.ReadAsync(zoom, x, y, null, First, ct);
        var later = await source.ReadAsync(zoom, x, y, null, Hours(12), ct);

        Assert.NotNull(later);
        Assert.NotEqual(first, later);

        // Without t, --time-step (0, the first step) applies.
        Assert.Equal(first, await source.ReadAsync(zoom, x, y, null, null, ct));

        // An instant between steps snaps to the nearest; one outside the forecast to its end.
        Assert.Equal(later, await source.ReadAsync(zoom, x, y, null, Hours(12).AddMinutes(-20), ct));
        Assert.Equal(first, await source.ReadAsync(zoom, x, y, null, First.AddDays(-3), ct));

        // Palettes and time steps combine.
        var night = await source.ReadAsync(zoom, x, y, "night", Hours(12), ct);
        Assert.NotNull(night);
        Assert.NotEqual(later, night);
    }

    [Fact]
    public async Task Only_a_few_scenes_are_kept_while_stepping_through_a_forecast()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S111, Parallel = 2 });
        var (zoom, x, y) = await DrawnTileAsync(source);

        for (int hour = 0; hour < 12; hour++)
            Assert.NotNull(await source.ReadAsync(zoom, x, y, null, Hours(hour), TestContext.Current.CancellationToken));

        Assert.Equal(SceneCache.Capacity, source.ScenesHeld);
    }

    [Fact]
    public async Task Datasets_that_dont_vary_with_time_ignore_t()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14 });
        var ct = TestContext.Current.CancellationToken;

        Assert.Empty(source.Times);
        Assert.Null(source.ToTileJson(null, First)["s100"]!["times"]);
        Assert.Null(source.ToTileJson(null, First)["s100"]!["time"]);

        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(14, area.MinY, area.MaxY);
        foreach (int x in XyzTileGrid.Columns(14, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
                Assert.Equal(await source.ReadAsync(14, x, y, null, null, ct), await source.ReadAsync(14, x, y, null, First, ct));
        }
    }

    [Fact]
    public void Datasets_that_dont_vary_with_time_are_portrayed_once_per_palette()
    {
        Assert.SkipUnless(File.Exists(S111) && File.Exists(S57), "S-57 or S-111 fixture not present.");
        var settings = new TilesServeCommand.Settings { Layers = [S57, S111] };
        Assert.Equal(0, TileRenderSession.TryOpen(settings, out var session));
        using (session)
        {
            var early = session!.Prepare("day", Hours(1));
            var late = session.Prepare("day", Hours(20));
            var night = session.Prepare("night", Hours(1));

            // The chart's portrayal is shared across time steps, not across palettes.
            Assert.Same(Assert.Single(early.VectorResults), Assert.Single(late.VectorResults));
            Assert.NotSame(early.VectorResults[0], night.VectorResults[0]);
        }
    }

    [Fact]
    public async Task Time_steps_are_chosen_by_url_and_listed_in_tilejson()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S111, Parallel = 2 });
        var (zoom, x, y) = await DrawnTileAsync(source);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        var plain = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Equal(48, plain["s100"]!["times"]!.AsArray().Count);
        Assert.Equal("2026-03-20T19:00:00Z", (string?)plain["s100"]!["times"]![0]);
        Assert.Null(plain["s100"]!["time"]);
        Assert.Equal($"http://127.0.0.1:{server.Port}/{{z}}/{{x}}/{{y}}.png", (string?)plain["tiles"]![0]);

        // A requested instant is snapped, and carried into the tile URL.
        var timed = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "night/tiles.json?t=2026-03-21T06:40Z"), ct))!;
        Assert.Equal("2026-03-21T07:00:00Z", (string?)timed["s100"]!["time"]);
        Assert.Equal("night", (string?)timed["s100"]!["palette"]);
        Assert.Equal(
            $"http://127.0.0.1:{server.Port}/night/{{z}}/{{x}}/{{y}}.png?t=2026-03-21T07%3A00%3A00Z",
            (string?)timed["tiles"]![0]);

        var tile = await http.GetByteArrayAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png?t=2026-03-21T07:00:00Z"), ct);
        Assert.Equal(await source.ReadAsync(zoom, x, y, null, Hours(12), ct), tile);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.GetAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png?t=yesterday"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.GetAsync(new Uri(server.BaseUri, "tiles.json?t=yesterday"), ct)).StatusCode);
    }

    [Fact]
    public void Times_without_a_kind_are_taken_as_utc()
    {
        var unspecified = new DateTime(2026, 3, 21, 7, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(Hours(12), TileRenderSession.AsUtc(unspecified));
        Assert.Equal(DateTimeKind.Utc, TileRenderSession.AsUtc(unspecified).Kind);
        Assert.Equal("2026-03-21T07:00:00Z", TileRenderSession.FormatTime(unspecified));
        Assert.Equal(Hours(12), TileRenderSession.AsUtc(Hours(12).ToLocalTime()));
    }

    [Fact]
    public void Disk_cached_time_steps_are_kept_apart()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiles-time-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new DiskTileCache(root, "0123456789abcdef0123456789abcdef", TileImageFormat.Png, 1 << 20);
            cache.Add(new TileKey("day", 3, 1, 1), [1]);
            cache.Add(new TileKey("day", 3, 1, 1, Hours(12)), [2]);

            Assert.True(cache.TryGet(new TileKey("day", 3, 1, 1), out var plain));
            Assert.True(cache.TryGet(new TileKey("day", 3, 1, 1, Hours(12)), out var timed));
            Assert.Equal([1], plain);
            Assert.Equal([2], timed);
            Assert.True(File.Exists(Path.Combine(root, "0123456789abcdef0123456789abcdef", "day-20260321T070000Z", "3", "1", "1.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
