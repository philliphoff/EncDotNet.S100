using EncDotNet.S100.Pipelines;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

/// <summary>
/// One running aisstream.io subscription. Owns a transport, a
/// background receive loop, and a reconnect controller.
/// </summary>
internal sealed class AisStreamIoSubscription : IAisSubscription
{
    private readonly AisStreamIoOptions _options;
    private readonly Func<IAisStreamIoTransport> _transportFactory;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();

    private AisSubscriptionRequest _activeRequest;
    private IAisStreamIoTransport? _transport;
    private Task? _loop;
    private bool _resubscribePending;

    public AisStreamIoSubscription(
        AisStreamIoOptions options,
        Func<IAisStreamIoTransport> transportFactory,
        AisSubscriptionRequest initialRequest,
        ILogger logger)
    {
        _options = options;
        _transportFactory = transportFactory;
        _logger = logger;
        _activeRequest = initialRequest;
    }

    public AisSubscriptionRequest ActiveRequest
    {
        get { lock (_stateLock) return _activeRequest; }
    }

    public event EventHandler<AisPositionReport>? PositionReportReceived;
    public event EventHandler<AisStaticVoyageData>? StaticVoyageDataReceived;
    public event EventHandler<AisTargetLost>? TargetLost;

    /// <summary>
    /// aisstream.io maps swap-and-replace cleanly to in-place
    /// updates — we always return <see langword="true"/>. The
    /// receive loop notices the change on its next iteration and
    /// resends the subscribe frame.
    /// </summary>
    public bool TryUpdateArea(BoundingBox? area)
    {
        lock (_stateLock)
        {
            _activeRequest = _activeRequest with { Area = area };
            _resubscribePending = true;
        }
        return true;
    }

    internal void Start()
    {
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var backoff = _options.InitialReconnectBackoff;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var transport = _transportFactory();
                lock (_stateLock) _transport = transport;
                try
                {
                    await transport.ConnectAsync(_options.Endpoint, connectCts.Token).ConfigureAwait(false);

                    await SendSubscribeAsync(transport, connectCts.Token).ConfigureAwait(false);
                    backoff = _options.InitialReconnectBackoff;

                    await ReceiveUntilDoneAsync(transport, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (_stateLock) _transport = null;
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "aisstream.io transport error; reconnecting after {Backoff}.", backoff);
            }

            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            backoff = TimeSpan.FromMilliseconds(
                Math.Min(backoff.TotalMilliseconds * 2, _options.MaxReconnectBackoff.TotalMilliseconds));
        }
    }

    private async Task SendSubscribeAsync(IAisStreamIoTransport transport, CancellationToken cancellationToken)
    {
        AisSubscriptionRequest snapshot;
        lock (_stateLock) snapshot = _activeRequest;

        var frame = AisStreamIoJson.BuildSubscribeFrame(_options.ApiKey, snapshot);
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(_options.SubscribeDeadline);
        await transport.SendTextAsync(frame, deadlineCts.Token).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("aisstream.io subscribe sent: {Frame}", AisStreamIoJson.RedactApiKey(frame, _options.ApiKey));
        }
    }

    private async Task ReceiveUntilDoneAsync(IAisStreamIoTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // If a TryUpdateArea call has flagged a resubscribe, send a fresh frame
            // before the next receive.
            bool needsResubscribe;
            lock (_stateLock)
            {
                needsResubscribe = _resubscribePending;
                _resubscribePending = false;
            }
            if (needsResubscribe)
            {
                await SendSubscribeAsync(transport, cancellationToken).ConfigureAwait(false);
            }

            var frame = await transport.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null) return; // peer closed
            DispatchFrame(frame);
        }
    }

    private void DispatchFrame(string frame)
    {
        var message = AisStreamIoJson.ParseInbound(frame);
        switch (message)
        {
            case AisPositionReport pr:
                if (PassesFilter(pr.Mmsi))
                {
                    PositionReportReceived?.Invoke(this, pr);
                }
                break;
            case AisStaticVoyageData sd:
                if (PassesFilter(sd.Mmsi))
                {
                    StaticVoyageDataReceived?.Invoke(this, sd);
                }
                break;
            default:
                _logger.LogTrace("Ignoring inbound aisstream.io frame ({Length} bytes).", frame.Length);
                break;
        }
        _ = TargetLost; // suppress unused-event warning; populated only by future drivers.
    }

    private bool PassesFilter(uint mmsi)
    {
        AisSubscriptionRequest active;
        lock (_stateLock) active = _activeRequest;
        if (active.Mmsis is { Count: > 0 } mmsis && !mmsis.Contains(mmsi)) return false;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_loop is not null) await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        IAisStreamIoTransport? transport;
        lock (_stateLock)
        {
            transport = _transport;
            _transport = null;
        }
        if (transport is not null)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        _cts.Dispose();
    }
}
