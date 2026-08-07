using EncDotNet.S100.Datasets.Pipelines.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Hosts the read-only S-100 MCP tool set over the <b>stdio</b> transport:
/// the process speaks MCP over its own standard input / output. This is the
/// transport a client uses when it spawns the server itself (the canonical
/// <c>command</c> + <c>args</c> pattern), as opposed to the connect-out-of-band
/// Streamable HTTP transport in <see cref="S100McpServer"/>.
/// </summary>
/// <remarks>
/// <para>
/// In stdio mode <b>standard output is the protocol channel</b>. All logging
/// is pinned to standard error so it can never corrupt the MCP stream; callers
/// (and the tools) must likewise avoid writing to <see cref="Console.Out"/>.
/// </para>
/// <para>
/// The host runs until standard input reaches end-of-file (the client
/// disconnected) or the supplied cancellation token is signalled
/// (e.g. Ctrl-C). The catalog is read-only, so a single process serves a
/// single fixed set of datasets; spawn another process for another set.
/// </para>
/// </remarks>
public static class S100McpStdioHost
{
    /// <summary>
    /// Serves the read-only catalog tools over stdio until the client
    /// disconnects or the token is cancelled.
    /// </summary>
    /// <param name="catalog">The dataset catalog the tools read from.</param>
    /// <param name="additionalTools">
    /// Optional host-supplied tools appended to the built-in set. Names must
    /// not collide with the built-ins. May be <see langword="null"/> or empty.
    /// </param>
    /// <param name="cancellationToken">Stops the host when signalled.</param>
    public static async Task RunAsync(
        IDatasetCatalog catalog,
        IReadOnlyList<McpServerTool>? additionalTools = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var builder = Host.CreateApplicationBuilder();

        // stdout is the MCP transport channel in stdio mode; keep every log
        // record on stderr so it never corrupts the protocol stream.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // Suppress the generic host's "Application started / stopping" chatter
        // so stderr carries only real diagnostics.
        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);

        var tools = S100McpTools.Create(catalog).ToList();
        if (additionalTools is { Count: > 0 } extra)
        {
            tools.AddRange(extra);
        }

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(tools);

        using var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
