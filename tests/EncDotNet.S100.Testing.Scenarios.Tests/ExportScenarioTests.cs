using EncDotNet.S100.Testing.Scenarios.Protection;
using EncDotNet.S100.Testing.Scenarios.Quirks;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// Writes the scenarios to a folder for a look in the viewer or <c>s100</c>
/// CLI. Runs only when <c>S100_SCENARIO_OUT</c> names the folder.
/// </summary>
public sealed class ExportScenarioTests
{
    private const string Variable = "S100_SCENARIO_OUT";

    [Fact]
    public void Writes_the_scenarios_when_asked()
    {
        var root = Environment.GetEnvironmentVariable(Variable);
        Assert.SkipWhen(string.IsNullOrEmpty(root), $"Set {Variable} to a folder to write the scenarios there.");

        var definitions = new[]
        {
            SyntheticScenarios.SpitAcrossScales(),
            SyntheticScenarios.SpitAcrossScales(declareS102VerticalDatum: false),
            SyntheticScenarios.SpitCrossing(),
            SyntheticScenarios.DangersNearRoute(),
            SyntheticScenarios.ApproachWithTideWindow(),
            SyntheticScenarios.WarningsOnRoute(),
            SyntheticScenarios.WarningsOnRoute(s124Edition: 1),
            SyntheticScenarios.PortPlanVsOwnRoute(),
        };
        foreach (var definition in definitions.Concat(QuirkScenarios.All()))
        {
            var scenario = ScenarioGenerator.Generate(definition, Path.Combine(root!, definition.Name));
            Assert.True(File.Exists(scenario.CataloguePath));
        }

        foreach (var (data, protection) in ProtectedScenarios.CheckedIn())
        {
            using var protectedSet = ProtectedScenarioGenerator.Generate(data, protection, Path.Combine(root!, "protected", protection.Name));
            Assert.True(File.Exists(protectedSet.PermitPath));
        }
    }

    // Regenerates the checked-in Protected-* sets (#768): set the variable to
    // tests/datasets/ExchangeSets and run this test.
    [Fact]
    public void Writes_the_checked_in_protected_sets_when_asked()
    {
        const string fixtures = "S100_PROTECTED_FIXTURES_OUT";
        var root = Environment.GetEnvironmentVariable(fixtures);
        Assert.SkipWhen(string.IsNullOrEmpty(root), $"Set {fixtures} to tests/datasets/ExchangeSets to regenerate the Protected-* sets.");

        foreach (var (data, protection) in ProtectedScenarios.CheckedIn())
        {
            var folder = Path.Combine(root!, protection.Name);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            using var protectedSet = ProtectedScenarioGenerator.Generate(data, protection, folder);
            Assert.True(File.Exists(protectedSet.PermitPath));
        }
    }
}
