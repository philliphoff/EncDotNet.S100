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

    public static TheoryData<string> Scenarios() => new(ScenariosByName.Keys);

    // Each product's files hash the same on a second run (#942: per product).
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_product_is_byte_identical_on_a_second_run(string name)
    {
        var first = Hashes(ScenarioGenerator.Generate(ScenariosByName[name](7), Path.Combine(_root, name, "a")));
        var second = Hashes(ScenarioGenerator.Generate(ScenariosByName[name](7), Path.Combine(_root, name, "b")));

        Assert.Equal(first, second);
        var products = first.Keys.Where(k => k != "CATALOG.XML").Select(k => k[..k.IndexOf('/', StringComparison.Ordinal)]).Distinct();
        Assert.Equal(ExpectedProducts[name], products.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_new_products_follow_the_seed_where_they_sample_it()
    {
        var a = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.PortPlanVsOwnRoute(seed: 1), Path.Combine(_root, "a")));
        var b = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.PortPlanVsOwnRoute(seed: 2), Path.Combine(_root, "b")));

        // The plan samples the seeded relief and tide phases.
        Assert.NotEqual(a["S-129/DATASET_FILES/129ZZ00PORT.gml"], b["S-129/DATASET_FILES/129ZZ00PORT.gml"]);

        // Routes are fixed geography and times: the seed doesn't move them.
        Assert.Equal(a["S-421/DATASET_FILES/421ZZ00OWN.gml"], b["S-421/DATASET_FILES/421ZZ00OWN.gml"]);

        var c = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.ApproachWithTideWindow(seed: 1), Path.Combine(_root, "c")));
        var d = Hashes(ScenarioGenerator.Generate(SyntheticScenarios.ApproachWithTideWindow(seed: 2), Path.Combine(_root, "d")));
        Assert.NotEqual(c["S-111/DATASET_FILES/111ZZ00APPR_GRD.h5"], d["S-111/DATASET_FILES/111ZZ00APPR_GRD.h5"]);
    }

    private static readonly Dictionary<string, Func<ulong, ScenarioDefinition>> ScenariosByName = new()
    {
        ["approach-with-tide-window"] = seed => SyntheticScenarios.ApproachWithTideWindow(seed),
        ["warnings-on-route"] = seed => SyntheticScenarios.WarningsOnRoute(seed),
        ["warnings-on-route-s124-ed1"] = seed => SyntheticScenarios.WarningsOnRoute(seed, s124Edition: 1),
        ["port-plan-vs-own-route"] = seed => SyntheticScenarios.PortPlanVsOwnRoute(seed),
    };

    private static readonly Dictionary<string, string[]> ExpectedProducts = new()
    {
        ["approach-with-tide-window"] = ["S-101", "S-102", "S-104", "S-111", "S-421"],
        ["warnings-on-route"] = ["S-101", "S-124", "S-421"],
        ["warnings-on-route-s124-ed1"] = ["S-101", "S-124", "S-421"],
        ["port-plan-vs-own-route"] = ["S-101", "S-102", "S-104", "S-129", "S-421"],
    };

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
