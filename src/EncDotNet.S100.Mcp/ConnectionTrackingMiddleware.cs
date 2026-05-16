using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Increments the parent <see cref="S100McpServer"/>'s connection
/// counter on the way in and decrements it on the way out, including
/// when the request body is the long-lived Streamable HTTP "GET"
/// channel.
/// </summary>
/// <remarks>
/// The MCP Streamable HTTP transport uses POSTs for request/response
/// and a long-lived GET for server-initiated events. Both contribute
/// to the connection count for the purposes of the status indicator
/// — it's a "is anything talking to me right now?" gauge, not a
/// session count.
/// </remarks>
internal sealed class ConnectionTrackingMiddleware(IConnectionCountReporter reporter) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        reporter.Increment();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            reporter.Decrement();
        }
    }
}
