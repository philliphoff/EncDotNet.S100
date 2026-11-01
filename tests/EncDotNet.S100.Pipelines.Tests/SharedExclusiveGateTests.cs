using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

public sealed class SharedExclusiveGateTests
{
    [Fact]
    public void SharedHoldersEnterTogether()
    {
        var gate = new SharedExclusiveGate();

        var first = gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        var second = gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        gate.ExitShared();
        gate.ExitShared();
    }

    [Fact]
    public async Task ExclusiveWaitsForSharedHoldersToLeave()
    {
        var gate = new SharedExclusiveGate();
        await gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        await gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        var exclusive = gate.EnterExclusiveAsync(TestContext.Current.CancellationToken);
        Assert.False(exclusive.IsCompleted);

        gate.ExitShared();
        Assert.False(exclusive.IsCompleted);
        gate.ExitShared();

        await exclusive.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.ExitExclusive();
    }

    [Fact]
    public async Task SharedRequestQueuesBehindWaitingExclusive()
    {
        var gate = new SharedExclusiveGate();
        await gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        var exclusive = gate.EnterExclusiveAsync(TestContext.Current.CancellationToken);
        var lateShared = gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        Assert.False(exclusive.IsCompleted);
        Assert.False(lateShared.IsCompleted);

        gate.ExitShared();
        await exclusive.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(lateShared.IsCompleted);

        gate.ExitExclusive();
        await lateShared.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.ExitShared();
    }

    [Fact]
    public async Task CancelledExclusiveWaiterLetsQueuedSharedRequestsIn()
    {
        var gate = new SharedExclusiveGate();
        await gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();

        var exclusive = gate.EnterExclusiveAsync(cts.Token);
        var lateShared = gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        Assert.False(lateShared.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exclusive);
        await lateShared.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.ExitShared();
        gate.ExitShared();

        // The gate is free again: an exclusive request enters at once.
        Assert.True(gate.EnterExclusiveAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        gate.ExitExclusive();
    }

    [Fact]
    public void ExitWithoutHoldThrows()
    {
        var gate = new SharedExclusiveGate();

        Assert.Throws<InvalidOperationException>(gate.ExitShared);
        Assert.Throws<InvalidOperationException>(gate.ExitExclusive);
    }
}
