using System;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Sticky-port behaviour for <see cref="McpServerHost"/>:
/// auto-allocates-and-persists when <c>McpPort == 0</c>, surfaces a
/// conflict event when a persisted port is squatted, and rebinds
/// via <see cref="McpServerHost.ResetPortAsync"/> on user opt-in.
/// </summary>
public class McpServerHostStickyPortTests
{
    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public ImmutableArray<LoadedDataset> Datasets => ImmutableArray<LoadedDataset>.Empty;
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private static ViewerSettings NewSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            "EncDotNet.S100.Viewer.Tests",
            $"mcp-{Guid.NewGuid():N}",
            "settings.json");
        return new ViewerSettings
        {
            SettingsFilePath = tmp,
            McpEnabled = true,
            McpPort = 0,
        };
    }

    [Fact]
    public async Task Apply_with_port_zero_binds_and_persists_concrete_port()
    {
        var settings = NewSettings();
        await using var host = new McpServerHost(new EmptyCatalog(), settings);

        await host.Apply();

        Assert.NotNull(host.Server);
        Assert.True(host.Server!.IsRunning);
        Assert.NotNull(host.Server.Port);
        Assert.True(host.Server.Port > 0);

        // The bound port must have been written back to settings.
        Assert.Equal(host.Server.Port!.Value, settings.McpPort);

        // And persisted to disk.
        Assert.True(File.Exists(settings.SettingsFilePath));
        var reloaded = ViewerSettings.Load(settings.SettingsFilePath);
        Assert.Equal(host.Server.Port!.Value, reloaded.McpPort);
    }

    [Fact]
    public async Task Apply_raises_McpPortConflict_when_persisted_port_is_in_use()
    {
        // Reserve a free port, then squat on it.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var squatter = new TcpListener(IPAddress.Loopback, port);
        squatter.Start();
        try
        {
            var settings = NewSettings();
            settings.McpPort = port;

            await using var host = new McpServerHost(new EmptyCatalog(), settings);

            int? conflictPort = null;
            host.McpPortConflict += (_, e) => conflictPort = e.Port;

            await host.Apply();

            Assert.Equal(port, conflictPort);
            // Server must NOT come up — the user has to opt in to recovery.
            Assert.Null(host.Server);
            // Persisted port is unchanged — the user might want to
            // resolve the conflict externally and try again.
            Assert.Equal(port, settings.McpPort);
        }
        finally
        {
            squatter.Stop();
        }
    }

    [Fact]
    public async Task ResetPortAsync_rebinds_and_persists_new_port_after_conflict()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var squatter = new TcpListener(IPAddress.Loopback, port);
        squatter.Start();
        try
        {
            var settings = NewSettings();
            settings.McpPort = port;

            await using var host = new McpServerHost(new EmptyCatalog(), settings);
            await host.Apply(); // conflict, no server
            Assert.Null(host.Server);

            var newPort = await host.ResetPortAsync();

            Assert.NotNull(newPort);
            Assert.NotEqual(port, newPort);
            Assert.NotNull(host.Server);
            Assert.True(host.Server!.IsRunning);
            Assert.Equal(newPort, host.Server.Port);
            // Persisted to settings.
            Assert.Equal(newPort, settings.McpPort);
            var reloaded = ViewerSettings.Load(settings.SettingsFilePath);
            Assert.Equal(newPort, reloaded.McpPort);
        }
        finally
        {
            squatter.Stop();
        }
    }

    [Fact]
    public async Task Apply_with_already_running_matching_port_is_idempotent()
    {
        var settings = NewSettings();
        await using var host = new McpServerHost(new EmptyCatalog(), settings);

        await host.Apply();
        var firstPort = host.Server?.Port;
        var firstServer = host.Server;

        await host.Apply();

        Assert.Same(firstServer, host.Server);
        Assert.Equal(firstPort, host.Server?.Port);
    }
}
