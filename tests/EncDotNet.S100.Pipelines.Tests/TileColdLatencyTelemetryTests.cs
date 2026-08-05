using System.Diagnostics.Metrics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.Diagnostics;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the cold-path tiling instruments added for cold zoom/pan
/// profiling (<see cref="Telemetry.TileColdLatency"/> and
/// <see cref="Telemetry.TileVisibleQueueDepth"/>) are published on the
/// renderer meter with the documented names/units and record values. These
/// are the signals that separate "tiling worker is slow to catch up" from
/// "Mapsui paint is slow" during the initial cold gesture.
/// </summary>
public class TileColdLatencyTelemetryTests
{
    [Fact]
    public void ColdLatencyAndQueueDepth_PublishExpectedInstruments()
    {
        var seen = new Dictionary<string, (string Unit, double Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name is "s100.render.tile.cold.latency"
                or "s100.render.tile.visible.queue.depth")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((inst, value, _, _) =>
            seen[inst.Name] = (inst.Unit ?? string.Empty, value));
        listener.SetMeasurementEventCallback<int>((inst, value, _, _) =>
            seen[inst.Name] = (inst.Unit ?? string.Empty, value));
        listener.Start();

        Telemetry.TileColdLatency.Record(42.5);
        Telemetry.TileVisibleQueueDepth.Record(9);

        Assert.True(seen.TryGetValue("s100.render.tile.cold.latency", out var latency));
        Assert.Equal("ms", latency.Unit);
        Assert.Equal(42.5, latency.Value);

        Assert.True(seen.TryGetValue("s100.render.tile.visible.queue.depth", out var depth));
        Assert.Equal("{tile}", depth.Unit);
        Assert.Equal(9, depth.Value);
    }

    [Fact]
    public void MetatileMetrics_PublishExpectedInstruments()
    {
        var seen = new Dictionary<string, string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name.StartsWith(
                    "s100.render.metatile.", StringComparison.Ordinal))
            {
                seen[instrument.Name] = instrument.Unit ?? string.Empty;
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.Start();

        Telemetry.MetatileRasterizeDuration.Record(10);
        Telemetry.MetatileSliceDuration.Record(2);
        Telemetry.MetatileTiles.Record(4);
        Telemetry.MetatileJobs.Add(1);
        Telemetry.MetatileFallbacks.Add(
            1, new KeyValuePair<string, object?>("reason", "scamin"));

        Assert.Equal("ms", seen["s100.render.metatile.rasterize.duration"]);
        Assert.Equal("ms", seen["s100.render.metatile.slice.duration"]);
        Assert.Equal("{tile}", seen["s100.render.metatile.tiles"]);
        Assert.Equal("{job}", seen["s100.render.metatile.jobs"]);
        Assert.Equal("{fallback}", seen["s100.render.metatile.fallbacks"]);
    }

    [Fact]
    public void AsyncDiskWriteMetrics_PublishExpectedInstruments()
    {
        var seen = new Dictionary<string, string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name.StartsWith(
                    "s100.render.tile.disk.write_queue.", StringComparison.Ordinal))
            {
                seen[instrument.Name] = instrument.Unit ?? string.Empty;
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.Start();

        Telemetry.TileDiskWriteQueueDepth.Record(4);
        Telemetry.TileDiskWriteQueueDiscarded.Add(
            1,
            new KeyValuePair<string, object?>("reason", "full"));

        Assert.Equal(
            "{tile}",
            seen["s100.render.tile.disk.write_queue.depth"]);
        Assert.Equal(
            "{tile}",
            seen["s100.render.tile.disk.write_queue.discarded"]);
    }

    [Fact]
    public void TileRasterize_emits_trace_with_tile_and_operation_context()
    {
        // ActivityStopped fires on whatever thread the activity completes on,
        // so unrelated renderer activities stopping concurrently can mutate the
        // collection while we assert over it. The listener is process-global, so
        // sibling tests rasterizing in parallel emit their own
        // s100.render.tile.rasterize activities too. Scope the callback to this
        // test's tile key, guard the list with a lock, and assert over a
        // snapshot.
        const string tileKeys = "3/2/4";
        var observed = new List<System.Diagnostics.Activity>();
        var gate = new object();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == "EncDotNet.S100.Renderers.Mapsui",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<
                System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != "s100.render.tile.rasterize"
                    || (string?)activity.GetTagItem("s100.render.tile.keys")
                        != tileKeys)
                {
                    return;
                }

                lock (gate)
                {
                    observed.Add(activity);
                }
            },
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var bitmap = S100VectorTileRenderer.RasterizeTile(
            new VectorScene([]),
            baseIndex: null,
            new TileKey(3, 2, 4),
            deviceScale: 1);

        System.Diagnostics.Activity[] snapshot;
        lock (gate)
        {
            snapshot = observed.ToArray();
        }

        var activity = Assert.Single(snapshot);
        Assert.Equal(tileKeys, activity.GetTagItem("s100.render.tile.keys"));
        Assert.Equal(3, activity.GetTagItem("s100.render.tile.band"));
        Assert.Equal(0, activity.GetTagItem("s100.render.tile.candidate_operations"));
        Assert.NotNull(activity.GetTagItem("s100.render.tile.width_px"));
    }
}
