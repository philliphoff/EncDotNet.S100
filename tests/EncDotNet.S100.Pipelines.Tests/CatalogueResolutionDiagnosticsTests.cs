using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Diagnostics;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class CatalogueResolutionDiagnosticsTests
{
    private sealed record Measurement(long Value, IReadOnlyDictionary<string, object?> Tags);

    private static MeterListener StartCapture(List<Measurement> sink)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "EncDotNet.S100.Datasets.Pipelines"
                    && instrument.Name == "s100.catalogue.match.count")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            var dict = new Dictionary<string, object?>(tags.Length);
            for (int i = 0; i < tags.Length; i++) dict[tags[i].Key] = tags[i].Value;
            sink.Add(new Measurement(value, dict));
        });
        listener.Start();
        return listener;
    }

    [Fact]
    public void Report_NullCatalogueRef_DoesNothing()
    {

        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements);

        CatalogueResolutionDiagnostics.Report(this, new SpecRef("S-101", new SpecVersion(1, 2, 0)),
            null, "portrayal");

        Assert.Empty(measurements);
    }

    [Fact]
    public void Report_ExactMatch_EmitsCounterWithExactTag()
    {

        var scope = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements);

        CatalogueResolutionDiagnostics.Report(scope,
            new SpecRef("S-101", new SpecVersion(1, 2, 0)),
            new CatalogueRef("S-101", new SpecVersion(1, 2, 0)),
            "portrayal");

        var m = Assert.Single(measurements);
        Assert.Equal(1L, m.Value);
        Assert.Equal("S-101", m.Tags["s100.spec.name"]);
        Assert.Equal("1.2.0", m.Tags["s100.spec.edition"]);
        Assert.Equal("1.2.0", m.Tags["s100.catalogue.version"]);
        Assert.Equal("portrayal", m.Tags["s100.catalogue.kind"]);
        Assert.Equal("Exact", m.Tags["s100.catalogue.match"]);
    }

    [Fact]
    public void Report_MajorDivergence_TagsAccordingly()
    {

        var scope = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements);

        CatalogueResolutionDiagnostics.Report(scope,
            new SpecRef("S-101", new SpecVersion(1, 2, 0)),
            new CatalogueRef("S-101", new SpecVersion(2, 0, 0)),
            "portrayal");

        var m = Assert.Single(measurements);
        Assert.Equal("MajorDivergence", m.Tags["s100.catalogue.match"]);
    }

    [Fact]
    public void Report_RepeatedSamePair_EmitsOnlyOnce()
    {

        var scope = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements);

        var spec = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var cat = new CatalogueRef("S-101", new SpecVersion(2, 0, 0));
        CatalogueResolutionDiagnostics.Report(scope, spec, cat, "portrayal");
        CatalogueResolutionDiagnostics.Report(scope, spec, cat, "portrayal");
        CatalogueResolutionDiagnostics.Report(scope, spec, cat, "portrayal");

        Assert.Single(measurements);
    }

    [Fact]
    public void Report_DistinctScopes_EmitIndependently()
    {

        var scopeA = new object();
        var scopeB = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements);

        var spec = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var cat = new CatalogueRef("S-101", new SpecVersion(2, 0, 0));
        CatalogueResolutionDiagnostics.Report(scopeA, spec, cat, "portrayal");
        CatalogueResolutionDiagnostics.Report(scopeB, spec, cat, "portrayal");

        Assert.Equal(2, measurements.Count);
    }
}
