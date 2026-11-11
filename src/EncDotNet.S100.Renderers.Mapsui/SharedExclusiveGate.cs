namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// An async gate that admits any number of shared holders or one exclusive
/// holder, granting waiters in arrival order.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MapsuiDatasetLayerSession"/> uses it so single-dataset renders
/// (shared) run in parallel while a bulk refresh (exclusive) still waits for
/// them and keeps new ones out until it has applied every dataset. Earlier a
/// single <see cref="SemaphoreSlim"/> made every render wait for every other,
/// so a batch of on-pan loads queued on one render slot.
/// </para>
/// <para>
/// Waiters are granted first in, first out: a shared request that arrives
/// after a waiting exclusive one waits behind it, so a steady stream of
/// renders cannot starve a refresh. A cancelled waiter leaves the queue and
/// lets any waiters it was holding back proceed.
/// </para>
/// </remarks>
internal sealed class SharedExclusiveGate
{
    private readonly object _sync = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private int _sharedHolders;
    private bool _exclusiveHeld;

    /// <summary>Waits to enter the gate as one of any number of shared holders.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes once the gate is held.</returns>
    public Task EnterSharedAsync(CancellationToken cancellationToken = default) =>
        EnterAsync(exclusive: false, cancellationToken);

    /// <summary>Waits to enter the gate as its only holder.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes once the gate is held.</returns>
    public Task EnterExclusiveAsync(CancellationToken cancellationToken = default) =>
        EnterAsync(exclusive: true, cancellationToken);

    /// <summary>Releases a shared hold taken by <see cref="EnterSharedAsync"/>.</summary>
    public void ExitShared()
    {
        lock (_sync)
        {
            if (_sharedHolders <= 0)
                throw new InvalidOperationException("The gate is not held shared.");
            _sharedHolders--;
            GrantWaiters();
        }
    }

    /// <summary>Releases the hold taken by <see cref="EnterExclusiveAsync"/>.</summary>
    public void ExitExclusive()
    {
        lock (_sync)
        {
            if (!_exclusiveHeld)
                throw new InvalidOperationException("The gate is not held exclusively.");
            _exclusiveHeld = false;
            GrantWaiters();
        }
    }

    private Task EnterAsync(bool exclusive, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        Waiter waiter;
        LinkedListNode<Waiter> node;
        lock (_sync)
        {
            if (_waiters.Count == 0 && CanEnter(exclusive))
            {
                Take(exclusive);
                return Task.CompletedTask;
            }

            waiter = new Waiter(exclusive);
            node = _waiters.AddLast(waiter);
        }

        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(
                () => Cancel(node, cancellationToken));
            _ = waiter.Completion.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return waiter.Completion.Task;
    }

    private void Cancel(LinkedListNode<Waiter> node, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            // Already granted: the caller holds the gate and releases it as usual.
            if (node.List is null)
                return;

            _waiters.Remove(node);
            node.Value.Completion.TrySetCanceled(cancellationToken);

            // A cancelled exclusive waiter at the head may have been holding
            // back shared waiters that can now enter.
            GrantWaiters();
        }
    }

    private bool CanEnter(bool exclusive) =>
        exclusive ? !_exclusiveHeld && _sharedHolders == 0 : !_exclusiveHeld;

    private void Take(bool exclusive)
    {
        if (exclusive)
            _exclusiveHeld = true;
        else
            _sharedHolders++;
    }

    private void GrantWaiters()
    {
        while (_waiters.First is { } head && CanEnter(head.Value.Exclusive))
        {
            _waiters.RemoveFirst();
            Take(head.Value.Exclusive);
            head.Value.Completion.TrySetResult();
        }
    }

    private sealed class Waiter(bool exclusive)
    {
        public bool Exclusive { get; } = exclusive;

        // Continuations run off this lock so a grant never runs caller code inline.
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
