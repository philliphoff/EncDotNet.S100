using System.Threading.Channels;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.Tests;

/// <summary>
/// In-process <see cref="IAisStreamIoTransport"/> for tests. Records
/// every outgoing JSON frame and yields scripted inbound frames in
/// order. Closes (yields <see langword="null"/>) when the script
/// runs out and <see cref="ScriptClose"/> has been called.
/// </summary>
internal sealed class FakeAisStreamIoTransport : IAisStreamIoTransport
{
    private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
    public List<string> OutboundFrames { get; } = new();
    public List<Uri> Connections { get; } = new();
    public bool Disposed { get; private set; }

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        Connections.Add(endpoint);
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        OutboundFrames.Add(payload);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public void EnqueueInbound(string frame) => _inbound.Writer.TryWrite(frame);

    public void ScriptClose() => _inbound.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _inbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
