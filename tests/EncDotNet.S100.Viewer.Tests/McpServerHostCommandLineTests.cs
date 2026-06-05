using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// CLI-driven MCP behaviour for <see cref="McpServerHost"/>: an
/// automation run must not persist the bound port back to the user's
/// settings, and should publish the endpoint to the configured
/// <c>--mcp-port-file</c>.
/// </summary>
public class McpServerHostCommandLineTests
{
    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public ImmutableArray<LoadedDataset> Datasets => ImmutableArray<LoadedDataset>.Empty;
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private static string TempDir() => Path.Combine(
        Path.GetTempPath(), "EncDotNet.S100.Viewer.Tests", $"mcp-cli-{Guid.NewGuid():N}");

    [Fact]
    public async Task Cli_configured_run_does_not_persist_port()
    {
        var dir = TempDir();
        var settingsPath = Path.Combine(dir, "settings.json");
        var settings = new ViewerSettings
        {
            SettingsFilePath = settingsPath,
            McpEnabled = true,
            McpPort = 0,
            McpConfiguredFromCommandLine = true,
        };

        await using var host = new McpServerHost(new EmptyCatalog(), settings);
        await host.Apply();

        Assert.NotNull(host.Server);
        Assert.True(host.Server!.IsRunning);

        // The bound port must NOT have been written back to settings…
        Assert.Equal(0, settings.McpPort);
        // …and nothing should have been persisted to disk.
        Assert.False(File.Exists(settingsPath));
    }

    [Fact]
    public async Task Port_file_receives_endpoint_uri()
    {
        var dir = TempDir();
        var portFile = Path.Combine(dir, "endpoint.txt");
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(dir, "settings.json"),
            McpEnabled = true,
            McpPort = 0,
            McpConfiguredFromCommandLine = true,
            McpPortFilePath = portFile,
        };

        await using var host = new McpServerHost(new EmptyCatalog(), settings);
        await host.Apply();

        Assert.True(File.Exists(portFile));
        var contents = File.ReadAllText(portFile).Trim();
        Assert.StartsWith("http://", contents);
        Assert.Contains(host.Server!.Port!.Value.ToString(), contents);
    }
}
