using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>A variant made with <c>with</c>, as the generator docs show: no S-102 at all.</summary>
public sealed class ScenarioVariantTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-variant-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Without_S102_the_channel_charts_the_dredged_depth()
    {
        var definition = SyntheticScenarios.SpitAcrossScales() with
        {
            Name = "spit-without-s102",
            Surfaces = [],
        };
        var scenario = ScenarioGenerator.Generate(definition, _root);
        var sources = ScenarioLoading.Sources(scenario);

        var depth = sources.Depth!.Sample(definition.Place("channel-west"));

        Assert.Equal(ChartedDepthKind.DredgedArea, depth!.Kind);
        Assert.Equal(12.5, depth.DepthMeters);
        Assert.Equal("101ZZ00SPIT5", depth.DatasetId);
        Assert.True(definition.World.DepthAt(definition.Place("channel-west")) > depth.DepthMeters);
    }
}
