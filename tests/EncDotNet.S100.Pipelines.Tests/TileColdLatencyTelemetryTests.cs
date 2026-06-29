using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Renderers.Mapsui.Diagnostics;
using Xunit;

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
}
