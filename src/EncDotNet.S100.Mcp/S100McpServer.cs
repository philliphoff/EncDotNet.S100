using System.Net;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Streamable-HTTP MCP server that exposes the three MCP-1 tools
/// (<see cref="ListDatasetsTool"/>, <see cref="DescribeFeatureTool"/>,
/// <see cref="SampleCoverageTool"/>) over an in-process Kestrel
/// listener.
/// </summary>
/// <remarks>
/// <para>
/// The server hosts an ASP.NET Core <see cref="WebApplication"/>
/// internally and maps the MCP Streamable HTTP transport at
/// <c>"/"</c> (the spec's default). External agents connect to
/// <see cref="Endpoint"/>.
/// </para>
/// <para>
/// Security: there is no authentication. The only protection is the
/// loopback bind address — by default <see cref="IPAddress.Loopback"/>.
/// Callers must not expose a non-loopback bind in a UI in v1.
/// </para>
/// <para>
/// Read-only: the wrapped tools never mutate host state. The server
/// itself adds nothing beyond surfacing them.
/// </para>
/// </remarks>
public sealed class S100McpServer : IAsyncDisposable
{
    private readonly IDatasetCatalog _catalog;
    private readonly S100McpServerOptions _options;
    private readonly ILoggerFactory? _loggerFactory;

    private WebApplication? _app;
    private int _connectionCount;

    /// <summary>Creates a new server instance bound to the supplied catalog.</summary>
    /// <param name="catalog">The dataset catalog the tools read from.</param>
    /// <param name="options">Bind / transport options.</param>
    /// <param name="loggers">
    /// Optional logger factory. When null, the SDK's default null
    /// logger is used.
    /// </param>
    public S100McpServer(
        IDatasetCatalog catalog,
        S100McpServerOptions options,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        _catalog = catalog;
        _options = options;
        _loggerFactory = loggers;
    }

    /// <summary>True once <see cref="StartAsync"/> has completed; false after <see cref="StopAsync"/>.</summary>
    public bool IsRunning => _app is not null && Port is not null;

    /// <summary>The TCP port the listener is bound to, or <see langword="null"/> until <see cref="StartAsync"/> succeeds.</summary>
    public int? Port { get; private set; }

    /// <summary>Endpoint URI external agents should connect to, or <see langword="null"/> until <see cref="StartAsync"/> succeeds.</summary>
    public Uri? Endpoint { get; private set; }

    /// <summary>Number of currently-open MCP transport connections.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Raised on the calling pool thread when <see cref="ConnectionCount"/> changes.</summary>
    public event EventHandler<EventArgs>? ConnectionsChanged;

    /// <summary>
    /// Raised when <see cref="IsRunning"/>, <see cref="Port"/>, or
    /// <see cref="Endpoint"/> may have changed (start / stop).
    /// </summary>
    public event EventHandler<EventArgs>? StateChanged;

    /// <summary>Starts the server. Idempotent — does nothing if already running.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        if (_loggerFactory is not null)
        {
            // The viewer typically passes its own ILoggerFactory so MCP
            // requests show up alongside the rest of the app's logs.
            builder.Services.AddSingleton(_loggerFactory);
        }

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(_options.BindAddress, _options.Port);
            k.Limits.KeepAliveTimeout = _options.IdleTimeout;
        });

        // Register the MCP-1 tools as in-process singletons.
        var listDatasets = new ListDatasetsTool(_catalog);
        var describeFeature = new DescribeFeatureTool(_catalog);
        var sampleCoverage = new SampleCoverageTool(_catalog);
        var findAt = new FindAtTool(_catalog);
        var queryFeatures = new QueryFeaturesTool(_catalog);
        var sampleCoverageAlong = new SampleCoverageAlongTool(_catalog);
        var listSpecs = new ListSpecsTool(_catalog);
        var listTimeSteps = new ListTimeStepsTool(_catalog);
        var tools = S100McpServerToolFactory
            .CreateTools(listDatasets, describeFeature, sampleCoverage, findAt, queryFeatures, sampleCoverageAlong, listSpecs, listTimeSteps)
            .ToArray();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        // Surface connection lifecycle to consumers (status bar).
        builder.Services.AddSingleton<IConnectionCountReporter>(_ => new ConnectionCountReporter(this));
        builder.Services.AddTransient<ConnectionTrackingMiddleware>();

        var app = builder.Build();
        app.UseMiddleware<ConnectionTrackingMiddleware>();
        app.MapMcp("/");

        await app.StartAsync(ct).ConfigureAwait(false);

        Port = ResolveBoundPort(app);
        Endpoint = BuildEndpoint(_options.BindAddress, Port!.Value);
        _app = app;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops the server. Idempotent.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;
        Port = null;
        Endpoint = null;
        var prevConnections = Interlocked.Exchange(ref _connectionCount, 0);

        try
        {
            await app.StopAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }

        if (prevConnections != 0)
        {
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    internal void IncrementConnections()
    {
        Interlocked.Increment(ref _connectionCount);
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void DecrementConnections()
    {
        Interlocked.Decrement(ref _connectionCount);
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int ResolveBoundPort(WebApplication app)
    {
        var addressFeature = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();
        var address = addressFeature?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report a bound address.");
        // Kestrel reports addresses as "http://host:port".
        var uri = new Uri(address);
        return uri.Port;
    }

    private static Uri BuildEndpoint(IPAddress address, int port)
    {
        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
        return new Uri($"http://{host}:{port}/");
    }
}

/// <summary>Internal indirection so middleware can find the owning server.</summary>
internal interface IConnectionCountReporter
{
    void Increment();
    void Decrement();
}

internal sealed class ConnectionCountReporter(S100McpServer server) : IConnectionCountReporter
{
    public void Increment() => server.IncrementConnections();
    public void Decrement() => server.DecrementConnections();
}
