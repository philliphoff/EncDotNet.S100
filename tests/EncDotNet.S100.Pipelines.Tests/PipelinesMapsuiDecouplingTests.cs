using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #189 regression guard: the packable
/// <c>EncDotNet.S100.Datasets.Pipelines</c> assembly must be Mapsui-free.
/// </summary>
public class PipelinesMapsuiDecouplingTests
{
    [Fact]
    public void Pipelines_assembly_has_no_Mapsui_in_its_reference_closure()
    {
        var pipelinesPath = typeof(DatasetPipelineFactory).Assembly.Location;

        var offenders = MapsuiDependencyClosure.FindMapsuiReferences(pipelinesPath);

        Assert.True(
            offenders.Count == 0,
            $"EncDotNet.S100.Datasets.Pipelines must not reference Mapsui, but its closure reaches: {string.Join(", ", offenders)}");
    }
}
