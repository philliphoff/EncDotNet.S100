using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the machine-readable start-up line <c>tiles serve --ready-json</c>
/// prints (issue #1496): it is one line of JSON, and every URL in it answers.
/// </summary>
public sealed class ServeReadyLineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ready-" + Guid.NewGuid().ToString("N"));

    public ServeReadyLineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ITileSource TileSet()
    {
        var path = Path.Combine(_root, "set");
        using (var sink = new XyzDirectoryTileSink(path, TileImageFormat.Png))
        {
            sink.Write(1, 0, 0, [1, 2, 3]);
            sink.Complete(new TileSetMetadata
            {
                Name = "Ready",
                Description = "Tiles for the ready line",
                Format = TileImageFormat.Png,
                MinZoom = 1,
                MaxZoom = 1,
                Bounds = (-10, -5, 10, 5),
                TilePixelSize = 256,
                Settings = new Dictionary<string, string>(),
            });
        }

        return TileSource.Open(path);
    }

    [Theory]
    [InlineData("secret", true)]
    [InlineData(null, false)]
    public async Task The_line_parses_and_its_urls_answer(string? token, bool viewer)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = TileSet();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token, viewer, ct);

        var line = ServeReadyLine.Format(IPAddress.Loopback, server.Port, token, source.Format, viewer);

        Assert.DoesNotContain('\n', line);
        var json = JsonNode.Parse(line)!.AsObject();
        var prefix = token is null ? string.Empty : token + "/";
        var baseUrl = $"http://127.0.0.1:{server.Port}/{prefix}";
        Assert.Equal("ready", (string?)json["event"]);
        Assert.Equal("127.0.0.1", (string?)json["host"]);
        Assert.Equal(server.Port, (int?)json["port"]);
        Assert.Equal(token, (string?)json["token"]);
        Assert.Equal(baseUrl, (string?)json["baseUrl"]);
        Assert.Equal(baseUrl + "{z}/{x}/{y}.png", (string?)json["tiles"]);
        Assert.Equal(baseUrl + "tiles.json", (string?)json["tileJson"]);
        Assert.Equal(baseUrl + "wmts/1.0.0/WMTSCapabilities.xml", (string?)json["wmts"]);
        Assert.Equal(baseUrl + "wms", (string?)json["wms"]);
        Assert.Equal($"http://127.0.0.1:{server.Port}/healthz", (string?)json["healthz"]);
        Assert.Equal(viewer ? baseUrl : null, (string?)json["preview"]);

        using var http = new HttpClient();
        string?[] urls =
        [
            ((string)json["tiles"]!).Replace("{z}/{x}/{y}", "1/0/0", StringComparison.Ordinal),
            (string?)json["tileJson"],
            (string?)json["wmts"],
            (string?)json["wms"] + "?SERVICE=WMS&REQUEST=GetCapabilities",
            (string?)json["healthz"],
            (string?)json["preview"],
        ];
        foreach (var url in urls.OfType<string>())
        {
            using var response = await http.GetAsync(url, ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} answered {(int)response.StatusCode}");
        }
    }

    [Fact]
    public void Listening_on_every_address_gives_loopback_urls()
    {
        var json = JsonNode.Parse(ServeReadyLine.Format(IPAddress.Any, 8200, "t0k", TileImageFormat.Webp, viewer: true))!;

        Assert.Equal("127.0.0.1", (string?)json["host"]);
        Assert.Equal("http://127.0.0.1:8200/t0k/{z}/{x}/{y}.webp", (string?)json["tiles"]);
        Assert.Equal("http://127.0.0.1:8200/healthz", (string?)json["healthz"]);
    }

    [Fact]
    public void Listening_on_every_ipv6_address_gives_ipv6_loopback_urls()
    {
        var json = JsonNode.Parse(ServeReadyLine.Format(IPAddress.IPv6Any, 8200, token: null, TileImageFormat.Png, viewer: false))!;

        Assert.Equal("::1", (string?)json["host"]);
        Assert.Equal("http://[::1]:8200/wms", (string?)json["wms"]);
        Assert.Equal(JsonValueKind.Null, json["preview"]?.GetValueKind() ?? JsonValueKind.Null);
    }

    [Fact]
    public void The_option_is_off_by_default()
    {
        Assert.False(new TilesServeCommand.Settings().ReadyJson);
    }
}
