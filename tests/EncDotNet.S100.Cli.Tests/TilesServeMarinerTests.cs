using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for mariner settings in <c>tiles serve</c> and <c>tiles export</c>
/// (issue #1493): settings sets registered under <c>settings/{hash}</c> and
/// asked for as <c>{palette}-{hash}</c> styles, and the same settings as
/// start-up options.
/// </summary>
public sealed class TilesServeMarinerTests
{
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";
    private static readonly XNamespace Wms = "http://www.opengis.net/wms";

    // A synthetic S-101 test dataset with depth areas, contours and soundings.
    private static string S101 => Path.Combine(AppContext.BaseDirectory, "TestData", "S101", "101AA00DS0003.000");

    [Fact]
    public void Settings_are_read_from_json_with_the_viewer_names()
    {
        Assert.True(MarinerStyle.TryParse(
            """
            {
              "displayCategory": "standard", "SafetyContour": 10, "safetyDepth": 12.5, "shallowContour": 2,
              "deepContour": 20, "fourShades": true, "soundings": false, "shallowWaterDangers": false,
              "plainBoundaries": false, "simplifiedSymbols": true, "fullLightLines": true,
              "ignoreScaleMinimum": true, "depthUnit": "feet"
            }
            """, out var style, out var error), error);

        Assert.Equal(EcdisDisplayCategory.Standard, style.DisplayCategory);
        Assert.Equal(10, style.SafetyContour);
        Assert.Equal(12.5, style.SafetyDepth);
        Assert.Equal(DepthUnit.Feet, style.DepthUnit);
        Assert.False(style.Soundings);

        var mariner = style.ToMarinerSettings(nationalLanguage: "fra");
        Assert.Equal(10, mariner.SafetyContour.TotalMetres);
        Assert.True(mariner.FourShades);
        Assert.False(mariner.PlainBoundaries);
        Assert.True(mariner.IgnoreScaleMinimum);
        Assert.Equal("fra", mariner.NationalLanguage);

        var display = style.ToEcdisDisplay();
        Assert.NotNull(display);
        Assert.Equal(EcdisDisplayCategory.Standard, display.Category);
        Assert.Contains(MarinerStyle.SoundingsViewingGroup, display.HiddenViewingGroups["S-101"]);

        // The JSON written reads back as the same settings.
        Assert.True(MarinerStyle.TryParse(style.ToJson().ToJsonString(), out var again, out _));
        Assert.Equal(style, again);
    }

    [Theory]
    [InlineData("[]", "JSON object")]
    [InlineData("not json", "not JSON")]
    [InlineData("""{ "safetyContuor": 10 }""", "Unknown setting 'safetyContuor'")]
    [InlineData("""{ "safetyContour": "10" }""", "'safetyContour' must be a depth")]
    [InlineData("""{ "safetyContour": -1 }""", "'safetyContour' must be a depth")]
    [InlineData("""{ "fourShades": 1 }""", "'fourShades' must be true or false")]
    [InlineData("""{ "displayCategory": "Everything" }""", "'displayCategory' must be one of")]
    [InlineData("""{ "displayCategory": "2" }""", "'displayCategory' must be one of")]
    [InlineData("""{ "depthUnit": "cubits" }""", "'depthUnit' must be one of")]
    public void Bad_settings_are_refused_with_a_reason(string json, string expected)
    {
        Assert.False(MarinerStyle.TryParse(json, out _, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void No_settings_change_nothing()
    {
        Assert.True(MarinerStyle.TryParse("{}", out var style, out _));
        Assert.True(style.IsEmpty);
        Assert.Equal(string.Empty, style.Key);
        Assert.Null(style.ToEcdisDisplay());
        Assert.Equal(MarinerSettings.Default, style.ToMarinerSettings(null));
        Assert.True(new TilesServeCommand.Settings { Input = S101 }.MarinerStyle.IsEmpty);
    }

    [Fact]
    public void The_key_follows_the_values()
    {
        var a = new MarinerStyle { SafetyContour = 10 };
        Assert.Equal(a.Key, new MarinerStyle { SafetyContour = 10 }.Key);
        Assert.NotEqual(a.Key, new MarinerStyle { SafetyContour = 11 }.Key);
        Assert.Matches("^[0-9a-f]{16}$", a.Key);

        // Values left out fall back.
        var startup = new MarinerStyle { SafetyContour = 5, FourShades = true };
        Assert.Equal(new MarinerStyle { SafetyContour = 10, FourShades = true }, a.Over(startup));
    }

    [Fact]
    public void A_hash_is_registered_once()
    {
        var registry = new MarinerStyleRegistry();
        var style = new MarinerStyle { SafetyContour = 10 };

        Assert.Equal(MarinerStyleRegistry.Outcome.Created, registry.Register("Abc1", style));
        Assert.Equal(MarinerStyleRegistry.Outcome.Unchanged, registry.Register("abc1", style with { }));
        Assert.Equal(MarinerStyleRegistry.Outcome.Conflict, registry.Register("abc1", new MarinerStyle { SafetyContour = 20 }));
        Assert.True(registry.TryGet("ABC1", out var found));
        Assert.Equal(style, found);
        Assert.Equal(["abc1"], registry.Hashes);
        Assert.Equal(["day-abc1", "night-abc1"], registry.Styles(["day", "night"]));

        Assert.False(MarinerStyleRegistry.IsValidHash("a-b"));
        Assert.False(MarinerStyleRegistry.IsValidHash(""));
        Assert.False(MarinerStyleRegistry.IsValidHash(new string('a', MarinerStyleRegistry.MaxHashLength + 1)));
        Assert.True(MarinerStyleRegistry.IsValidHash("render_key_0123"));
    }

    [Fact]
    public void The_registry_holds_a_bounded_number_of_sets()
    {
        var registry = new MarinerStyleRegistry();
        for (int i = 0; i < MarinerStyleRegistry.Capacity; i++)
            Assert.Equal(MarinerStyleRegistry.Outcome.Created, registry.Register("h" + i, new MarinerStyle { SafetyContour = i }));

        Assert.Equal(MarinerStyleRegistry.Outcome.Full, registry.Register("more", MarinerStyle.Empty));
    }

    [Theory]
    [InlineData("--display-category", "everything", "--display-category")]
    [InlineData("--depth-unit", "cubits", "--depth-unit")]
    [InlineData("--safety-contour", "-5", "--safety-contour")]
    [InlineData("--deep-contour", "NaN", "--deep-contour")]
    public void Bad_start_up_settings_are_refused(string option, string value, string expected)
    {
        var settings = option switch
        {
            "--display-category" => new TilesServeCommand.Settings { Input = S101, DisplayCategory = value },
            "--depth-unit" => new TilesServeCommand.Settings { Input = S101, DepthUnit = value },
            "--safety-contour" => new TilesServeCommand.Settings { Input = S101, SafetyContour = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
            _ => new TilesServeCommand.Settings { Input = S101, DeepContour = double.NaN },
        };

        Assert.SkipUnless(File.Exists(S101), "S-101 fixture not present.");
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public void Start_up_options_set_the_settings_and_change_the_fingerprint()
    {
        Assert.SkipUnless(File.Exists(S101), "S-101 fixture not present.");
        var plain = new TilesServeCommand.Settings { Input = S101 };
        var set = new TilesServeCommand.Settings
        {
            Input = S101,
            DisplayCategory = "display-base",
            SafetyContour = 10,
            FourShades = true,
            NoSoundings = true,
            SymbolizedBoundaries = true,
            DepthUnit = "fathoms-feet",
        };
        Assert.True(set.Validate().Successful);

        Assert.Equal(
            new MarinerStyle
            {
                DisplayCategory = EcdisDisplayCategory.DisplayBase,
                SafetyContour = 10,
                FourShades = true,
                Soundings = false,
                PlainBoundaries = false,
                DepthUnit = DepthUnit.FathomsFeet,
            },
            set.MarinerStyle);
        Assert.NotEqual(TileRenderSession.Fingerprint(plain), TileRenderSession.Fingerprint(set));
    }

    private static RenderedTileSource OpenRendered(TilesServeCommand.Settings? settings = null)
    {
        settings ??= new TilesServeCommand.Settings { Input = S101, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    /// <summary>Every tile at the highest zoom (the lower draws nothing) in <paramref name="style"/>, in a fixed order.</summary>
    private static async Task<List<byte[]?>> TilesAsync(RenderedTileSource source, string? style)
    {
        int zoom = source.Layout.MaxZoom;
        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
        var tiles = new List<byte[]?>();
        foreach (int x in XyzTileGrid.Columns(zoom, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
                tiles.Add(await source.ReadAsync(zoom, x, y, style, null, TestContext.Current.CancellationToken));
        }

        return tiles;
    }

    private static bool Same(List<byte[]?> a, List<byte[]?> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First is null ? p.Second is null : p.Second is not null && p.First.AsSpan().SequenceEqual(p.Second));

    [Fact]
    public async Task Two_settings_sets_give_different_tiles_for_the_same_request()
    {
        Assert.SkipUnless(File.Exists(S101), "S-101 fixture not present.");
        using var source = OpenRendered();
        source.MarinerStyles.Register("base", new MarinerStyle { DisplayCategory = EcdisDisplayCategory.DisplayBase });
        source.MarinerStyles.Register("deep", new MarinerStyle { SafetyContour = 0, SafetyDepth = 0 });
        source.MarinerStyles.Register("none", MarinerStyle.Empty);

        var plain = await TilesAsync(source, null);
        Assert.Contains(plain, t => t is not null);

        var displayBase = await TilesAsync(source, "day-base");
        var deep = await TilesAsync(source, "day-deep");
        Assert.False(Same(plain, displayBase));
        Assert.False(Same(plain, deep));
        Assert.False(Same(displayBase, deep));

        // An empty set renders as the server's own settings: today's tiles.
        Assert.True(Same(plain, await TilesAsync(source, "day-none")));

        // A set in another palette is another style.
        Assert.False(Same(displayBase, await TilesAsync(source, "night-base")));
        Assert.Contains("night-base", source.Palettes);

        // An unknown hash or palette has no tiles.
        Assert.All(await TilesAsync(source, "day-unknown"), Assert.Null);
        Assert.All(await TilesAsync(source, "sepia-base"), Assert.Null);
    }

    [Fact]
    public async Task Start_up_settings_render_as_the_same_registered_set()
    {
        Assert.SkipUnless(File.Exists(S101), "S-101 fixture not present.");
        using var startup = OpenRendered(new TilesServeCommand.Settings { Input = S101, Parallel = 2, DisplayCategory = "display-base" });
        using var registered = OpenRendered();
        registered.MarinerStyles.Register("base", new MarinerStyle { DisplayCategory = EcdisDisplayCategory.DisplayBase });

        Assert.True(Same(await TilesAsync(startup, null), await TilesAsync(registered, "day-base")));
        Assert.Equal("DisplayBase", (string?)startup.ToTileJson(null)["s100"]!["displayCategory"]);
    }

    [Fact]
    public async Task Settings_sets_are_registered_over_http_and_served_as_styles()
    {
        Assert.SkipUnless(File.Exists(S101), "S-101 fixture not present.");
        using var source = OpenRendered();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;
        var body = """{ "displayCategory": "DisplayBase", "safetyContour": 10 }""";

        using (var created = await http.PutAsync(new Uri(server.BaseUri, "settings/k1"), new StringContent(body, Encoding.UTF8, "application/json"), ct))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var json = JsonNode.Parse(await created.Content.ReadAsStringAsync(ct))!;
            Assert.Equal("k1", (string?)json["hash"]);
            Assert.Equal(["day-k1", "dusk-k1", "night-k1"], json["styles"]!.AsArray().Select(s => (string)s!));
            Assert.Equal(10, (double)json["settings"]!["safetyContour"]!);
        }

        Assert.Equal(HttpStatusCode.OK, (await http.PutAsync(new Uri(server.BaseUri, "settings/k1"), new StringContent(body), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsync(new Uri(server.BaseUri, "settings/k1"), new StringContent("{}"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsync(new Uri(server.BaseUri, "settings/k2"), new StringContent("""{ "safetyContour": "deep" }"""), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsync(new Uri(server.BaseUri, "settings/a.b"), new StringContent("{}"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(server.BaseUri, "settings/K1"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "settings/k2"), ct)).StatusCode);

        // The tiles, on the XYZ, WMTS and WMS routes.
        int zoom = source.Layout.MaxZoom;
        var tiles = await TilesAsync(source, "day-k1");
        var area = source.Layout.Area;
        var (firstRow, _) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
        int x = XyzTileGrid.Columns(zoom, area.MinX, area.MaxX)[0];
        var expected = await source.ReadAsync(zoom, x, firstRow, "day-k1", null, ct);
        Assert.NotNull(expected);
        Assert.Contains(tiles, t => t is not null);

        Assert.Equal(expected, await http.GetByteArrayAsync(new Uri(server.BaseUri, $"day-k1/{zoom}/{x}/{firstRow}.png"), ct));
        Assert.Equal(expected, await http.GetByteArrayAsync(new Uri(server.BaseUri, $"wmts/1.0.0/s100/day-k1/WebMercatorQuad/{zoom}/{firstRow}/{x}.png"), ct));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, $"day-k2/{zoom}/{x}/{firstRow}.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "day-k2/tiles.json"), ct)).StatusCode);

        using (var wmtsError = await http.GetAsync(new Uri(server.BaseUri, $"wmts/1.0.0/s100/day-k2/WebMercatorQuad/{zoom}/{firstRow}/{x}.png"), ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wmtsError.StatusCode);
            Assert.Contains("InvalidParameterValue", await wmtsError.Content.ReadAsStringAsync(ct));
        }

        var styled = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "day-k1/tiles.json"), ct))!;
        Assert.Equal("day-k1", (string?)styled["s100"]!["style"]);
        Assert.Equal("10", (string?)styled["s100"]!["safetyContour"]);

        // The capabilities list the registered styles.
        var wmts = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        var wmtsStyles = wmts.Descendants(Wmts + "Style").Select(s => s.Element(Ows + "Identifier")!.Value).ToList();
        Assert.Contains("day-k1", wmtsStyles);
        Assert.Contains("night-k1", wmtsStyles);
        Assert.DoesNotContain("day-k2", wmtsStyles);

        var wms = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmsService.Path + "?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        Assert.Contains("dusk-k1", wms.Descendants(Wms + "Style").Select(s => s.Element(Wms + "Name")!.Value));

        using var unknown = await http.GetAsync(new Uri(server.BaseUri,
            WmsService.Path + "?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=day-k2&CRS=EPSG:3857&BBOX=0,0,1000,1000&WIDTH=64&HEIGHT=64&FORMAT=image/png"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("StyleNotDefined", await unknown.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task A_built_tile_set_has_no_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiles-mariner-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var sink = new XyzDirectoryTileSink(root, TileImageFormat.Png))
            {
                sink.Write(1, 1, 0, Encoding.ASCII.GetBytes("tile"));
                sink.Complete(new TileSetMetadata
                {
                    Name = "Built",
                    Description = "Built set",
                    Format = TileImageFormat.Png,
                    MinZoom = 1,
                    MaxZoom = 1,
                    Bounds = (0, 0, 10, 10),
                    TilePixelSize = 256,
                    Settings = new Dictionary<string, string>(),
                });
            }

            await using var server = await TileServer.StartAsync(TileSource.Open(root), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var ct = TestContext.Current.CancellationToken;
            using var response = await http.PutAsJsonAsync(new Uri(server.BaseUri, "settings/k1"), new { safetyContour = 10 }, ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
