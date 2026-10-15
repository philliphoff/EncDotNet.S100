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

        foreach (var definition in new[] { SyntheticScenarios.SpitAcrossScales(), SyntheticScenarios.SpitAcrossScales(declareS102VerticalDatum: false) })
        {
            var scenario = ScenarioGenerator.Generate(definition, Path.Combine(root!, definition.Name));
            Assert.True(File.Exists(scenario.CataloguePath));
        }
    }
}
