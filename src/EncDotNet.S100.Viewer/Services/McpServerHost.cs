using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp;
using Microsoft.Extensions.Logging;

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
    private readonly ViewerDatasetCatalog _catalog;
    private readonly ViewerSettings _settings;
    private readonly ILoggerFactory? _loggers;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private S100McpServer? _server;
    private bool _disposed;

    public McpServerHost(
        ViewerDatasetCatalog catalog,
        ViewerSettings settings,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        _catalog = catalog;
        _settings = settings;
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

        var options = new S100McpServerOptions
        {
            BindAddress = bindAddress,
            Port = port,
        };
        var next = new S100McpServer(_catalog, options, _loggers);
        try
        {
            await next.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await next.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _server = next;
        ServerChanged?.Invoke(this, EventArgs.Empty);
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
