using System.Diagnostics.Metrics;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Diagnostics;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class MapPaintInstrumentationTests
{
    private sealed record Measurement(
        string Instrument,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);

    [Fact]
    public void EndPaintAndEmit_SeparatesFeatureClassesAndTagsFallback()
    {
        var layerName = $"issue-165-{Guid.NewGuid():N}";
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements, layerName);

        var depthContour = new PointFeature(0, 0);
        depthContour[FeatureTagKeys.FeatureType] = "DepthContour";
        var coastline = new PointFeature(0, 0);
        coastline[FeatureTagKeys.FeatureType] = "Coastline";
        var unclassified = new PointFeature(0, 0);

        MapPaintInstrumentation.BeginPaint();
        MapPaintInstrumentation.RecordDraw(
            MapPaintInstrumentation.CreateMetricKey("VectorStyle", layerName, depthContour),
            3.5);
        MapPaintInstrumentation.RecordDraw(
            MapPaintInstrumentation.CreateMetricKey("VectorStyle", layerName, coastline),
            2.5);
        MapPaintInstrumentation.RecordDraw(
            MapPaintInstrumentation.CreateMetricKey("VectorStyle", layerName, unclassified),
            1.5);
        MapPaintInstrumentation.EndPaintAndEmit();

        var callMeasurements = measurements
            .Where(measurement => measurement.Instrument == "s100.map.paint.style.calls")
            .ToArray();
        var durationMeasurements = measurements
            .Where(measurement => measurement.Instrument == "s100.map.paint.style.duration")
            .ToArray();

        Assert.Equal(3, callMeasurements.Length);
        Assert.All(callMeasurements, measurement => Assert.Equal(1, measurement.Value));
        Assert.Equal(
            new[] { "(unclassified)", "Coastline", "DepthContour" },
            callMeasurements
                .Select(measurement => Assert.IsType<string>(measurement.Tags["featureClass"]))
                .OrderBy(featureClass => featureClass)
                .ToArray());
        Assert.Equal(3, durationMeasurements.Length);
        Assert.Equal(
            new[] { "(unclassified)", "Coastline", "DepthContour" },
            durationMeasurements
                .Select(measurement => Assert.IsType<string>(measurement.Tags["featureClass"]))
                .OrderBy(featureClass => featureClass)
                .ToArray());
        Assert.All(durationMeasurements, measurement =>
        {
            Assert.Equal("VectorStyle", measurement.Tags["style"]);
            Assert.Equal(layerName, measurement.Tags["layer"]);
            Assert.Equal("n/a", measurement.Tags["points"]);
        });
    }

    private static MeterListener StartCapture(List<Measurement> sink, string layerName)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "EncDotNet.S100.Viewer"
                    && instrument.Name is "s100.map.paint.style.calls" or "s100.map.paint.style.duration")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Capture(instrument, value, tags, layerName, sink));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            Capture(instrument, value, tags, layerName, sink));
        listener.Start();
        return listener;
    }

    private static void Capture(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string layerName,
        List<Measurement> sink)
    {
        var tagMap = new Dictionary<string, object?>(tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            tagMap[tags[i].Key] = tags[i].Value;
        }

        if (tagMap.TryGetValue("layer", out var layer) && Equals(layer, layerName))
        {
            sink.Add(new Measurement(instrument.Name, value, tagMap));
        }
    }
}
