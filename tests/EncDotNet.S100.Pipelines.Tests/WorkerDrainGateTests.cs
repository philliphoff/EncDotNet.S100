using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="WorkerDrainGate"/>, the one-way gate the tiled
/// renderer uses to stop background Skia workers and wait for in-flight
/// rasterisation before the process tears down native Skia. These pin the
/// invariants that make the teardown-race fix correct: a fresh gate admits
/// workers; <c>DrainAndWait</c> blocks until every registered worker completes;
/// and once draining, no new worker is admitted.
/// </summary>
public class WorkerDrainGateTests
{
    [Fact]
    public void DrainAndWait_NoWorkers_ReturnsImmediately()
    {
        var gate = new WorkerDrainGate();

        Assert.True(gate.DrainAndWait(TimeSpan.FromSeconds(1)));
        Assert.True(gate.IsDraining);
        Assert.Equal(0, gate.ActiveWorkers);
    }

    [Fact]
    public void TryRegister_BeforeDrain_Succeeds_AndCounts()
    {
        var gate = new WorkerDrainGate();

        Assert.True(gate.TryRegister());
        Assert.Equal(1, gate.ActiveWorkers);

        gate.Complete();
        Assert.Equal(0, gate.ActiveWorkers);
    }

    [Fact]
    public void TryRegister_AfterDrain_Fails_AndDoesNotCount()
    {
        var gate = new WorkerDrainGate();
        gate.DrainAndWait(TimeSpan.FromSeconds(1));

        Assert.False(gate.TryRegister());
        Assert.Equal(0, gate.ActiveWorkers);
    }

    [Fact]
    public async Task DrainAndWait_BlocksUntilRegisteredWorkerCompletes()
    {
        var gate = new WorkerDrainGate();
        Assert.True(gate.TryRegister());

        // Worker still in-flight: a short drain must time out.
        Assert.False(gate.DrainAndWait(TimeSpan.FromMilliseconds(100)));

        // Complete the worker on another thread, then a fresh wait succeeds.
        var completer = Task.Run(() =>
        {
            Thread.Sleep(50);
            gate.Complete();
        });

        Assert.True(gate.DrainAndWait(TimeSpan.FromSeconds(5)));
        await completer;
        Assert.Equal(0, gate.ActiveWorkers);
    }

    [Fact]
    public async Task DrainAndWait_AwaitsWorkerRegisteredConcurrentlyWithDrain()
    {
        // A worker that registers and immediately completes while the drain is
        // racing must still leave the gate idle (no leaked active count).
        var gate = new WorkerDrainGate();

        var worker = Task.Run(() =>
        {
            if (gate.TryRegister())
            {
                gate.Complete();
            }
        });

        gate.DrainAndWait(TimeSpan.FromSeconds(5));
        await worker;

        Assert.Equal(0, gate.ActiveWorkers);
        Assert.True(gate.IsDraining);
    }
}
