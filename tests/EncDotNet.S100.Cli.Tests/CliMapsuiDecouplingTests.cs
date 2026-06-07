using System;
using System.IO;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #189 regression guard: the <c>s100</c> CLI uses the headless render
/// path and must not acquire Mapsui transitively.
/// </summary>
public class CliMapsuiDecouplingTests
{
    [Fact]
    public void Cli_assembly_has_no_Mapsui_in_its_reference_closure()
    {
        // The CLI assembly name is "s100" (see EncDotNet.S100.Cli.csproj); its
        // types are internal, so it is located by file rather than by a type.
        var cliPath = Path.Combine(AppContext.BaseDirectory, "s100.dll");
        Assert.True(File.Exists(cliPath), $"Expected CLI assembly at {cliPath}");

        var offenders = MapsuiDependencyClosure.FindMapsuiReferences(cliPath);

        Assert.True(
            offenders.Count == 0,
            $"The s100 CLI must not reference Mapsui, but its closure reaches: {string.Join(", ", offenders)}");
    }
}
