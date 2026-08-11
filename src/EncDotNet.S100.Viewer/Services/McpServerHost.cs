using System.Net;
using System.Net.Sockets;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services.McpCapabilities;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Mutable = EncDotNet.S100.Mcp.Tools.Mutable;
using SharedMutableTools = EncDotNet.S100.Mcp.MutableTools.S100MutableTools;

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
    private readonly EncDotNet.S100.Datasets.Pipelines.Catalog.IDatasetCatalog _catalog;
    private readonly ViewerSettings _settings;
    private readonly IMapCapabilityAccessor<IMapSnapshotRenderer>? _snapshotAccessor;
    private readonly IMapCapabilityAccessor<IMapViewportController>? _viewportAccessor;
    private readonly IMapCapabilityAccessor<IMapCoordinateConverter>? _coordinateAccessor;
    private readonly IRenderStateControllerAccessor? _renderStateAccessor;
    private readonly MapPresentationStateProjection? _presentationProjection;
    private readonly GlobalTimeService? _globalTime;
    private readonly IRenderActivityMonitor? _renderActivityMonitor;
    private readonly IDatasetLoadGateway? _loadGateway;
    private readonly EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm? _ownShipHelm;
    private readonly RoutesService? _routesService;
    private readonly IGeographicPickPresenter? _pickPresenter;
    private readonly IViewerUiControllerAccessor? _uiControllerAccessor;
    private readonly IAppScreenshotProvider? _appScreenshot;
    private readonly ILoggerFactory? _loggers;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private S100McpServer? _server;
    private bool _disposed;

    public McpServerHost(
        EncDotNet.S100.Datasets.Pipelines.Catalog.IDatasetCatalog catalog,
        ViewerSettings settings,
        IMapCapabilityAccessor<IMapSnapshotRenderer>? snapshotAccessor = null,
        IMapCapabilityAccessor<IMapViewportController>? viewportAccessor = null,
        IMapCapabilityAccessor<IMapCoordinateConverter>? coordinateAccessor = null,
        ILoggerFactory? loggers = null,
        IRenderStateControllerAccessor? renderStateAccessor = null,
        GlobalTimeService? globalTime = null,
        IRenderActivityMonitor? renderActivityMonitor = null,
        IDatasetLoadGateway? loadGateway = null,
        EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm? ownShipHelm = null,
        RoutesService? routesService = null,
        IGeographicPickPresenter? pickPresenter = null,
        IViewerUiControllerAccessor? uiControllerAccessor = null,
        IAppScreenshotProvider? appScreenshot = null,
        MapPresentationStateProjection? presentationProjection = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        _catalog = catalog;
        _settings = settings;
        _snapshotAccessor = snapshotAccessor;
        _viewportAccessor = viewportAccessor;
        _coordinateAccessor = coordinateAccessor;
        _renderStateAccessor = renderStateAccessor;
        _presentationProjection = presentationProjection;
        _globalTime = globalTime;
        _renderActivityMonitor = renderActivityMonitor;
        _loadGateway = loadGateway;
        _ownShipHelm = ownShipHelm;
        _routesService = routesService;
        _pickPresenter = pickPresenter;
        _uiControllerAccessor = uiControllerAccessor;
        _appScreenshot = appScreenshot;
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
        var tools = new System.Collections.Generic.List<McpServerTool>();
        if (_viewportAccessor is not null)
        {
            tools.Add(SetViewportMcpAdapter.Create(new SetViewportTool(_viewportAccessor)));
        }
        if (_coordinateAccessor is not null)
        {
            tools.Add(PickFeaturesMcpAdapter.Create(
                new PickFeaturesTool(_coordinateAccessor, _catalog, _pickPresenter)));
        }
        // Presentation (set_palette / set_display_category / set_display_mode)
        // and time (set_time_step) now come from the shared, renderer-neutral
        // S100MutableTools factory. The viewer adapts its own services onto the
        // shared capability seams (ViewerPresentationController,
        // ViewerTimeController) instead of carrying duplicate tool classes.
        AddSharedMutableTools(tools);

        if (_renderStateAccessor is not null)
        {
            tools.Add(SetRenderSubsystemMcpAdapter.Create(new SetRenderSubsystemTool(_renderStateAccessor)));
        }
        if (_renderActivityMonitor is not null)
        {
            tools.Add(AwaitRenderIdleMcpAdapter.Create(new AwaitRenderIdleTool(_renderActivityMonitor)));
            tools.Add(GetRenderStatsMcpAdapter.Create(new GetRenderStatsTool(_renderActivityMonitor)));
        }
        if (_ownShipHelm is not null)
        {
            tools.Add(SetOwnShipMcpAdapter.Create(new SetOwnShipTool(_ownShipHelm)));
        }
        if (_uiControllerAccessor is not null)
        {
            tools.Add(ListPanelsMcpAdapter.Create(new ListPanelsTool(_uiControllerAccessor)));
            tools.Add(SetPanelMcpAdapter.Create(new SetPanelTool(_uiControllerAccessor)));
        }
        if (_appScreenshot is not null)
        {
            tools.Add(CaptureAppScreenshotMcpAdapter.Create(new CaptureAppScreenshotTool(_appScreenshot)));
        }
        if (_routesService is not null)
        {
            var invoker = new DispatcherRouteEditInvoker();
            tools.Add(CreateRouteMcpAdapter.Create(new CreateRouteTool(_routesService, invoker)));
            tools.Add(ListRoutesMcpAdapter.Create(new ListRoutesTool(_routesService, invoker)));
            tools.Add(GetRouteMcpAdapter.Create(new GetRouteTool(_routesService, invoker)));
            tools.Add(DeleteRouteMcpAdapter.Create(new DeleteRouteTool(_routesService, invoker)));
            tools.Add(AppendWaypointMcpAdapter.Create(new AppendWaypointTool(_routesService, invoker)));
            tools.Add(InsertWaypointMcpAdapter.Create(new InsertWaypointTool(_routesService, invoker)));
            tools.Add(MoveWaypointMcpAdapter.Create(new MoveWaypointTool(_routesService, invoker)));
            tools.Add(DeleteWaypointMcpAdapter.Create(new DeleteWaypointTool(_routesService, invoker)));
            tools.Add(SetLegAttributesMcpAdapter.Create(new SetLegAttributesTool(_routesService, invoker)));
            tools.Add(SetRouteInfoMcpAdapter.Create(new SetRouteInfoTool(_routesService, invoker)));
        }
        return tools.Count == 0 ? null : tools;
    }

    /// <summary>
    /// Appends the shared, renderer-neutral mutating tools the viewer can back
    /// today — presentation (<c>set_palette</c> / <c>set_display_category</c> /
    /// <c>set_display_mode</c>) and time (<c>set_time_step</c>) — bound to the
    /// viewer's services through capability adapters. Each accessor is
    /// <see langword="null"/> when its backing service is unavailable, so the
    /// factory omits the corresponding tools.
    /// </summary>
    private void AddSharedMutableTools(System.Collections.Generic.List<McpServerTool> tools)
    {
        Mutable.ICapabilityAccessor<Mutable.IPresentationController>? presentation =
            _renderStateAccessor is not null && _presentationProjection is not null
                ? new DelegatingCapabilityAccessor<Mutable.IPresentationController>(() =>
                    _renderStateAccessor.Current is { } controller
                        ? new ViewerPresentationController(
                            controller, _presentationProjection.CreateSnapshot)
                        : null)
                : null;

        Mutable.ICapabilityAccessor<Mutable.ITimeController>? time =
            _globalTime is not null
                ? new Mutable.StaticCapabilityAccessor<Mutable.ITimeController>(
                    new ViewerTimeController(_globalTime))
                : null;

        // render_to_image: the viewer's live-map snapshot renderer, with its
        // coordinate converter supplying the live viewport size for the unsized
        // capture default and the echoed viewport dimensions.
        Mutable.ICapabilityAccessor<Mutable.IImageRenderer>? renderer =
            _snapshotAccessor is not null
                ? new DelegatingCapabilityAccessor<Mutable.IImageRenderer>(() =>
                    _snapshotAccessor.Current is { } snapshot
                        ? new ViewerImageRenderer(snapshot, _coordinateAccessor?.Current)
                        : null)
                : null;

        // open_dataset / close_dataset / close_all_datasets: the viewer's
        // read-only catalog plus its UI-thread load gateway, presented as the
        // shared mutable-catalog seam. Passed directly (not via an accessor)
        // because the catalog exists for the whole session; readiness of the
        // load path is handled inside the adapter.
        Mutable.IMutableDatasetCatalog? catalog = _loadGateway is not null
            ? new ViewerMutableDatasetCatalog(_catalog, _loadGateway)
            : null;

        if (presentation is null && time is null && renderer is null && catalog is null)
        {
            return;
        }

        foreach (var tool in SharedMutableTools.Create(
            presentation: presentation, time: time, renderer: renderer, catalog: catalog))
        {
            tools.Add(tool);
        }
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
