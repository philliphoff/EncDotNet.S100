using EncDotNet.S100.Cli.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 mcp serve</c>. The integration test spawns the built
/// <c>s100</c> host exactly as an MCP client would — over stdio, via the
/// official <see cref="StdioClientTransport"/> — and drives a real tools/list
/// plus a tool call against a committed fixture, so it exercises argument
/// parsing, the headless <c>FileDatasetCatalog</c> build, and the stdio host
/// end to end. A fast in-process test covers the command's argument validation
/// without starting a server.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class McpServeCommandTests
{
    private static readonly string[] ExpectedTools =
    [
        "add_library_source",
        "await_library_idle",
        "close_all_datasets",
        "close_dataset",
        "count_features",
        "describe_feature",
        "describe_feature_type",
        "describe_library_item",
        "find_at",
        "identify_features",
        "library_action",
        "list_datasets",
        "list_known_sources",
        "list_library_sources",
        "list_secom_services",
        "list_specs",
        "list_time_steps",
        "nearest_features",
        "open_dataset",
        "query_features",
        "query_library_items",
        "refresh_library_source",
        "remove_library_source",
        "render_to_image",
        "sample_coverage",
        "sample_coverage_along",
        "search_features",
        "set_display_category",
        "set_display_mode",
        "set_library_source_options",
        "set_palette",
        "set_secom_identity",
        "set_time_step",
        "set_viewport",
    ];

    private static string HostPath() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "s100.exe" : "s100");

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public async Task Serve_over_stdio_lists_tools_and_serves_the_dataset()
    {
        var host = HostPath();
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "s100-mcp-serve",
            Command = host,
            Arguments = ["mcp", "serve", dataset],
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

        var toolNames = (await client.ListToolsAsync(cancellationToken: cts.Token))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedTools, toolNames);

        var result = await client.CallToolAsync(
            "list_datasets",
            new Dictionary<string, object?> { ["page"] = 0, ["pageSize"] = 50 },
            cancellationToken: cts.Token);

        Assert.False(result.IsError ?? false, "list_datasets returned an error.");
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
        // FileDatasetCatalog derives a positional dataset's id from its file name.
        Assert.Contains("navwarn_surface.gml", text);
    }

    [Fact]
    public async Task Serve_is_mutable_by_default_setting_palette_and_rendering()
    {
        var host = HostPath();
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "s100-mcp-serve-mutable",
            Command = host,
            Arguments = ["mcp", "serve", dataset],
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

        // A mutating tool is advertised without any flag — mutable by default.
        var toolNames = (await client.ListToolsAsync(cancellationToken: cts.Token))
            .Select(t => t.Name)
            .ToArray();
        Assert.Contains("set_palette", toolNames);
        Assert.Contains("render_to_image", toolNames);

        // set_palette mutates session state and echoes the previous value.
        var palette = await client.CallToolAsync(
            "set_palette",
            new Dictionary<string, object?> { ["palette"] = "Night" },
            cancellationToken: cts.Token);
        Assert.False(palette.IsError ?? false, "set_palette returned an error.");
        var paletteText = palette.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        using (var doc = System.Text.Json.JsonDocument.Parse(paletteText))
        {
            Assert.Equal("Night", doc.RootElement.GetProperty("palette").GetString());
            Assert.Equal("Day", doc.RootElement.GetProperty("previous").GetString());
        }

        // render_to_image returns a PNG image block backed by the Skia pipeline.
        var render = await client.CallToolAsync(
            "render_to_image",
            new Dictionary<string, object?> { ["width"] = 256, ["height"] = 256 },
            cancellationToken: cts.Token);
        Assert.False(render.IsError ?? false, "render_to_image returned an error.");
        var image = render.Content.OfType<ImageContentBlock>().FirstOrDefault();
        Assert.NotNull(image);
        Assert.Equal("image/png", image!.MimeType);
        Assert.False(image.Data.IsEmpty, "render_to_image produced no image bytes.");
    }

    [Fact]
    public async Task Serve_opens_and_closes_a_dataset_mid_session()
    {
        var host = HostPath();
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "s100-mcp-serve-openclose",
            Command = host,
            Arguments = ["mcp", "serve", dataset],
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

        // Load the fixture a second time; the catalog assigns a unique id by
        // suffixing (e.g. "…#2") since the base id is already in use.
        var open = await client.CallToolAsync(
            "open_dataset",
            new Dictionary<string, object?> { ["path"] = dataset },
            cancellationToken: cts.Token);
        Assert.False(open.IsError ?? false, "open_dataset returned an error.");

        var openText = open.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        string openedId;
        using (var doc = System.Text.Json.JsonDocument.Parse(openText))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
            openedId = doc.RootElement.GetProperty("datasets")[0].GetProperty("id").GetString()!;
        }
        Assert.NotNull(openedId);

        // Both the upfront and the newly opened dataset are now present.
        var list = await client.CallToolAsync(
            "list_datasets",
            new Dictionary<string, object?> { ["page"] = 0, ["pageSize"] = 50 },
            cancellationToken: cts.Token);
        var listText = list.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
        Assert.Contains(openedId, listText);

        // Close just the dataset we opened.
        var close = await client.CallToolAsync(
            "close_dataset",
            new Dictionary<string, object?> { ["id"] = openedId },
            cancellationToken: cts.Token);
        Assert.False(close.IsError ?? false, "close_dataset returned an error.");
        var closeText = close.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        using (var doc = System.Text.Json.JsonDocument.Parse(closeText))
        {
            Assert.True(doc.RootElement.GetProperty("removed").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        }
    }

    [Fact]
    public void Serve_rejects_only_without_an_exchange_set_or_folder()
    {
        // Purely a settings-validation path — the server is never started.
        var exit = CliApp.Build().Run(["mcp", "serve", "--layer", "a.gml", "--only", "S101"]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Serve_adds_startup_collections_once_to_a_kept_library()
    {
        var host = HostPath();
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        var root = Path.Combine(Path.GetTempPath(), $"s100-serve-library-{Guid.NewGuid():N}");
        var charts = Path.Combine(root, "warnings");
        Directory.CreateDirectory(charts);
        File.Copy(dataset, Path.Combine(charts, "navwarn_surface.gml"));
        try
        {
            // The same command line twice over one data directory: the folder is added once.
            for (var run = 0; run < 2; run++)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "s100-mcp-serve",
                    Command = host,
                    Arguments = ["mcp", "serve", "--collection", charts, "--data-dir", Path.Combine(root, "data")],
                });
                await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

                var result = await client.CallToolAsync(
                    "list_library_sources", new Dictionary<string, object?>(), cancellationToken: cts.Token);

                Assert.False(result.IsError ?? false, "list_library_sources returned an error.");
                var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var collection = Assert.Single(doc.RootElement.GetProperty("collections").EnumerateArray());
                Assert.Equal("warnings", collection.GetProperty("name").GetString());
            }

            Assert.True(File.Exists(Path.Combine(root, "data", "collections.json")));
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

    [Theory]
    [InlineData("https://example.test/catalog.xml", null, null, "https://example.test/catalog.xml", null)]
    [InlineData("noaa-s111#sfbofs, cbofs", "noaa-s111", null, null, "sfbofs|cbofs")]
    [InlineData("noaa-enc", "noaa-enc", null, null, null)]
    [InlineData("./charts#Belgium", null, "./charts", null, "Belgium")]
    public void A_collection_value_is_a_url_a_path_or_a_known_source(
        string value, string? known, string? path, string? url, string? choices)
    {
        var request = Commands.McpServeCommand.CollectionRequest(value);

        Assert.Equal(known, request.KnownSourceId);
        Assert.Equal(path, request.Path);
        Assert.Equal(url, request.Url);
        Assert.Equal(choices, request.Choices is null ? null : string.Join("|", request.Choices));
    }
}
