using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Serves MCP tools over the Streamable HTTP transport for
/// <c>s100 mcp serve --transport http</c> (issue #1464): the MCP endpoint at
/// <see cref="EndpointPath"/>, guarded by a bearer token when there is one,
/// and the <c>/healthz</c> health check outside it.
/// </summary>
/// <remarks>
/// <para>
/// With a token, every MCP request must carry <c>Authorization: Bearer
/// &lt;token&gt;</c>, or it's answered <c>401 Unauthorized</c>. The token is
/// compared in constant time.
/// </para>
/// <para>
/// A request that carries an <c>Origin</c> header from another host is
/// answered <c>403 Forbidden</c>, as the MCP specification asks of HTTP
/// servers, so a web page in a browser can't reach a server on this machine
/// through DNS rebinding. MCP clients that aren't browsers send no
/// <c>Origin</c>.
/// </para>
/// </remarks>
internal sealed class McpHttpServer : IAsyncDisposable
{
    /// <summary>The MCP endpoint's path.</summary>
    public const string EndpointPath = "/mcp";

    private readonly WebApplication _app;

    private McpHttpServer(WebApplication app, Uri endpoint)
    {
        _app = app;
        Endpoint = endpoint;
    }

    /// <summary>The MCP endpoint, as bound (the host as given; the actual port).</summary>
    public Uri Endpoint { get; }

    /// <summary>The bound port.</summary>
    public int Port => Endpoint.Port;

    /// <summary>Starts serving <paramref name="tools"/> on <paramref name="address"/>:<paramref name="port"/> (0 for any free port).</summary>
    public static async Task<McpHttpServer> StartAsync(
        IReadOnlyList<McpServerTool> tools,
        IPAddress address,
        int port,
        string? token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(address);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(address, port));
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        var app = builder.Build();
        ServeHost.MapHealth(app);

        var expected = token is null ? null : Encoding.UTF8.GetBytes("Bearer " + token);
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(EndpointPath, StringComparison.Ordinal),
            branch => branch.Use((context, next) =>
            {
                if (!IsSameOrigin(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                if (expected is not null && !IsAuthorized(context.Request, expected))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    return Task.CompletedTask;
                }

                return next(context);
            }));
        app.MapMcp(EndpointPath);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        return new McpHttpServer(app, EndpointUri(address, bound.Port));
    }

    /// <summary>The MCP endpoint for an address and port, e.g. <c>http://192.168.1.20:8300/mcp</c>.</summary>
    public static Uri EndpointUri(IPAddress address, int port) =>
        new(ServeHost.BaseUri(address, port, token: null), EndpointPath.TrimStart('/'));

    /// <summary>
    /// Runs until the host shuts down — on Ctrl-C (SIGINT) or SIGTERM, which
    /// the web host handles itself — or <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken) => _app.WaitForShutdownAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsAuthorized(HttpRequest request, byte[] expected)
    {
        var header = request.Headers.Authorization.ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), expected);
    }

    /// <summary>True without an <c>Origin</c> header, or when it names the host the request was sent to.</summary>
    private static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
