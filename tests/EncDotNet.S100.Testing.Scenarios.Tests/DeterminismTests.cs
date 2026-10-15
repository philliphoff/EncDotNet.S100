using System.Security.Cryptography;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>The same seed and scenario give byte-identical files.</summary>
public sealed class DeterminismTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-determinism-" + Guid.NewGuid().ToString("N"));

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
    public void Two_runs_with_the_same_seed_write_identical_files()
    {
        var first = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.SpitAcrossScales(seed: 7), Path.Combine(_root, "a")));
        var second = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.SpitAcrossScales(seed: 7), Path.Combine(_root, "b")));

        Assert.Equal(6, first.Count);
        Assert.Equal(first, second);
    }

    [Fact]
    public void A_different_seed_changes_the_seeded_content()
    {
        var a = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.SpitAcrossScales(seed: 1), Path.Combine(_root, "a")));
        var b = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.SpitAcrossScales(seed: 2), Path.Combine(_root, "b")));

        Assert.Equal(a.Keys, b.Keys);
        Assert.NotEqual(a["S-101/DATASET_FILES/101ZZ00SPIT5.000"], b["S-101/DATASET_FILES/101ZZ00SPIT5.000"]);
        Assert.NotEqual(a["S-104/DATASET_FILES/104ZZ00SPIT_STN.h5"], b["S-104/DATASET_FILES/104ZZ00SPIT_STN.h5"]);
    }

    [Fact]
    public void Seeded_sequences_are_fixed_by_the_generator()
    {
        // SplitMix64 from seed 0: the first outputs are part of the format.
        var random = new ScenarioRandom(0);

        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
    }

    private static SortedDictionary<string, string> Hashes(GeneratedScenario scenario)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(scenario.Root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(scenario.Root, file).Replace('\\', '/');
            hashes[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return hashes;
    }
}
