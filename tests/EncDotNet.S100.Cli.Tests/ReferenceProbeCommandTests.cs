using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 reference probe</c> (issue #1720): probes the bundled S-101
/// rules and writes the results keyed by the catalogue hashes.
/// </summary>
public sealed class ReferenceProbeCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("reference-probe-").FullName;

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
    public async Task Probe_writes_the_chosen_feature_types_keyed_by_the_bundled_hashes()
    {
        var output = Path.Combine(_root, "probes.json.gz");

        var exit = CliApp.Build().Run(
            ["reference", "probe", "--spec", "S-101", "--feature-type", "LateralBeacon", "-o", output],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        var set = RuleProbeStore.ReadFile(output);
        var (fc, pc) = await RuleProber.BundledHashesAsync("S-101", TestContext.Current.CancellationToken);
        Assert.True(set.Matches(fc, pc));
        var probe = Assert.Single(set.FeatureTypes);
        Assert.Equal("LateralBeacon", probe.Code);
        Assert.Contains(probe.Outcomes, o => o.Parts.Contains("symbol:BCNLAT21"));
    }

    [Fact]
    public void Probe_refuses_a_spec_it_cannot_probe()
    {
        var exit = CliApp.Build().Run(
            ["reference", "probe", "--spec", "S-124", "-o", Path.Combine(_root, "x.json")],
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(Path.Combine(_root, "x.json")));
    }

    [Fact]
    public void The_default_output_is_next_to_the_bundled_portrayal_catalogue()
    {
        var output = ReferenceProbeCommand.DefaultOutput("S-101");

        // Tests run inside the repository.
        Assert.NotNull(output);
        Assert.EndsWith(
            Path.Combine("src", "EncDotNet.S100.Specifications", "content", "S101", "pc", RuleProbeStore.FileName),
            output,
            StringComparison.Ordinal);
    }
}
