using System.Threading.Channels;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.Tests;

/// <summary>
/// In-process <see cref="IAisStreamIoTransport"/> for tests. Records
/// every outgoing JSON frame and yields scripted inbound frames in
/// order. Closes (yields <see langword="null"/>) when the script
/// runs out and <see cref="ScriptClose"/> has been called.
/// </summary>
/// <remarks>
/// The subscription's receive loop calls into the transport on a
/// thread-pool thread while the test reads it from another, so all
/// recorded state is guarded and tests wait on signals rather than
/// polling unsynchronized collections.
/// </remarks>
internal sealed class FakeAisStreamIoTransport : IAisStreamIoTransport
{
    /// <summary>
    /// Upper bound on any single wait. Only reached when the
    /// awaited event never happens, so it is generous enough to ride
    /// out a heavily loaded CI runner.
    /// </summary>
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
    private readonly object _gate = new();
    private readonly List<string> _outbound = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _outboundWaiters = new();
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Snapshot of the frames sent so far.</summary>
    public IReadOnlyList<string> OutboundFrames
    {
        get { lock (_gate) return _outbound.ToArray(); }
    }

    public bool Disposed => _disposed.Task.IsCompleted;

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _outbound.Add(payload);
            for (var i = _outboundWaiters.Count - 1; i >= 0; i--)
            {
                if (_outbound.Count >= _outboundWaiters[i].Count)
                {
                    _outboundWaiters[i].Signal.TrySetResult();
                    _outboundWaiters.RemoveAt(i);
                }
            }
        }
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

    /// <summary>
    /// Completes once at least <paramref name="count"/> frames have
    /// been sent, and returns a snapshot of them.
    /// </summary>
    public async Task<IReadOnlyList<string>> WaitForOutboundFramesAsync(int count)
    {
        Task wait;
        lock (_gate)
        {
            if (_outbound.Count >= count) return _outbound.ToArray();
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _outboundWaiters.Add((count, signal));
            wait = signal.Task;
        }
        await wait.WaitAsync(WaitTimeout).ConfigureAwait(false);
        return OutboundFrames;
    }

    /// <summary>Completes once the transport has been disposed.</summary>
    public Task WaitForDisposedAsync() => _disposed.Task.WaitAsync(WaitTimeout);

    public ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        _disposed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Transport factory handed to <see cref="AisStreamIoMessageSource"/>.
/// Hands each created transport to the test through a channel, so the
/// test never reads a collection the receive loop is writing to.
/// </summary>
internal sealed class FakeAisStreamIoTransportFactory
{
    private readonly Channel<FakeAisStreamIoTransport> _created = Channel.CreateUnbounded<FakeAisStreamIoTransport>();

    public IAisStreamIoTransport Create()
    {
        var transport = new FakeAisStreamIoTransport();
        _created.Writer.TryWrite(transport);
        return transport;
    }

    /// <summary>Returns the next transport the source creates, in creation order.</summary>
    public async Task<FakeAisStreamIoTransport> NextAsync() =>
        await _created.Reader.ReadAsync().AsTask().WaitAsync(FakeAisStreamIoTransport.WaitTimeout).ConfigureAwait(false);
}
