using System.Net;
using System.Net.Sockets;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Feeds;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Infrastructure.Updates;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the container health check (issue #1464): the <c>/healthz</c>
/// route <c>tiles serve</c> and <c>feed serve</c> answer outside their token,
/// and the <c>s100 health</c> probe that asks for it.
/// </summary>
public sealed class HealthCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "health-" + Guid.NewGuid().ToString("N"));

    public HealthCheckTests() => Directory.CreateDirectory(_root);

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
                Name = "Health",
                Description = "Tiles for the health check",
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

    [Fact]
    public async Task Tiles_serve_answers_the_health_check_without_its_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(TileSet(), IPAddress.Loopback, 0, "secret", cancellationToken: ct);
        using var http = new HttpClient();

        using var health = await http.GetAsync($"http://127.0.0.1:{server.Port}/healthz", ct);
        using var root = await http.GetAsync($"http://127.0.0.1:{server.Port}/tiles.json", ct);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync(ct));
        Assert.Equal("no-store", health.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
    }

    [Fact]
    public async Task Feed_serve_answers_the_health_check_without_its_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Directory.CreateDirectory(Path.Combine(_root, "feed")).FullName;
        await using var server = await FeedServer.StartAsync(
            new FeedPublisher(folder, timeProvider: TimeProvider.System), IPAddress.Loopback, 0, "secret", cancellationToken: ct);
        using var http = new HttpClient();

        using var health = await http.GetAsync($"http://127.0.0.1:{server.Port}/healthz", ct);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task The_probe_is_healthy_while_a_server_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(TileSet(), IPAddress.Loopback, 0, token: null, cancellationToken: ct);

        var (healthy, message) = await HealthCommand.ProbeAsync(IPAddress.Loopback, server.Port, TimeSpan.FromSeconds(5), ct);

        Assert.True(healthy, message);
        Assert.Equal($"Healthy: http://127.0.0.1:{server.Port}/healthz", message);
    }

    [Fact]
    public async Task The_probe_is_unhealthy_when_nothing_listens()
    {
        var ct = TestContext.Current.CancellationToken;
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var (healthy, message) = await HealthCommand.ProbeAsync(IPAddress.Loopback, port, TimeSpan.FromSeconds(5), ct);

        Assert.False(healthy);
        Assert.Equal($"Unhealthy: nothing is listening at http://127.0.0.1:{port}/healthz.", message);
    }

    [Fact]
    public async Task The_probe_is_unhealthy_when_the_server_answers_with_an_error()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new HttpListener();
        int port;
        using (var free = new TcpListener(IPAddress.Loopback, 0))
        {
            free.Start();
            port = ((IPEndPoint)free.LocalEndpoint).Port;
        }

        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var answer = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 503;
            context.Response.Close();
        }, ct);

        var (healthy, message) = await HealthCommand.ProbeAsync(IPAddress.Loopback, port, TimeSpan.FromSeconds(5), ct);
        await answer;

        Assert.False(healthy);
        Assert.StartsWith($"Unhealthy: http://127.0.0.1:{port}/healthz answered 503", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" YES ", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_update_check_is_turned_off_by_its_environment_variable(string? value, bool disabled)
    {
        Assert.Equal(disabled, DisabledCliUpdateChecker.IsDisabledBy(value));
    }
}
