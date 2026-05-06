using System.Collections.Generic;
using System.Diagnostics;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Smoke tests for the observability instrumentation added across
/// the libraries. We don't assert exact span trees (those depend on
/// runtime data); we just verify that the named ActivitySources are
/// reachable by an <see cref="ActivityListener"/> and that a happy-path
/// call produces at least one activity with the expected name.
/// </summary>
public sealed class TelemetrySmokeTests
{
    [Fact]
    public void FeatureCatalogueReader_emits_parse_activity()
    {
        var observed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EncDotNet.S100.Features",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new System.InvalidOperationException("S-101 feature catalogue not found.");
        _ = FeatureCatalogueReader.Read(fcStream);

        Assert.Contains(observed, a => a.OperationName == "s100.featurecatalogue.parse");
    }

    [Fact]
    public void All_libraries_register_named_activity_sources()
    {
        // Touch each library's Telemetry indirectly via a public type
        // to ensure the assembly is loaded before we ask the listener.
        _ = typeof(EncDotNet.S100.Diagnostics.S100Telemetry);
        _ = typeof(EncDotNet.S100.Features.FeatureCatalogueReader);
        _ = typeof(EncDotNet.S100.Specifications.Specification);

        var seen = new HashSet<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src =>
            {
                if (src.Name.StartsWith("EncDotNet.S100", System.StringComparison.Ordinal))
                {
                    seen.Add(src.Name);
                }
                return false;
            },
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(listener);

        // Force a parse so at least the Features ActivitySource is queried.
        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new System.InvalidOperationException("S-101 feature catalogue not found.");
        _ = FeatureCatalogueReader.Read(fcStream);

        Assert.Contains("EncDotNet.S100.Features", seen);
    }
}
