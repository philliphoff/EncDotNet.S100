using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using EncDotNet.S100.Cli.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 mcp serve --transport http</c> (#1464): the bearer token, the
/// origin check and the health check of <see cref="McpHttpServer"/> in
/// process, and the served tool sets of the real <c>s100</c> host, with and
/// without <c>--allow-changes</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class McpServeHttpTests
{
    /// <summary>The tools left out over HTTP unless --allow-changes is given.</summary>
    private static readonly string[] ChangeTools =
    [
        "add_library_source",
        "await_library_idle",
        "close_all_datasets",
        "close_dataset",
        "library_action",
        "open_dataset",
        "refresh_library_source",
        "remove_library_source",
        "set_library_source_options",
        "set_secom_identity",
    ];

    private static readonly McpServerTool[] PingTool =
        [McpServerTool.Create(() => "pong", new McpServerToolCreateOptions { Name = "ping" })];

    [Fact]
    public async Task A_token_is_needed_for_the_endpoint_but_not_the_health_check()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await McpHttpServer.StartAsync(PingTool, IPAddress.Loopback, 0, "s3cret", ct);
        using var http = new HttpClient();

        using (var health = await http.GetAsync(new Uri(server.Endpoint, "/healthz"), ct))
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(http, server.Endpoint, authorization: null, origin: null, ct));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(http, server.Endpoint, "Bearer wrong", origin: null, ct));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(http, server.Endpoint, "s3cret", origin: null, ct));

        await using var client = await ConnectAsync(server.Endpoint, "s3cret", ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        Assert.Equal("ping", Assert.Single(tools).Name);
    }

    [Fact]
    public async Task Without_a_token_any_client_connects()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await McpHttpServer.StartAsync(PingTool, IPAddress.Loopback, 0, token: null, ct);

        await using var client = await ConnectAsync(server.Endpoint, token: null, ct);
        Assert.Single(await client.ListToolsAsync(cancellationToken: ct));
    }

    [Fact]
    public async Task A_request_from_another_origin_is_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await McpHttpServer.StartAsync(PingTool, IPAddress.Loopback, 0, token: null, ct);
        using var http = new HttpClient();

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(http, server.Endpoint, null, "http://evil.example", ct));
        // The server's own origin passes the check (and then fails as a bad MCP request, not 403).
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(http, server.Endpoint, null, $"http://127.0.0.1:{server.Port}", ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_leaves_out_the_change_tools_unless_allowed(bool allowChanges)
    {
        var host = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "s100.exe" : "s100");
        var dataset = Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_surface.gml");
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        int port = FreePort();

        var start = new ProcessStartInfo(host)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "mcp", "serve", dataset, "--transport", "http", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--token", "t0ken" })
            start.ArgumentList.Add(argument);
        if (allowChanges)
            start.ArgumentList.Add("--allow-changes");

        using var process = Process.Start(start)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            var endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
            Assert.True(await WaitHealthyAsync(port, cts.Token), "The server never answered /healthz.");

            await using var client = await ConnectAsync(endpoint, "t0ken", cts.Token);
            var names = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();

            Assert.Contains("list_datasets", names);
            Assert.Contains("render_to_image", names);
            Assert.Contains("query_library_items", names);
            foreach (var tool in ChangeTools)
                Assert.Equal(allowChanges, names.Contains(tool));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("--host", "0.0.0.0")]
    [InlineData("--port", "9000")]
    [InlineData("--allow-changes", null)]
    [InlineData("--transport", "websocket")]
    public void Http_options_need_the_http_transport(string option, string? value)
    {
        string[] args = value is null ? ["mcp", "serve", option] : ["mcp", "serve", option, value];

        Assert.NotEqual(0, CliApp.Build().Run(args, TestContext.Current.CancellationToken));
    }

    private static async Task<McpClient> ConnectAsync(Uri endpoint, string? token, CancellationToken ct)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "s100-mcp-http-test",
        };
        if (token is not null)
            options.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token };

        return await McpClient.CreateAsync(new HttpClientTransport(options), cancellationToken: ct);
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient http, Uri endpoint, string? authorization, string? origin, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", MediaTypeHeaderValue.Parse("application/json")),
        };
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);

        using var response = await http.SendAsync(request, ct);
        return response.StatusCode;
    }

    private static async Task<bool> WaitHealthyAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/healthz"), ct);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250, ct);
        }

        return false;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
