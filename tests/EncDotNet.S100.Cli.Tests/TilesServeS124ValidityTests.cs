using System.Net;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for S-124 by validity in <c>tiles serve</c> (issue #1499): a time
/// asked for (<c>t</c>, WMS <c>TIME</c>, the WMTS Time dimension) draws only
/// the warnings in force then, as the viewer's map does at its view time.
/// </summary>
/// <remarks>
/// The synthetic fixture's NAVAREA X 120/26 has three parts: part 1 (a point)
/// in force 10–20 Oct 2026, part 2 (a line with an affected area and text)
/// ended 5 Oct, and part 3 (an area) from 25 Oct.
/// </remarks>
public sealed class TilesServeS124ValidityTests
{
    private static string S124 => Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_drawing_parts.gml");

    private const string Part1 = "ZZ.S124.NAVAREAX.120.26.1";
    private const string Part2 = "ZZ.S124.NAVAREAX.120.26.2";
    private const string Part3 = "ZZ.S124.NAVAREAX.120.26.3";

    private static readonly DateTime Part1InForce = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Part3InForce = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforePublication = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    // Part 1's point, clear of part 3's area.
    private const double Part1Longitude = 146.00;
    private const double Part1Latitude = -16.50;

    private static RenderedTileSource OpenRendered()
    {
        var settings = new TilesServeCommand.Settings { Input = S124, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    /// <summary>The tile holding part 1's point, at a zoom where it is clear of part 3.</summary>
    private static (int Zoom, int X, int Y) Part1Tile(RenderedTileSource source)
    {
        int zoom = Math.Clamp(12, source.Layout.MinZoom, source.Layout.MaxZoom);
        var (mx, my) = WebMercator.FromLonLat(Part1Longitude, Part1Latitude);
        double size = XyzTileGrid.TileWorldSize(zoom);
        int x = (int)Math.Floor((mx + XyzTileGrid.Extent) / size);
        int y = (int)Math.Floor((XyzTileGrid.Extent - my) / size);
        return (zoom, x, y);
    }

    [Fact]
    public void A_time_leaves_out_the_parts_not_in_force()
    {
        var settings = new TilesServeCommand.Settings { Input = S124 };
        Assert.Equal(0, TileRenderSession.TryOpen(settings, out var session));
        using (session)
        {
            // S-124 doesn't vary with time in steps, so no time is snapped to.
            Assert.Empty(session!.Times);

            var october15 = session.ChooseTime(Part1InForce, strict: false);
            Assert.Null(october15.Time);
            Assert.NotNull(october15.Variant);
            var hidden = Assert.Single(october15.HiddenWarnings!)!;
            Assert.Equal([Part2, Part3], hidden.Order(StringComparer.Ordinal));

            var october27 = session.ChooseTime(Part3InForce, strict: false);
            Assert.Equal([Part1, Part2], october27.HiddenWarnings![0]!.Order(StringComparer.Ordinal));
            Assert.NotEqual(october15.Variant, october27.Variant);

            // Times with the same warnings in force share their tiles.
            Assert.Equal(october15.Variant, session.ChooseTime(Part1InForce.AddDays(2), strict: false).Variant);

            // --strict-time judges warnings the same way.
            Assert.Equal(october15.Variant, session.ChooseTime(Part1InForce, strict: true).Variant);

            // A warning not yet in force isn't drawn (S-98 §12.12.2).
            Assert.Equal([Part1, Part2, Part3], session.ChooseTime(BeforePublication, strict: false).HiddenWarnings![0]!.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task A_warning_draws_inside_its_validity_and_not_outside_it()
    {
        using var source = OpenRendered();
        var (zoom, x, y) = Part1Tile(source);
        var ct = TestContext.Current.CancellationToken;

        // Without a time, every warning draws, as before.
        Assert.NotNull(await source.ReadAsync(zoom, x, y, null, null, ct));

        // In force: part 1 draws.
        Assert.NotNull(await source.ReadAsync(zoom, x, y, null, Part1InForce, ct));

        // Ended, or not yet in force: nothing draws there.
        Assert.Null(await source.ReadAsync(zoom, x, y, null, Part3InForce, ct));
        Assert.Null(await source.ReadAsync(zoom, x, y, null, BeforePublication, ct));

        // The time asked for is echoed so tile URLs built from it draw the same.
        Assert.Equal("2026-10-27T12:00:00Z", (string?)source.ToTileJson(null, Part3InForce)["s100"]!["time"]);
        Assert.Null(source.ToTileJson(null)["s100"]!["time"]);
    }

    [Fact]
    public async Task Wms_time_and_the_wmts_time_dimension_filter_warnings()
    {
        using var source = OpenRendered();
        var (zoom, x, y) = Part1Tile(source);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        // ?t= on the XYZ tile URLs.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png?t=2026-10-15T12:00:00Z"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png?t=2026-10-27T12:00:00Z"), ct)).StatusCode);

        // The WMTS Time dimension.
        string Wmts(string time) =>
            $"wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=s100&STYLE=default&FORMAT=image/png&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX={zoom}&TILEROW={y}&TILECOL={x}&Time={time}";
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(server.BaseUri, Wmts("2026-10-15T12:00:00Z")), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(server.BaseUri, Wmts("2026-10-27T12:00:00Z")), ct)).StatusCode);

        // WMS TIME: the map around part 1 draws it only while it is in force.
        var (minX, minY, maxX, maxY) = XyzTileGrid.TileWorldBounds(zoom, x, y);
        string box = string.Join(',', new[] { minX, minY, maxX, maxY }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        async Task<int> DrawnPixelsAsync(string? time)
        {
            var query = $"wms?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=&CRS=EPSG:3857&BBOX={box}&WIDTH=256&HEIGHT=256&FORMAT=image/png&TRANSPARENT=TRUE"
                + (time is null ? string.Empty : "&TIME=" + time);
            var response = await http.GetAsync(new Uri(server.BaseUri, query), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync(ct));
            return bitmap.Pixels.Count(p => p.Alpha != 0);
        }

        Assert.True(await DrawnPixelsAsync(null) > 0);
        Assert.True(await DrawnPixelsAsync("2026-10-15T12:00:00Z") > 0);
        Assert.Equal(0, await DrawnPixelsAsync("2026-10-27T12:00:00Z"));
    }

    [Fact]
    public void Disk_cached_untimed_variants_are_kept_apart()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiles-s124-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = new DiskTileCache(root, "0123456789abcdef0123456789abcdef", TileImageFormat.Png, 1 << 20);
            cache.Add(new TileKey("day", 3, 1, 1), [1]);
            cache.Add(new TileKey("day", 3, 1, 1, null, "0011223344556677"), [2]);

            Assert.True(cache.TryGet(new TileKey("day", 3, 1, 1), out var plain));
            Assert.True(cache.TryGet(new TileKey("day", 3, 1, 1, null, "0011223344556677"), out var variant));
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
        }
    }
}
