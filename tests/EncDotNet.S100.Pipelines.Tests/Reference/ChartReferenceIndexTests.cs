using System.Diagnostics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// <see cref="ChartReferenceIndex"/> against the bundled catalogues. The
/// floors are the Chart reference prototype's numbers (design note
/// <c>docs/design/symbol-dictionary.md</c> §2, issue #1634).
/// </summary>
public class ChartReferenceIndexTests
{
    private static Task<ChartReferenceIndex> S101() => ChartReferenceFixture.GetAsync("S-101");

    private static FeatureTypeEntry Feature(ChartReferenceIndex index, string code)
    {
        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, code), out var entry));
        return Assert.IsType<FeatureTypeEntry>(entry);
    }

    private static PortrayalItemEntry Item(ChartReferenceIndex index, ReferenceKind kind, string code)
    {
        Assert.True(index.TryGet(new ReferenceKey(index.Spec, kind, code), out var entry));
        return Assert.IsType<PortrayalItemEntry>(entry);
    }

    [Fact]
    public async Task S101_links_at_least_152_of_190_feature_types()
    {
        var index = await S101();

        var concrete = index.FeatureTypes.Where(f => !f.IsAbstract).ToList();
        Assert.Equal(190, concrete.Count);
        Assert.True(concrete.Count(f => f.IsPortrayed) >= 152, $"{concrete.Count(f => f.IsPortrayed)} linked");
    }

    [Fact]
    public async Task S101_lateral_beacon_uses_BCNLAT21_when_simplified_and_red()
    {
        var index = await S101();
        var beacon = Feature(index, "LateralBeacon");

        Assert.Equal("BCNLAT", beacon.Alias);
        Assert.Equal("LateralBeacon", beacon.RuleFile);
        var uses = beacon.Uses.Where(u => u.ItemId == "BCNLAT21").ToList();
        Assert.NotEmpty(uses);
        Assert.All(uses, u => Assert.Null(u.ViaRule));
        Assert.Contains(uses, u =>
            u.Conditions.Any(c => c is { Kind: RuleConditionKind.ContextParameter, Subject: "SimplifiedSymbols", Operator: ConditionOperator.IsTrue })
            && u.Conditions.Any(c => c is { Kind: RuleConditionKind.Attribute, Subject: "colour", Index: 1, Operator: ConditionOperator.Equal, Value: "3" })
            && u.Conditions.Any(c => c is { Kind: RuleConditionKind.Primitive, Value: "Point" }));

        // The paper-chart symbols sit in the else of the Simplified symbols test.
        var paper = Assert.Single(beacon.Uses, u => u.ItemId == "BCNSTK02");
        Assert.Contains(paper.Conditions, c => c is { Subject: "SimplifiedSymbols", Operator: ConditionOperator.IsFalse });

        // The colour listed value 3 is red, so the condition reads "colour is red".
        var colour = Assert.IsType<AttributeEntry>(
            index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Attribute, "colour"), out var c) ? c : null);
        Assert.Equal("red", colour.ListedValues.Single(v => v.Code == "3").Label, ignoreCase: true);
        Assert.Contains("LateralBeacon", colour.ConditionedFeatureTypes);

        var symbol = Item(index, ReferenceKind.Symbol, "BCNLAT21");
        Assert.Contains("LateralBeacon", symbol.UsedBy);
        Assert.Equal("minor lateral beacon, red, simplified", symbol.Definition);
        Assert.Equal("S52Preslib4.0", symbol.Source);
        Assert.False(symbol.IsUnreached);
    }

    [Fact]
    public async Task S101_depth_area_fills_come_by_way_of_DEPARE03_and_SEABED01()
    {
        var index = await S101();
        var depthArea = Feature(index, "DepthArea");

        foreach (var token in new[] { "DEPVS", "DEPMS", "DEPMD", "DEPDW" })
        {
            var fill = depthArea.ColorFills.First(f => f.Token == token);
            Assert.Equal("DEPARE03", fill.ViaRule);
            Assert.Equal("SEABED01", fill.RuleFile);
            Assert.Equal(["DEPARE03", "SEABED01"], fill.RulePath);
            Assert.True(fill.Line > 0);
        }

        // DEPMS is only drawn with four shades on.
        Assert.All(
            depthArea.ColorFills.Where(f => f.Token == "DEPMS"),
            f => Assert.Contains(f.Conditions, c => c is { Subject: "FourShades", Operator: ConditionOperator.IsTrue }));

        // The rule's own fill for an area with no depth range.
        Assert.Contains(depthArea.ColorFills, f => f is { Token: "NODTA", ViaRule: null });

        // DEPARE03 draws DRGARE01 only for Dredged Area.
        Assert.DoesNotContain(depthArea.Uses, u => u.ItemId == "DRGARE01");
        Assert.Contains(Feature(index, "DredgedArea").Uses, u => u is { ItemId: "DRGARE01", ViaRule: "DEPARE03" });
    }

    [Fact]
    public async Task S101_assembled_families_are_one_entry_each()
    {
        var index = await S101();

        foreach (var prefix in new[] { "SOUNDG", "SOUNDS", "SAFCON" })
        {
            var family = Item(index, ReferenceKind.Symbol, prefix);
            Assert.True(family.IsFamily);
            Assert.True(family.Members.Count >= 50, $"{prefix} has {family.Members.Count} members");
            Assert.NotEmpty(family.UsedBy);
            Assert.All(family.Members, m => Assert.Equal(prefix, Item(index, ReferenceKind.Symbol, m).Family));
        }

        var sounding = Feature(index, "Sounding");
        Assert.Contains(sounding.Uses, u => u is { ItemId: "SOUNDG", IsFamily: true, ViaRule: "SOUNDG03" });
        Assert.Contains("Sounding", Item(index, ReferenceKind.Symbol, "SOUNDG12").UsedBy);
        Assert.Contains(Feature(index, "DepthContour").Uses, u => u is { ItemId: "SAFCON", IsFamily: true });

        // A rule named like a symbol (SOUNDG03, SAFCON01) isn't a use of that symbol.
        Assert.DoesNotContain(sounding.Uses, u => u is { ItemId: "SOUNDG03", IsFamily: false });
    }

    [Fact]
    public async Task S101_items_no_rule_names_are_marked_unreached()
    {
        var index = await S101();

        var unreached = index.Items.Where(i => i.IsUnreached).ToList();
        Assert.NotEmpty(unreached);
        Assert.All(unreached, i => Assert.Empty(i.UsedBy));
        Assert.DoesNotContain(unreached, i => i.Family is not null);
    }

    [Fact]
    public async Task S101_viewing_groups_give_display_category_and_chart1_section()
    {
        var index = await S101();

        var beacon = Feature(index, "LateralBeacon").PrimaryViewingGroup;
        Assert.NotNull(beacon);
        Assert.Equal("27020", beacon.Id);
        Assert.Equal("StandardDisplay", beacon.DisplayModeId);
        Assert.Contains("Buoys and Beacons", beacon.Description);

        Assert.Equal("DisplayBase", Feature(index, "DepthArea").PrimaryViewingGroup?.DisplayModeId);
        Assert.Equal("OtherInformation", Feature(index, "Sounding").PrimaryViewingGroup?.DisplayModeId);

        // D6: some rules set different viewing groups by geometry or case.
        Assert.Contains(index.FeatureTypes, f =>
            f.ViewingGroups.Where(v => v.ViaRule is null).Select(v => v.ViewingGroup.DisplayModeId).Distinct().Count() > 1);
    }

    [Fact]
    public async Task S101_display_settings_list_the_feature_types_that_branch_on_them()
    {
        var index = await S101();

        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, "SimplifiedSymbols"), out var entry));
        var setting = Assert.IsType<DisplaySettingEntry>(entry);
        Assert.Equal("Boolean", setting.Type, ignoreCase: true);
        Assert.Contains("LateralBeacon", setting.ConditionedFeatureTypes);

        // A setting tested only inside an expression (SEABED01) counts too.
        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, "SafetyContour"), out var contour));
        Assert.Contains("DepthArea", Assert.IsType<DisplaySettingEntry>(contour).ConditionedFeatureTypes);
    }

    [Fact]
    public async Task S101_search_ranks_code_then_name_prefix_then_text()
    {
        var index = await S101();

        var byAcronym = index.Search("bcnlat");
        Assert.Equal("LateralBeacon", byAcronym[0].Entry.Code);
        Assert.Equal(ReferenceMatch.ExactCode, byAcronym[0].Match);
        Assert.Contains(byAcronym, h => h is { Match: ReferenceMatch.NamePrefix, Entry.Code: "BCNLAT21" });

        var byName = index.Search("Lateral", ReferenceKind.FeatureType);
        Assert.Equal(["LateralBeacon", "LateralBuoy"], byName.Take(2).Select(h => h.Entry.Code));
        Assert.All(byName.Take(2), h => Assert.Equal(ReferenceMatch.NamePrefix, h.Match));
        Assert.Equal(byName.Select(h => h.Match).Order(), byName.Select(h => h.Match));

        // "red" is a value the lateral beacon's conditions test.
        Assert.Contains(index.Search("red", ReferenceKind.FeatureType), h => h is { Entry.Code: "LateralBeacon", Match: ReferenceMatch.Text });

        Assert.Equal(3, index.Search("beacon", limit: 3).Count);
        Assert.Empty(index.Search("   "));
    }

    [Fact]
    public async Task TryGet_matches_codes_without_regard_to_case_and_checks_the_spec()
    {
        var index = await S101();

        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "lateralbeacon"), out _));
        Assert.False(index.TryGet(new ReferenceKey(new SpecRef("S-401", default), ReferenceKind.FeatureType, "LateralBeacon"), out _));
        Assert.False(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Symbol, "LateralBeacon"), out _));
    }

    [Fact]
    public async Task S201_xslt_templates_link_symbols_with_their_match_predicate()
    {
        var index = await ChartReferenceFixture.GetAsync("S-201");

        Assert.True(index.FeatureTypes.Count(f => f.IsPortrayed) >= 22);
        var beacon = Feature(index, "LateralBeacon");
        var stake = Assert.Single(beacon.Uses, u => u.ItemId == "BCNSTK02");
        Assert.Equal("BeaconLateral_PAPER_CHART", stake.RuleFile);
        Assert.Contains(stake.Conditions, c => c is { Kind: RuleConditionKind.Primitive, Value: "Point" });
        Assert.Contains(stake.Conditions, c => c is { Kind: RuleConditionKind.Attribute, Subject: "beaconShape", Value: "1" });
        Assert.Equal("27020", beacon.PrimaryViewingGroup?.Id);

        // Topmark draws through a named template in another file.
        Assert.Contains(Feature(index, "Topmark").Uses, u => u.ViaRule == "FloatingTopmark");
    }

    [Fact]
    public async Task S421_follows_xsl_include_to_stylesheets_the_catalogue_misnames()
    {
        var index = await ChartReferenceFixture.GetAsync("S-421");

        Assert.Contains(Feature(index, "RouteWaypoint").Uses, u => u.ItemId == "RTEWPT01");
    }

    [Theory]
    [InlineData("S-401", 124)]
    [InlineData("S-131", 28)]
    [InlineData("S-127", 20)]
    [InlineData("S-124", 2)]
    [InlineData("S-122", 3)]
    public async Task Other_products_link_at_least_the_prototype_numbers(string spec, int floor)
    {
        var index = await ChartReferenceFixture.GetAsync(spec);

        var linked = index.FeatureTypes.Count(f => !f.IsAbstract && f.Uses.Count > 0);
        Assert.True(linked >= floor, $"{spec}: {linked} feature types link a symbol (floor {floor})");
    }

    [Theory]
    [InlineData("S-102")]
    [InlineData("S-111")]
    [InlineData("S-125")]
    [InlineData("S-128")]
    [InlineData("S-411")]
    public async Task Every_bundled_catalogue_builds(string spec)
    {
        var index = await ChartReferenceFixture.GetAsync(spec);

        Assert.Equal(spec, index.Spec.Name);
        Assert.NotEmpty(index.Items.Concat<ReferenceEntry>(index.FeatureTypes));
    }

    [Fact]
    public async Task Builds_without_a_feature_catalogue_from_the_rule_files()
    {
        var (_, portrayal) = await ChartReferenceFixture.OpenAsync("S-101", TestContext.Current.CancellationToken);
        using (portrayal)
        {
            var index = await ChartReferenceIndex.BuildAsync(
                new SpecRef("S-101", default), null, portrayal, TestContext.Current.CancellationToken);

            Assert.Empty(index.Attributes);
            Assert.Contains(index.FeatureTypes, f => f.Code == "LateralBeacon" && f.Uses.Any(u => u.ItemId == "BCNLAT21"));
        }
    }

    [Fact]
    public async Task S101_build_time()
    {
        var (features, portrayal) = await ChartReferenceFixture.OpenAsync("S-101", TestContext.Current.CancellationToken);
        using (portrayal)
        {
            var spec = new SpecRef("S-101", default);

            // The first build in a process pays for JIT; the budget is for a warm build.
            var first = Stopwatch.StartNew();
            await ChartReferenceIndex.BuildAsync(spec, features, portrayal, TestContext.Current.CancellationToken);
            var cold = first.Elapsed.TotalMilliseconds;
            var timings = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                await ChartReferenceIndex.BuildAsync(spec, features, portrayal, TestContext.Current.CancellationToken);
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var best = timings.Min();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"S-101 ChartReferenceIndex build: best {best:F0} ms of [{string.Join(", ", timings.Select(t => t.ToString("F0")))}], first {cold:F0} ms (budget 300 ms)");

            // A loose guard so a slow CI machine doesn't fail; the PR records the measured time.
            Assert.True(best < 3000, $"S-101 build took {best:F0} ms");
        }
    }
}
