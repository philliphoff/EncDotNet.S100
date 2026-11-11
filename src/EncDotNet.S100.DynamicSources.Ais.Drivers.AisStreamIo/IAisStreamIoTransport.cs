namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

/// <summary>
/// Test seam over <see cref="System.Net.WebSockets.ClientWebSocket"/>.
/// Production wraps a real WebSocket; tests inject an in-process
/// fake that exchanges JSON frames without a network.
/// </summary>
public interface IAisStreamIoTransport : IAsyncDisposable
{
    /// <summary>
    /// Opens a transport connection to the supplied endpoint.
    /// </summary>
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a single text frame (one JSON object).
    /// </summary>
    Task SendTextAsync(string payload, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one inbound text frame. Returns <see langword="null"/>
    /// when the peer has closed the connection cleanly.
    /// </summary>
    Task<string?> ReceiveTextAsync(CancellationToken cancellationToken);
}
