using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>tiles serve --strict-time</c> (issue #1498): a time more than
/// half a step outside a time-varying dataset's steps draws nothing for it,
/// instead of snapping to its nearest step.
/// </summary>
public sealed class TilesServeStrictTimeTests
{
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // The S-111 fixture's 48 hourly steps.
    private static readonly DateTime First = new(2026, 3, 20, 19, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Last = First.AddHours(47);

    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double hours) => Origin.AddHours(hours);

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

    // Hourly steps 00:00–03:00, a gap, then 10:00–12:00.
    private static readonly DateTime[] Steps = [At(0), At(1), At(2), At(3), At(10), At(11), At(12)];

    [Fact]
    public void A_time_in_range_draws_its_nearest_step()
    {
        Assert.Equal(At(1), TileRenderSession.StrictStep(Steps, At(1)));
        Assert.Equal(At(1), TileRenderSession.StrictStep(Steps, At(1.4)));
        Assert.Equal(At(2), TileRenderSession.StrictStep(Steps, At(1.6)));

        // Half a step either side of the range still draws its end.
        Assert.Equal(At(3), TileRenderSession.StrictStep(Steps, At(3.5)));
        Assert.Equal(At(0), TileRenderSession.StrictStep(Steps, At(-0.5)));
    }

    [Fact]
    public void A_time_in_a_gap_draws_nothing()
    {
        Assert.Null(TileRenderSession.StrictStep(Steps, At(5)));
        Assert.Null(TileRenderSession.StrictStep(Steps, At(9.4)));
        Assert.Equal(At(10), TileRenderSession.StrictStep(Steps, At(9.5)));
    }

    [Fact]
    public void A_time_before_or_after_the_range_draws_nothing()
    {
        Assert.Null(TileRenderSession.StrictStep(Steps, At(-0.6)));
        Assert.Null(TileRenderSession.StrictStep(Steps, At(-48)));
        Assert.Null(TileRenderSession.StrictStep(Steps, At(12.6)));
        Assert.Null(TileRenderSession.StrictStep(Steps, At(24 * 365)));

        // A single step counts as an hour long.
        Assert.Equal(At(0), TileRenderSession.StrictStep([At(0)], At(0.5)));
        Assert.Null(TileRenderSession.StrictStep([At(0)], At(0.6)));
        Assert.Null(TileRenderSession.StrictStep([], At(0)));
    }

    [Fact]
    public async Task Without_strict_time_times_outside_the_range_still_snap()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S111, Parallel = 2 });
        var (zoom, x, y) = await DrawnTileAsync(source);
        var ct = TestContext.Current.CancellationToken;

        Assert.False(source.StrictTime);
        Assert.Equal(
            await source.ReadAsync(zoom, x, y, null, First, ct),
            await source.ReadAsync(zoom, x, y, null, First.AddDays(-3), ct));
        Assert.Null(source.ToTileJson(null, First.AddDays(-3))["s100"]!["datasets"]);
        Assert.Null(source.ToTileJson(null, First.AddDays(-3))["s100"]!["strictTime"]);
    }

    [Fact]
    public async Task Strict_time_draws_nothing_before_or_after_a_dataset()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S111, Parallel = 2, StrictTime = true });
        var (zoom, x, y) = await DrawnTileAsync(source);
        var ct = TestContext.Current.CancellationToken;
        var first = await source.ReadAsync(zoom, x, y, null, First, ct);

        // In range, as without --strict-time.
        Assert.NotNull(first);
        Assert.Equal(first, await source.ReadAsync(zoom, x, y, null, First.AddMinutes(-29), ct));
        Assert.NotNull(await source.ReadAsync(zoom, x, y, null, Last.AddMinutes(29), ct));

        // Before and after the range.
        Assert.Null(await source.ReadAsync(zoom, x, y, null, First.AddMinutes(-31), ct));
        Assert.Null(await source.ReadAsync(zoom, x, y, null, Last.AddMinutes(31), ct));
        Assert.Null(await source.ReadAsync(zoom, x, y, null, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct));

        // Without t, --time-step applies.
        Assert.Equal(first, await source.ReadAsync(zoom, x, y, null, null, ct));
    }

    [Fact]
    public void Strict_time_still_draws_datasets_that_dont_vary_with_time()
    {
        Assert.SkipUnless(File.Exists(S111) && File.Exists(S57), "S-57 or S-111 fixture not present.");
        var settings = new TilesServeCommand.Settings { Layers = [S57, S111], StrictTime = true };
        Assert.Equal(0, TileRenderSession.TryOpen(settings, out var session));
        using (session)
        {
            var outside = session!.ChooseTime(First.AddDays(-3), strict: true);
            Assert.False(outside.Empty);
            Assert.NotNull(outside.Variant);
            var described = Assert.Single(session.DescribeTime(outside));
            Assert.Equal("S-111", described.Spec);
            Assert.Null(described.Step);

            // The S-111 is left out of the scene; the chart still draws.
            var scene = session.Prepare("day", outside.Time, outside.Steps);
            Assert.Single(scene.VectorResults);
            Assert.NotNull(session.ResolveLayout(scene));

            // In range, the default scene serves.
            var inside = session.ChooseTime(First.AddHours(3).AddMinutes(10), strict: true);
            Assert.Equal(First.AddHours(3), inside.Time);
            Assert.Null(inside.Variant);
            Assert.Equal(First.AddHours(3), Assert.Single(session.DescribeTime(inside)).Step);
        }
    }

    [Fact]
    public async Task Strict_time_answers_204_and_lists_datasets_without_data_in_tilejson()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S111, Parallel = 2, StrictTime = true });
        var (zoom, x, y) = await DrawnTileAsync(source);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.OK,
            (await http.GetAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png?t=2026-03-21T07:00:00Z"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.GetAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png?t=2030-01-01T00:00:00Z"), ct)).StatusCode);

        var inside = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json?t=2026-03-21T06:40Z"), ct))!;
        Assert.True((bool)inside["s100"]!["strictTime"]!);
        Assert.Equal("2026-03-21T07:00:00Z", (string?)inside["s100"]!["time"]);
        var dataset = Assert.Single(inside["s100"]!["datasets"]!.AsArray())!;
        Assert.Equal("111US00_DBOFS_20260320T18Z_US4DE1BB.h5", (string?)dataset["name"]);
        Assert.Equal("S-111", (string?)dataset["spec"]);
        Assert.Equal("2026-03-21T07:00:00Z", (string?)dataset["time"]);

        // Outside the range, the dataset has no time, and the tile URL keeps
        // the time asked for so its tiles draw nothing too.
        var outside = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json?t=2030-01-01T00:00Z"), ct))!;
        Assert.Equal("2030-01-01T00:00:00Z", (string?)outside["s100"]!["time"]);
        Assert.Null(Assert.Single(outside["s100"]!["datasets"]!.AsArray())!["time"]);
        Assert.EndsWith("?t=2030-01-01T00%3A00%3A00Z", (string?)outside["tiles"]![0], StringComparison.Ordinal);

        // WMTS answers 204 for an empty tile too.
        var wmts = await http.GetAsync(new Uri(server.BaseUri,
            $"wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=s100&STYLE=default&FORMAT=image/png&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX={zoom}&TILEROW={y}&TILECOL={x}&Time=2030-01-01T00:00:00Z"), ct);
        Assert.Equal(HttpStatusCode.NoContent, wmts.StatusCode);
    }

    [Fact]
    public void Disk_cached_strict_variants_are_kept_apart()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiles-strict-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = new DiskTileCache(root, "0123456789abcdef0123456789abcdef", TileImageFormat.Png, 1 << 20);
            cache.Add(new TileKey("day", 3, 1, 1, First), [1]);
            cache.Add(new TileKey("day", 3, 1, 1, First, "0011223344556677"), [2]);

            Assert.True(cache.TryGet(new TileKey("day", 3, 1, 1, First), out var plain));
            Assert.True(cache.TryGet(new TileKey("day", 3, 1, 1, First, "0011223344556677"), out var variant));
            Assert.Equal([1], plain);
            Assert.Equal([2], variant);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
