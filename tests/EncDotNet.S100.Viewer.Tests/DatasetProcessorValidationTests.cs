using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// End-to-end smoke tests that the spec-specific dataset processors
/// surface a non-null <see cref="EncDotNet.S100.Validation.ValidationReport"/>
/// from <see cref="IDatasetProcessor.Validate"/> when a rule pack is
/// defined for the spec. These exercise the wire-up — raw GML →
/// typed projection → rule pack — without asserting on the contents of
/// the report itself (those are covered by the per-spec rule tests).
/// </summary>
public class DatasetProcessorValidationTests
{
    private static string TestData(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "TestData" }.Concat(parts).ToArray());

    private static PortrayalCatalogueManager CreateCatalogueManager(string spec)
    {
        var manager = new PortrayalCatalogueManager();
        manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        return manager;
    }

    [Fact]
    public void S125Processor_Validate_ReturnsNonNullReport()
    {
        var path = TestData("S125", "aton_point.gml");
        Assert.True(File.Exists(path), $"Missing fixture: {path}");

        var processor = new S125DatasetProcessor(path, CreateCatalogueManager("S-125"), TestAuthority.NewS98Provider());

        var report = processor.Validate();

        Assert.NotNull(report);
        Assert.True(report!.RulesEvaluated > 0,
            "S-125 rule pack should report at least one rule evaluated");
    }

    [Fact]
    public void S125Processor_Validate_IsCachedAcrossCalls()
    {
        var path = TestData("S125", "aton_point.gml");
        var processor = new S125DatasetProcessor(path, CreateCatalogueManager("S-125"), TestAuthority.NewS98Provider());

        var first = processor.Validate();
        var second = processor.Validate();

        // Same instance — confirms the processor caches and never
        // re-runs validation on subsequent calls.
        Assert.Same(first, second);
    }
}
