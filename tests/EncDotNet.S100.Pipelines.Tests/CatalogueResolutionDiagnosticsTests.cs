using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Diagnostics;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class CatalogueResolutionDiagnosticsTests
{
    private sealed record Measurement(long Value, IReadOnlyDictionary<string, object?> Tags);

    private static MeterListener StartCapture(List<Measurement> sink, string catalogueKind)
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

            // The counter is process-wide, so measurements emitted by other
            // test classes running in parallel (e.g. real dataset processors)
            // also reach this listener. Filter to this test's unique catalogue
            // kind so the assertions only see measurements this test provoked.
            if (dict.TryGetValue("s100.catalogue.kind", out var kind) && Equals(kind, catalogueKind))
            {
                sink.Add(new Measurement(value, dict));
            }
        });
        listener.Start();
        return listener;
    }

    [Fact]
    public void Report_NullCatalogueRef_DoesNothing()
    {
        var kind = UniqueKind();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements, kind);

        CatalogueResolutionDiagnostics.Report(this, new SpecRef("S-101", new SpecVersion(1, 2, 0)),
            null, kind);

        Assert.Empty(measurements);
    }

    [Fact]
    public void Report_ExactMatch_EmitsCounterWithExactTag()
    {
        var kind = UniqueKind();
        var scope = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements, kind);

        CatalogueResolutionDiagnostics.Report(scope,
            new SpecRef("S-101", new SpecVersion(1, 2, 0)),
            new CatalogueRef("S-101", new SpecVersion(1, 2, 0)),
            kind);

        var m = Assert.Single(measurements);
        Assert.Equal(1L, m.Value);
        Assert.Equal("S-101", m.Tags["s100.spec.name"]);
        Assert.Equal("1.2.0", m.Tags["s100.spec.edition"]);
        Assert.Equal("1.2.0", m.Tags["s100.catalogue.version"]);
        Assert.Equal(kind, m.Tags["s100.catalogue.kind"]);
        Assert.Equal("Exact", m.Tags["s100.catalogue.match"]);
    }

    [Fact]
    public void Report_MajorDivergence_TagsAccordingly()
    {
        var kind = UniqueKind();
        var scope = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements, kind);

        CatalogueResolutionDiagnostics.Report(scope,
            new SpecRef("S-101", new SpecVersion(1, 2, 0)),
            new CatalogueRef("S-101", new SpecVersion(2, 0, 0)),
            kind);

        var m = Assert.Single(measurements);
        Assert.Equal("MajorDivergence", m.Tags["s100.catalogue.match"]);
    }

    [Fact]
    public void Report_RepeatedSamePair_EmitsOnlyOnce()
    {
        var kind = UniqueKind();
        var scope = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements, kind);

        var spec = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var cat = new CatalogueRef("S-101", new SpecVersion(2, 0, 0));
        CatalogueResolutionDiagnostics.Report(scope, spec, cat, kind);
        CatalogueResolutionDiagnostics.Report(scope, spec, cat, kind);
        CatalogueResolutionDiagnostics.Report(scope, spec, cat, kind);

        Assert.Single(measurements);
    }

    [Fact]
    public void Report_DistinctScopes_EmitIndependently()
    {
        var kind = UniqueKind();
        var scopeA = new object();
        var scopeB = new object();
        var measurements = new List<Measurement>();
        using var listener = StartCapture(measurements, kind);

        var spec = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var cat = new CatalogueRef("S-101", new SpecVersion(2, 0, 0));
        CatalogueResolutionDiagnostics.Report(scopeA, spec, cat, kind);
        CatalogueResolutionDiagnostics.Report(scopeB, spec, cat, kind);

        Assert.Equal(2, measurements.Count);
    }

    private static string UniqueKind() => "portrayal-" + Guid.NewGuid().ToString("N");
}
