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
        "count_features",
        "describe_feature",
        "describe_feature_type",
        "find_at",
        "identify_features",
        "list_datasets",
        "list_specs",
        "list_time_steps",
        "nearest_features",
        "query_features",
        "sample_coverage",
        "sample_coverage_along",
        "search_features",
    ];

    private static string HostPath() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "s100.exe" : "s100");

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [SkippableFact]
    public async Task Serve_over_stdio_lists_tools_and_serves_the_dataset()
    {
        var host = HostPath();
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(host), $"CLI host not found: {host}");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

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
    public void Serve_rejects_layer_and_from_together()
    {
        // Purely a settings-validation path — the server is never started.
        var exit = CliApp.Build().Run(["mcp", "serve", "--layer", "a.gml", "--from", "b.zip"]);

        Assert.NotEqual(0, exit);
    }
}
