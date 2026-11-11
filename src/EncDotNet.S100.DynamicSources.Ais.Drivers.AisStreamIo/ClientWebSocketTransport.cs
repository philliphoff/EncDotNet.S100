using System.Net.WebSockets;
using System.Text;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

/// <summary>
/// Production transport — a thin wrapper around
/// <see cref="ClientWebSocket"/>. Reassembles fragmented text
/// frames before yielding them.
/// </summary>
public sealed class ClientWebSocketTransport : IAisStreamIoTransport
{
    private readonly Func<ClientWebSocket> _factory;
    private ClientWebSocket? _socket;

    /// <summary>
    /// Creates a transport that produces a new
    /// <see cref="ClientWebSocket"/> on each
    /// <see cref="ConnectAsync"/>.
    /// </summary>
    public ClientWebSocketTransport()
        : this(() => new ClientWebSocket())
    {
    }

    /// <summary>
    /// Creates a transport with a caller-supplied factory — used
    /// by tests that want to set proxies, headers, or fault
    /// injection.
    /// </summary>
    public ClientWebSocketTransport(Func<ClientWebSocket> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (_socket is { State: WebSocketState.Open or WebSocketState.Connecting })
        {
            throw new InvalidOperationException("Transport is already connected.");
        }
        var socket = _factory();
        await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        _socket = socket;
    }

    /// <inheritdoc />
    public async Task SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var socket = _socket ?? throw new InvalidOperationException("Not connected.");
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("Not connected.");
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_socket is null) return;
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                // Bound the close handshake — if the peer is wedged
                // we don't want Dispose to hang the host process.
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    statusDescription: null,
                    closeCts.Token).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
            // Already-broken sockets — drop quietly on dispose.
        }
        catch (OperationCanceledException)
        {
            // Close-handshake timed out — drop into Dispose() below
            // which will Abort the underlying socket.
        }
        _socket.Dispose();
        _socket = null;
    }
}
