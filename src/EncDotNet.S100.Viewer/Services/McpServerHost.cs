using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.McpTools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Owns the viewer-embedded <see cref="S100McpServer"/> lifecycle.
/// Constructs the server with the viewer's <see cref="ViewerDatasetCatalog"/>,
/// starts and stops it in response to <see cref="ViewerSettings.McpEnabled"/>
/// changes, and re-binds when the configured port changes.
/// </summary>
/// <remarks>
/// The server is created lazily on the first call to <see cref="Apply"/>
/// so that disabled installations pay no transport-stack cost. State
/// transitions are serialised through a single asynchronous lock so
/// rapid settings toggles do not produce overlapping Start/Stop calls.
/// </remarks>
internal sealed class McpServerHost : IAsyncDisposable
{
    private readonly EncDotNet.S100.Mcp.Tools.Catalog.IDatasetCatalog _catalog;
    private readonly ViewerSettings _settings;
    private readonly IMapHostAccessor? _mapHostAccessor;
    private readonly ILoggerFactory? _loggers;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private S100McpServer? _server;
    private bool _disposed;

    public McpServerHost(
        EncDotNet.S100.Mcp.Tools.Catalog.IDatasetCatalog catalog,
        ViewerSettings settings,
        IMapHostAccessor? mapHostAccessor = null,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        _catalog = catalog;
        _settings = settings;
        _mapHostAccessor = mapHostAccessor;
        _loggers = loggers;
    }

    /// <summary>
    /// The active server, or <c>null</c> when MCP is disabled. Exposed
    /// so the status-bar indicator can subscribe to lifecycle events
    /// without coupling the view-model to the server type directly.
    /// </summary>
    public S100McpServer? Server => _server;

    /// <summary>
    /// Raised whenever <see cref="Server"/> changes (created, replaced,
    /// or torn down). Status-bar subscribers should re-attach to the
    /// new server's events after handling this signal.
    /// </summary>
    public event EventHandler<EventArgs>? ServerChanged;

    /// <summary>
    /// Raised when an attempt to bind the MCP server failed because the
    /// configured <see cref="ViewerSettings.McpPort"/> was already in
    /// use by another process. The event argument carries the port the
    /// bind was attempted on. Subscribers (e.g. the main view-model)
    /// typically surface a sticky toast offering to re-allocate.
    /// </summary>
    public event EventHandler<McpPortConflictEventArgs>? McpPortConflict;

    /// <summary>
    /// Reconciles the running server with the current
    /// <see cref="ViewerSettings"/>. Starts, stops, or rebuilds the
    /// server as needed. Safe to call from any thread.
    /// </summary>
    public async Task Apply(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyCore(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyCore(CancellationToken cancellationToken)
    {
        var enabled = _settings.McpEnabled;
        var port = _settings.McpPort < 0 ? 0 : _settings.McpPort;
        var bindAddress = ParseBindAddress(_settings.McpBindAddress);

        if (!enabled)
        {
            await StopCurrentAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_server is { IsRunning: true } running)
        {
            var matches = running.Endpoint is { } ep
                && Equals(ep.Port, port == 0 ? ep.Port : port)
                && Equals(bindAddress, IPAddress.Parse(ep.Host.Trim('[', ']')));
            if (matches) return;
            await StopCurrentAsync(cancellationToken).ConfigureAwait(false);
        }

        var additionalTools = BuildAdditionalTools();
        var options = new S100McpServerOptions
        {
            BindAddress = bindAddress,
            Port = port,
            AdditionalTools = additionalTools,
        };
        var next = new S100McpServer(_catalog, options, _loggers);
        try
        {
            await next.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPortInUse(ex))
        {
            await next.DisposeAsync().ConfigureAwait(false);
            // Bind failed because the requested port is taken by another
            // process. Leave the server torn down and notify subscribers
            // so the UI can offer a recovery action. We never silently
            // fall back to an ephemeral port — the user explicitly
            // persisted this port (or it was persisted previously) and
            // must opt in to changing it.
            McpPortConflict?.Invoke(this, new McpPortConflictEventArgs(port));
            return;
        }
        catch
        {
            await next.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _server = next;

        // Persist the bound port so subsequent launches reuse it. When
        // the user asked for an ephemeral port (McpPort == 0) Kestrel
        // selected a concrete port for us; writing it back makes the
        // assignment "sticky". This trade-off does silently convert a
        // user who set McpPort == 0 ("pick any port each time") to a
        // persisted port, but ephemeral has no advantage for MCP
        // tooling and the user can clear it via the "Reset to auto"
        // button in Settings.
        //
        // Exception: when MCP was configured from the command line for
        // this run we never write the port back — an automation run
        // must not mutate the user's persisted profile.
        if (!_settings.McpConfiguredFromCommandLine
            && next.Port is { } boundPort && boundPort != _settings.McpPort)
        {
            _settings.McpPort = boundPort;
            TrySaveSettings();
        }

        PublishEndpoint(next);

        ServerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-allocates the MCP server's port by clearing
    /// <see cref="ViewerSettings.McpPort"/> back to 0 (ephemeral) and
    /// re-running <see cref="Apply"/>. The newly-bound port is
    /// persisted to settings as part of the normal apply flow.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// The port the server is now listening on, or <see langword="null"/>
    /// if the re-bind itself failed (e.g. an ephemeral assignment hit
    /// a transient conflict).
    /// </returns>
    public async Task<int?> ResetPortAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return null;

        _settings.McpPort = 0;
        TrySaveSettings();

        await Apply(cancellationToken).ConfigureAwait(false);
        return _server?.Port;
    }

    private void TrySaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch
        {
            // Settings persistence is best-effort; a failure here must
            // not take down the MCP server. The next successful save
            // (e.g. via the Settings UI) will re-persist the port.
        }
    }

    /// <summary>
    /// Makes the bound endpoint discoverable by external agents:
    /// writes the URI to <see cref="ViewerSettings.McpPortFilePath"/>
    /// when configured (so an ephemeral port can be read from a file)
    /// and echoes it to standard output. Both are best-effort.
    /// </summary>
    private void PublishEndpoint(S100McpServer server)
    {
        if (server.Endpoint is not { } endpoint)
            return;

        if (_settings.McpPortFilePath is { } path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, endpoint.ToString());
            }
            catch
            {
                // Best-effort — failure to publish must not stop the server.
            }
        }

        try
        {
            Console.Out.WriteLine($"[MCP] listening on {endpoint}");
        }
        catch
        {
            // Console may be redirected/closed; ignore.
        }
    }

    /// <summary>
    /// Detects the various ways Kestrel surfaces an "address in use"
    /// error. Kestrel typically wraps the underlying
    /// <see cref="SocketException"/> (errno <c>EADDRINUSE</c> = 48 on
    /// macOS / 98 on Linux / 10048 on Windows) in an
    /// <see cref="IOException"/>. We walk the inner-exception chain
    /// and match on the platform-portable
    /// <see cref="SocketError.AddressAlreadyInUse"/> as well as the
    /// .NET 10 <c>AddressInUseException</c> wrapper.
    /// </summary>
    private System.Collections.Generic.IReadOnlyList<McpServerTool>? BuildAdditionalTools()
    {
        if (_mapHostAccessor is null) return null;
        var renderTool = new RenderToImageTool(_mapHostAccessor);
        return new[] { RenderToImageMcpAdapter.Create(renderTool) };
    }

    private static bool IsPortInUse(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is SocketException sx && sx.SocketErrorCode == SocketError.AddressAlreadyInUse)
                return true;
            if (e.GetType().Name == "AddressInUseException")
                return true;
            if (e is IOException io
                && io.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e.InnerException is null) break;
        }
        return false;
    }

    private static IPAddress ParseBindAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return IPAddress.Loopback;
        return IPAddress.TryParse(raw, out var parsed) ? parsed : IPAddress.Loopback;
    }

    private async Task StopCurrentAsync(CancellationToken cancellationToken)
    {
        var prev = _server;
        if (prev is null) return;

        _server = null;
        try
        {
            await prev.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await prev.DisposeAsync().ConfigureAwait(false);
            ServerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCurrentAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            // best-effort during shutdown
        }
        _gate.Dispose();
    }
}

/// <summary>
/// Event payload for <see cref="McpServerHost.McpPortConflict"/>.
/// Carries the port the bind failed on so the UI can mention it in
/// the resulting error toast.
/// </summary>
internal sealed class McpPortConflictEventArgs : EventArgs
{
    public McpPortConflictEventArgs(int port)
    {
        Port = port;
    }

    /// <summary>The port that was already in use.</summary>
    public int Port { get; }
}
