using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// <see cref="RuleProber"/> and <see cref="RuleProbeStore"/> (issue #1720,
/// symbol dictionary design note §7, R13a): each feature type's rule run
/// over the values it reads, and the file committed next to the bundled
/// S-101 portrayal catalogue.
/// </summary>
public sealed class RuleProberTests
{
    private static readonly Lazy<Task<RuleProbeSet>> LateralBeacon = new(() =>
        RuleProber.ProbeBundledAsync("S-101", Tracing(), new RuleProberOptions { FeatureTypes = ["LateralBeacon"] }));

    // Probing always runs on LuaCSharp, whichever engine the tests render with.
    private static ILuaEngine Tracing() => TestLuaEngine.Create(LuaEngineKind.LuaCSharp);

    [Theory]
    [InlineData("true", "BCNLAT21")]
    [InlineData("false", "BCNSTK02")]
    public async Task Probing_lateral_beacon_draws_a_red_stake_by_the_simplified_symbols_setting(string simplified, string symbol)
    {
        var probe = Assert.Single((await LateralBeacon.Value).FeatureTypes);
        Assert.Equal("LateralBeacon", probe.Code);
        Assert.False(probe.IsPartial);

        // A red (3) stake (beacon shape 1).
        var outcome = probe.OutcomeOf(Values(probe, ("PrimitiveType", "Point"), ("beaconShape", "1"), ("colour", "3"), ("SimplifiedSymbols", simplified)));

        Assert.NotNull(outcome);
        Assert.Contains($"symbol:{symbol}", outcome.Parts);
        Assert.False(outcome.IsError);
        Assert.Contains(27020, outcome.ViewingGroups);
    }

    [Fact]
    public async Task Probing_records_the_conditions_of_the_branch_taken_including_an_else()
    {
        var probe = Assert.Single((await LateralBeacon.Value).FeatureTypes);
        var index = await ChartReferenceFixture.GetAsync("S-101");
        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "LateralBeacon"), out var entry));
        var feature = Assert.IsType<FeatureTypeEntry>(entry);

        foreach (var (simplified, symbol) in new[] { ("true", "BCNLAT21"), ("false", "BCNSTK02") })
        {
            var outcome = probe.OutcomeOf(Values(probe, ("PrimitiveType", "Point"), ("beaconShape", "1"), ("colour", "3"), ("SimplifiedSymbols", simplified)))!;
            var held = outcome.Conditions.Select(c => probe.ConditionLines[c]).ToHashSet();

            // Exactly one of the rule's uses of the symbol has every condition held.
            var drawn = feature.Uses
                .Where(u => u.ItemId == symbol && u.RuleFile == "LateralBeacon")
                .Where(u => u.Conditions.All(c => held.Contains(new ProbeConditionLine(u.RuleFile, c.Line))))
                .ToList();
            Assert.Single(drawn);
        }

        // Paper chart symbols are in the else of 'if contextParameters.SimplifiedSymbols':
        // its line never runs, so the branch body's first line is the evidence.
        var paper = feature.Uses.First(u => u.ItemId == "BCNSTK02");
        var otherwise = paper.Conditions.Single(c => c is { Kind: RuleConditionKind.ContextParameter, Operator: ConditionOperator.IsFalse });
        var line = new ProbeConditionLine("LateralBeacon", otherwise.Line);
        var at = probe.ConditionLines.ToList().IndexOf(line);
        Assert.True(at >= 0, $"{line} isn't recorded.");
        var simplifiedOutcome = probe.OutcomeOf(Values(probe, ("PrimitiveType", "Point"), ("beaconShape", "1"), ("colour", "3"), ("SimplifiedSymbols", "true")))!;
        var paperOutcome = probe.OutcomeOf(Values(probe, ("PrimitiveType", "Point"), ("beaconShape", "1"), ("colour", "3"), ("SimplifiedSymbols", "false")))!;
        Assert.Contains(at, paperOutcome.Conditions);
        Assert.DoesNotContain(at, simplifiedOutcome.Conditions);
    }

    [Fact]
    public async Task Probing_tests_the_values_the_conditions_test_with_the_listed_colours_together()
    {
        var probe = Assert.Single((await LateralBeacon.Value).FeatureTypes);

        // The primitive first, then the inputs the conditions test most.
        Assert.Equal(ProbeInputKind.Primitive, probe.Inputs[0].Kind);
        var colour = probe.Inputs.Single(i => i.Code == "colour");
        Assert.Contains("3,4,3", colour.Values);
        Assert.Contains("4,3,4", colour.Values);
        Assert.Contains(null, colour.Values);
        Assert.Equal(["true", "false"], probe.Inputs.Single(i => i.Code == "SimplifiedSymbols").Values);
        Assert.Equal(probe.Inputs.Aggregate(1, (n, i) => n * i.Values.Count), probe.Variants.Count);
        Assert.Equal(probe.Variants.Count, probe.GetVariants().Count());
    }

    [Fact]
    public async Task The_cap_marks_a_feature_type_partial()
    {
        var set = await RuleProber.ProbeBundledAsync(
            "S-101", Tracing(), new RuleProberOptions { FeatureTypes = ["LateralBeacon"], Cap = 16 }, cancellationToken: TestContext.Current.CancellationToken);

        var probe = Assert.Single(set.FeatureTypes);
        Assert.True(probe.IsPartial);
        Assert.InRange(probe.Variants.Count, 1, 16);
        Assert.Equal(16, set.Cap);

        // The inputs the conditions test most keep their values longest.
        Assert.True(probe.Inputs.Single(i => i.Code == "beaconShape").Values.Count > 1);
    }

    [Fact]
    public async Task The_committed_S101_probe_file_matches_the_bundled_catalogues()
    {
        var committed = await ReadCommittedAsync("S-101");
        var (fc, pc) = await RuleProber.BundledHashesAsync("S-101", TestContext.Current.CancellationToken);

        Assert.Null(StalenessMessage(committed, fc, pc));
        Assert.NotEmpty(committed.FeatureTypes);
        Assert.NotNull(committed.Find("LateralBeacon"));
    }

    [Fact]
    public async Task The_staleness_check_catches_a_hash_mismatch()
    {
        var committed = await ReadCommittedAsync("S-101");
        var (fc, pc) = await RuleProber.BundledHashesAsync("S-101", TestContext.Current.CancellationToken);

        var message = StalenessMessage(committed, fc, new string('0', 64));

        Assert.NotNull(message);
        Assert.Contains("reference probe --spec S-101", message, StringComparison.Ordinal);
        Assert.Null(await RuleProbeStore.TryLoadBundledAsync("S-101", fc, new string('0', 64), TestContext.Current.CancellationToken));
        Assert.Null(await RuleProbeStore.TryLoadBundledAsync("S-101", new string('0', 64), pc, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Loading_a_matching_file_skips_probing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (fc, pc) = await RuleProber.BundledHashesAsync("S-101", ct);

        var probed = 0;
        var set = await RuleProbeStore.LoadOrProbeAsync("S-101", fc!, pc!, cacheDirectory: null, _ =>
        {
            probed++;
            throw new InvalidOperationException("Probed although the committed file matches.");
        }, ct);

        Assert.NotNull(set);
        Assert.Equal(0, probed);
        Assert.True(set.Matches(fc, pc));
    }

    [Fact]
    public async Task A_changed_catalogue_is_probed_once_and_then_read_from_the_disk_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = Directory.CreateTempSubdirectory("rule-probes-").FullName;
        try
        {
            var fc = new string('a', 64);
            var pc = new string('b', 64);
            var probed = 0;
            Task<RuleProbeSet> Probe(CancellationToken _)
            {
                probed++;
                return Task.FromResult(new RuleProbeSet { Spec = "S-101", FeatureCatalogueHash = fc, PortrayalCatalogueHash = pc });
            }

            var first = await RuleProbeStore.LoadOrProbeAsync("S-101", fc, pc, cache, Probe, ct);
            var second = await RuleProbeStore.LoadOrProbeAsync("S-101", fc, pc, cache, Probe, ct);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(1, probed);
            Assert.True(File.Exists(RuleProbeStore.CachePath(cache, "S-101", fc, pc)));
            Assert.Null(RuleProbeStore.TryLoadCached(cache, "S-101", fc, new string('c', 64)));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task The_store_writes_the_same_bytes_it_reads()
    {
        var set = await LateralBeacon.Value;
        using var first = new MemoryStream();
        RuleProbeStore.Write(set, first);
        first.Position = 0;
        var read = RuleProbeStore.Read(first);
        using var second = new MemoryStream();
        RuleProbeStore.Write(read, second);

        Assert.Equal(first.ToArray(), second.ToArray());
        var probe = Assert.Single(read.FeatureTypes);
        Assert.Equal(set.FeatureTypes[0].Variants, probe.Variants);
        Assert.Equal(set.FeatureTypes[0].Inputs.Select(i => i.Values), probe.Inputs.Select(i => i.Values));
    }

    [Fact]
    public async Task The_index_carries_probes_attached_to_it()
    {
        var index = await ChartReferenceFixture.GetAsync("S-101");
        var set = await LateralBeacon.Value;

        var withProbes = index.WithProbes(set);

        Assert.Null(index.Probes);
        Assert.Same(set, withProbes.Probes);
        Assert.Same(index.FeatureTypes, withProbes.FeatureTypes);
        Assert.NotEmpty(withProbes.Search("lateral beacon"));
        Assert.Throws<ArgumentException>(() => index.WithProbes(new RuleProbeSet { Spec = "S-131", FeatureCatalogueHash = "a", PortrayalCatalogueHash = "b" }));
    }

    [Theory]
    [InlineData(new[] { "if a then", "  x()", "end" }, 1, 2)]
    [InlineData(new[] { "if a and", "   b then", "", "  -- note", "  x()", "end" }, 1, 5)]
    [InlineData(new[] { "if a then", "  x()", "else", "  y()", "end" }, 3, 4)]
    [InlineData(new[] { "if a then x() end" }, 1, 0)]
    [InlineData(new[] { "if a then", "end" }, 1, 0)]
    [InlineData(new[] { "if s == '--then' then", "  x()", "end" }, 1, 2)]
    public void The_evidence_of_a_condition_is_the_first_line_of_its_branch(string[] lines, int condition, int evidence)
    {
        Assert.Equal(evidence, RuleProber.EvidenceLine(lines, condition));
    }

    /// <summary>The staleness test's message, or <see langword="null"/> when the committed file is current.</summary>
    internal static string? StalenessMessage(RuleProbeSet committed, string? featureCatalogueHash, string? portrayalCatalogueHash) =>
        committed.Matches(featureCatalogueHash, portrayalCatalogueHash)
            ? null
            : $"src/EncDotNet.S100.Specifications/content/S101/pc/{RuleProbeStore.FileName} was probed from other catalogues "
              + $"(feature catalogue {committed.FeatureCatalogueHash}, portrayal catalogue {committed.PortrayalCatalogueHash}; "
              + $"bundled: {featureCatalogueHash}, {portrayalCatalogueHash}). "
              + $"Re-run 's100 reference probe --spec {committed.Spec}' and commit the file.";

    private static async Task<RuleProbeSet> ReadCommittedAsync(string spec)
    {
        using var source = Specification.CreatePortrayalCatalogueSource(spec);
        await using var stream = await source.OpenAsync(RuleProbeStore.FileName, TestContext.Current.CancellationToken);
        return RuleProbeStore.Read(stream);
    }

    private static int[] Values(FeatureTypeProbe probe, params (string Code, string? Value)[] chosen)
    {
        var values = new int[probe.Inputs.Count];
        for (var i = 0; i < probe.Inputs.Count; i++)
        {
            var input = probe.Inputs[i];
            var match = chosen.Where(c => c.Code == input.Code).ToList();

            // Inputs not named are left out of the feature (no value).
            var wanted = match.Count > 0 ? match[0].Value : input.Kind == ProbeInputKind.Attribute ? null : input.Values[0];
            values[i] = input.Values.ToList().IndexOf(wanted);
            Assert.True(values[i] >= 0, $"{input.Code} wasn't probed with {wanted ?? "no value"}.");
        }

        return values;
    }
}
