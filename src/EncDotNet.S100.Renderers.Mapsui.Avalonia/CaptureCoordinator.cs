using System.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

internal static class CaptureCoordinator
{
    internal static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan CaptureDrainTimeout = TimeSpan.FromMilliseconds(750);

    private static readonly object GateState = new();
    private static readonly SemaphoreSlim CaptureSequence = new(1, 1);
    private static readonly SemaphoreSlim DrainSignal = new(0);
    private static int _captureDepth;
    private static int _gateDepth;
    private static int _gateOwnerThreadId;
    private static long _gateGeneration;

    internal static bool CaptureActive => Volatile.Read(ref _captureDepth) > 0;

    internal static void BeginCapture() => Interlocked.Increment(ref _captureDepth);

    internal static void EndCapture() => Interlocked.Decrement(ref _captureDepth);

    internal static void NotifyDrained() => DrainSignal.Release();

    internal static bool WaitForFreshDrain(TimeSpan timeout)
    {
        DiscardStaleDrainSignals();
        return DrainSignal.Wait(timeout);
    }

    internal static async Task<bool> WaitForFreshDrainAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DiscardStaleDrainSignals();
        return await DrainSignal.WaitAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static GateLease EnterLivePaint()
    {
        lock (GateState)
        {
            var threadId = Environment.CurrentManagedThreadId;
            if (_gateOwnerThreadId == threadId)
            {
                // Live paint operations are not nested. Re-entry means a prior
                // frame failed before its end marker; invalidate that lease so
                // the compositor can recover rather than retaining the gate.
                _gateGeneration++;
                _gateDepth = 1;
                return new GateLease(_gateGeneration, threadId);
            }

            while (_gateOwnerThreadId != 0)
            {
                Monitor.Wait(GateState);
            }

            _gateOwnerThreadId = threadId;
            _gateDepth = 1;
            return new GateLease(_gateGeneration, threadId);
        }
    }

    internal static void ExitGate(GateLease lease)
    {
        lock (GateState)
        {
            if (lease.Generation != _gateGeneration
                || lease.OwnerThreadId != _gateOwnerThreadId)
            {
                return;
            }

            _gateDepth--;
            if (_gateDepth == 0)
            {
                _gateOwnerThreadId = 0;
                Monitor.PulseAll(GateState);
            }
        }
    }

    internal static byte[]? RunCapture(Func<byte[]?> capture, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(capture);

        CaptureSequence.Wait();
        BeginCapture();
        try
        {
            var lease = AcquireGate(timeout, CancellationToken.None);
            try
            {
                return capture();
            }
            finally
            {
                ExitGate(lease);
            }
        }
        finally
        {
            EndCapture();
            CaptureSequence.Release();
        }
    }

    internal static async Task<byte[]?> CaptureDrainedAsync(
        Func<Task> requestRepaintAsync,
        Func<Task<byte[]?>> captureAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestRepaintAsync);
        ArgumentNullException.ThrowIfNull(captureAsync);

        cancellationToken.ThrowIfCancellationRequested();
        await CaptureSequence.WaitAsync(cancellationToken).ConfigureAwait(false);
        BeginCapture();
        try
        {
            DiscardStaleDrainSignals();
            await requestRepaintAsync().ConfigureAwait(false);
            await DrainSignal.WaitAsync(CaptureDrainTimeout, cancellationToken)
                .ConfigureAwait(false);

            var lease = await Task.Run(
                () => AcquireGate(GateTimeout, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            try
            {
                return await captureAsync().ConfigureAwait(false);
            }
            finally
            {
                ExitGate(lease);
            }
        }
        finally
        {
            EndCapture();
            CaptureSequence.Release();
        }
    }

    private static GateLease AcquireGate(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var cancellationRegistration = cancellationToken.Register(
            static () =>
            {
                lock (GateState)
                {
                    Monitor.PulseAll(GateState);
                }
            });

        lock (GateState)
        {
            var threadId = Environment.CurrentManagedThreadId;
            if (_gateOwnerThreadId == 0 || _gateOwnerThreadId == threadId)
            {
                _gateOwnerThreadId = threadId;
                _gateDepth++;
                return new GateLease(_gateGeneration, threadId);
            }

            var remaining = timeout;
            while (_gateOwnerThreadId != 0 && remaining > TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(GateState, remaining);
                remaining = timeout - stopwatch.Elapsed;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_gateOwnerThreadId != 0)
            {
                Debug.WriteLine(
                    "CaptureCoordinator: timed out waiting for live paint; " +
                    "recovering the gate and running the capture unsynchronized " +
                    "from the abandoned paint.");
                _gateGeneration++;
            }

            _gateOwnerThreadId = threadId;
            _gateDepth = 1;
            return new GateLease(_gateGeneration, threadId);
        }
    }

    private static void DiscardStaleDrainSignals()
    {
        while (DrainSignal.Wait(0))
        {
        }
    }

    internal readonly record struct GateLease(
        long Generation,
        int OwnerThreadId);
}
