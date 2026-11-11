using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Tests;

/// <summary>
/// Issue #189 regression guard: the <c>EncDotNet.S100</c> facade exposes a
/// Mapsui-free public API and must not acquire Mapsui transitively.
/// </summary>
public class FacadeMapsuiDecouplingTests
{
    [Fact]
    public void Facade_assembly_has_no_Mapsui_in_its_reference_closure()
    {
        var facadePath = typeof(S100Dataset).Assembly.Location;

        var offenders = MapsuiDependencyClosure.FindMapsuiReferences(facadePath);

        Assert.True(
            offenders.Count == 0,
            $"The EncDotNet.S100 facade must not reference Mapsui, but its closure reaches: {string.Join(", ", offenders)}");
    }
}
