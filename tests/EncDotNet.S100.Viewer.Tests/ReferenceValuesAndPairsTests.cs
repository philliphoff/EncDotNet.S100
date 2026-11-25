using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Value symbols in attribute entries and before/after pairs in display
/// setting entries (#1722, symbol dictionary design note §4.4 C3 and C4,
/// R13c), from the committed S-101 probe results (R13a).
/// </summary>
public sealed class ReferenceValuesAndPairsTests : IDisposable
{
    private static readonly SpecRef S101 = new("S-101", default);
    private static readonly Lazy<Task<(PickHit Hit, string Path)>> Beacon = new(BeaconAsync);

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public ReferenceValuesAndPairsTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    private static ReferenceKey Key(ReferenceKind kind, string code) => new(S101, kind, code);

    // US5MA1BO with its wreck made a red stake lateral beacon, read as an S-57 cell is picked.
    private static async Task<(PickHit Hit, string Path)> BeaconAsync()
    {
        var directory = Directory.CreateTempSubdirectory("reference-values-").FullName;
        var path = SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000");
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            path,
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
        await processor.BuildVectorPortrayalAsync(new RenderContext());
        var feature = processor.EnumerateFeatures().First(f => f.FeatureType == "LateralBeacon");
        var info = processor.GetFeatureInfo(feature.FeatureRef)!;
        var hit = new PickHit
        {
            FeatureType = info.FeatureType,
            FeatureRef = info.FeatureRef,
            ProductSpec = "S-57",
            DatasetFileName = "US5MA1BO.000",
            Attributes = info.Attributes,
            Geometry = info.Geometry,
        };
        return (hit, path);
    }

    private static string Fixture(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, "tests", "datasets", .. parts]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return Path.Combine(["tests", "datasets", .. parts]);
    }

    private static ReferenceEntryViewModel Entry(
        ChartReferenceIndex index,
        ChartReferenceServiceTests.Fixture f,
        ChartReferenceGlyphCache cache,
        ReferenceKey key,
        ReferenceFeatureContext? feature = null,
        ViewerSettingsService? settings = null)
    {
        Assert.True(index.TryGet(key, out var entry));
        var view = ReferenceEntryViewModel.Create(entry, new ReferenceEntryContext
        {
            Index = index,
            Artwork = f.Service.GetArtwork(S101),
            Glyphs = cache,
            Open = _ => { },
            Feature = feature,
            Settings = settings,
        });
        view.Start();
        return view;
    }

    [AvaloniaFact]
    public async Task Colour_opened_from_the_synthetic_lateral_beacon_shows_a_symbol_per_value_with_red_highlighted()
    {
        Assert.SkipUnless(File.Exists(Fixture("S57", "US5MA1BO", "US5MA1BO.000")), "US5MA1BO is not in tests/datasets");
        var (hit, _) = await Beacon.Value;
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        var terms = ReferenceTerms.For(f.Service, cache, nav, examiner: null, urls: null);
        Assert.NotNull(await terms.IndexAsync(S101));
        var report = TestPickReport.Create();
        report.Terms = terms;
        report.SetPicks([hit]);

        // The pick's colour term opens Colour with the beacon as its feature.
        var colour = report.AttributeRows.Single(r => r.NameKey is { Code: "colour" });
        Assert.NotSame(terms, report.FeatureTerms);
        report.FeatureTerms!.Open(colour.NameKey!.Value);
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), nav.Current);
        var feature = Assert.IsType<ReferenceFeatureContext>(nav.CurrentFeature);
        Assert.Equal("LateralBeacon", feature.FeatureType);
        Assert.True(feature.IsPick);
        Assert.Equal("3", feature.ValueOf("colour"));
        // An S-57 cell's pick doesn't report its geometry; the probe's first primitive (Point) stands for it.
        Assert.True(feature.Primitive is null or "Point");

        using var tab = new ReferenceViewModel(nav, f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        using var host = ViewHost.Show(new ReferenceView { DataContext = tab }, 360, 1400);
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        var entry = Assert.IsType<AttributeEntryViewModel>(tab.Entry);
        await entry.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();
        host.Settle();

        Assert.True(entry.HasValueSymbols);
        Assert.False(entry.HasValues);
        Assert.Equal("Drawn for Lateral Beacon", entry.ValueSymbolsHeading);
        // A row per listed value, then the lists and no value the rule tells apart.
        Assert.Equal(entry.Values.Select(v => v.Title), entry.ValueSymbols.Take(entry.Values.Count).Select(v => v.Title));
        Assert.Contains(entry.ValueSymbols, v => v.Title == "Red, Green, Red");
        Assert.Contains(entry.ValueSymbols, v => v.Title == "No value");

        // Red is the beacon's own and the only one highlighted: a red stake is
        // BCNLAT21 simplified and BCNSTK02 on a paper chart.
        var red = Assert.Single(entry.ValueSymbols, v => v.IsFeatureValue);
        Assert.Equal("3 · Red", red.Title);
        Assert.Equal("BCNLAT21", red.Simplified.Label);
        Assert.Equal("BCNSTK02", red.PaperChart.Label);
        Assert.Equal(Key(ReferenceKind.Symbol, "BCNLAT21"), red.Simplified.Key);
        Assert.NotNull(red.Simplified.Glyph);
        Assert.NotNull(red.PaperChart.Glyph);
        Assert.EndsWith("this feature's value", red.AccessibleName, StringComparison.Ordinal);
        var green = entry.ValueSymbols.Single(v => v.Title == "4 · Green");
        Assert.Equal("BCNLAT22", green.Simplified.Label);
        Assert.False(green.IsFeatureValue);

        // Every probed value has a glyph; a value the probe didn't try says so.
        Assert.All(entry.ValueSymbols.Where(v => v.Simplified.Label != "Not probed"), v => Assert.True(v.Simplified.HasLead));
        Assert.Contains(entry.ValueSymbols, v => v.Simplified.Label == "Not probed");

        Assert.True(host.IsShown("Reference.Entry.ValueSymbols"));
        Assert.False(host.IsShown("Reference.Entry.Values"));
        var rows = host.FindAll<Border>("Reference.Entry.ValueSymbol");
        Assert.Equal(entry.ValueSymbols.Count, rows.Count);
        Assert.Single(rows, r => r.Classes.Contains("crOwn"));

        // A glyph is a term: it opens its symbol's entry.
        var glyph = host.FindAll<ReferenceTerm>("Reference.GlyphTerm").First(t => t.Text == "BCNLAT21");
        Assert.True(glyph.IsTerm);
        host.Click(glyph);
        Assert.Equal(Key(ReferenceKind.Symbol, "BCNLAT21"), nav.Current);
        Assert.Null(nav.CurrentFeature);
    }

    [Fact]
    public async Task A_feature_types_entry_opens_its_attributes_with_itself_as_the_feature()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache);
        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"));
        await tab.Loading;

        tab.Entry!.Terms.Open(Key(ReferenceKind.Attribute, "colour"));
        await tab.Loading;

        Assert.Equal(new ReferenceFeatureContext("LateralBeacon"), nav.CurrentFeature);
        var entry = Assert.IsType<AttributeEntryViewModel>(tab.Entry);
        Assert.True(entry.HasValueSymbols);
        // No pick, so no value is the feature's own.
        Assert.DoesNotContain(entry.ValueSymbols, v => v.IsFeatureValue);
        Assert.Equal("BCNLAT21", entry.ValueSymbols.Single(v => v.Title == "3 · Red").Simplified.Label);

        // Back on the feature type, and to a symbol: the symbol has no feature.
        Assert.True(nav.Back());
        await tab.Loading;
        Assert.Null(nav.CurrentFeature);
        tab.Entry!.Terms.Open(Key(ReferenceKind.Symbol, "BCNLAT21"));
        Assert.Null(nav.CurrentFeature);
    }

    [Fact]
    public void The_navigator_keeps_each_entrys_feature_and_takes_a_new_one_in_place()
    {
        var nav = new ReferenceNavigator();
        var changed = 0;
        nav.Changed += (_, _) => changed++;
        var colour = Key(ReferenceKind.Attribute, "colour");
        var beacon = new ReferenceFeatureContext("LateralBeacon", new Dictionary<string, string> { ["colour"] = "3" }, "Point");

        nav.Open(colour, beacon);
        nav.Open(colour, new ReferenceFeatureContext("LateralBeacon", new Dictionary<string, string> { ["colour"] = "3" }, "Point"));
        Assert.Equal(1, changed);
        nav.Open(colour, new ReferenceFeatureContext("LateralBuoy"));
        Assert.Equal((1, 2), (nav.History.Count, changed));
        Assert.Equal("LateralBuoy", nav.CurrentFeature!.FeatureType);

        nav.Open(Key(ReferenceKind.Symbol, "BCNLAT21"));
        Assert.Null(nav.CurrentFeature);
        Assert.True(nav.Back());
        Assert.Equal("LateralBuoy", nav.CurrentFeature!.FeatureType);

        Assert.Same(beacon, ReferenceFeatureContext.For(colour, beacon));
        Assert.Same(beacon, ReferenceFeatureContext.For(Key(ReferenceKind.FeatureType, "lateralbeacon"), beacon));
        Assert.Null(ReferenceFeatureContext.For(Key(ReferenceKind.FeatureType, "LateralBuoy"), beacon));
        Assert.Null(ReferenceFeatureContext.For(Key(ReferenceKind.Symbol, "BCNLAT21"), beacon));
    }

    [AvaloniaFact]
    public async Task Simplified_symbols_shows_pairs_including_BCNLAT21_against_its_paper_chart_form()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!;
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var entry = Assert.IsType<DisplaySettingEntryViewModel>(Entry(index, f, cache, Key(ReferenceKind.DisplaySetting, "SimplifiedSymbols")));
        await entry.GlyphsDrawn;

        Assert.True(entry.HasBeforeAfter);
        Assert.True(entry.IsToggle);
        Assert.Equal("Off, then on", entry.BeforeAfterNote);
        // Grouped by feature type, in name order.
        var names = entry.BeforeAfter.Select(g => g.Feature.Name).ToList();
        Assert.Equal(names.Order(StringComparer.CurrentCulture), names);
        Assert.Contains("Cardinal Buoy", names);

        var beacon = entry.BeforeAfter.Single(g => g.Feature.Code == "LateralBeacon");
        var pair = Assert.Single(beacon.Pairs, p => p.Before.Label == "BCNSTK02" && p.After.Label == "BCNLAT21");
        Assert.Equal("Lateral Beacon: BCNSTK02, then BCNLAT21", pair.AccessibleName);
        Assert.DoesNotContain(beacon.Pairs, p => LeadGlyph.DefaultSymbols.Contains(p.Before.Label) || LeadGlyph.DefaultSymbols.Contains(p.After.Label));
        Assert.Equal(beacon.Pairs.Count, beacon.Pairs.Select(p => (p.Before.Label, p.After.Label)).Distinct().Count());

        // Capped, with Show all.
        Assert.True(beacon.Pairs.Count > ReferencePairGroup.Cap);
        Assert.Equal(ReferencePairGroup.Cap, beacon.Visible.Count);
        Assert.True(beacon.CanShowAll);
        Assert.Equal($"Show all ({beacon.Pairs.Count})", beacon.ShowAllLabel);
        Assert.NotNull(beacon.Visible[0].Before.Glyph);
        beacon.ShowAllCommand.Execute(null);
        Assert.Equal(beacon.Pairs.Count, beacon.Visible.Count);
        Assert.False(beacon.CanShowAll);
        await entry.GlyphsDrawn;
        Assert.All(beacon.Visible, p => Assert.NotNull(p.After.Glyph));

        using var host = ViewHost.Show(new ReferenceEntryView { DataContext = entry }, 360, 4000);
        host.Settle();
        Assert.True(host.IsShown("Reference.Entry.BeforeAfter"));
        Assert.Contains(host.FindAll<StackPanel>("Reference.Entry.Pair"), p => Avalonia.Automation.AutomationProperties.GetName(p) == pair.AccessibleName);
        Assert.NotEmpty(host.FindAll<Button>("Reference.ShowAllPairs"));
        Assert.True(host.IsShown("Reference.Entry.Changes"));
    }

    [Fact]
    public async Task Safety_contour_shows_a_pair_for_a_wreck_either_side_of_the_value_now()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!;
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }) { SafetyContour = 10 };
        var entry = Assert.IsType<DisplaySettingEntryViewModel>(
            Entry(index, f, cache, Key(ReferenceKind.DisplaySetting, "SafetyContour"), settings: settings));

        Assert.True(entry.HasBeforeAfter);
        Assert.False(entry.IsToggle);
        Assert.Equal("Either side of 10 m", entry.BeforeAfterNote);
        var wreck = entry.BeforeAfter.Single(g => g.Feature.Code == "Wreck");
        // Shallower than the safety contour, a wreck is an isolated danger.
        var pair = wreck.Pairs.First(p => p.Before.Label == "ISODGR01");
        Assert.NotEqual(pair.Before.Label, pair.After.Label);
        Assert.Equal("Value of Sounding 9.9 → 10.1", pair.Caption);

        // The caption follows the setting.
        settings.SafetyContour = 20;
        Assert.Equal("Value of Sounding 19.9 → 20.1", pair.Caption);
        Assert.Equal("Either side of 20 m", entry.BeforeAfterNote);
        entry.Close();
    }

    [AvaloniaFact]
    public async Task With_no_probe_results_the_entries_are_unchanged()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!.WithProbes(null);
        Assert.Null(index.Probes);
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var feature = new ReferenceFeatureContext("LateralBeacon", new Dictionary<string, string> { ["colour"] = "3" }, "Point");

        var colour = Assert.IsType<AttributeEntryViewModel>(Entry(index, f, cache, Key(ReferenceKind.Attribute, "colour"), feature));
        Assert.False(colour.HasValueSymbols);
        Assert.True(colour.HasValues);
        Assert.Contains(colour.Values, v => v.Title == "3 · Red");
        Assert.Contains(colour.Changes, l => l.Code == "LateralBeacon");

        var simplified = Assert.IsType<DisplaySettingEntryViewModel>(Entry(index, f, cache, Key(ReferenceKind.DisplaySetting, "SimplifiedSymbols")));
        Assert.False(simplified.HasBeforeAfter);
        Assert.Contains(simplified.Changes, l => l.Code == "LateralBeacon");

        using (var host = ViewHost.Show(new ReferenceEntryView { DataContext = colour }, 360, 2000))
        {
            host.Settle();
            Assert.True(host.IsShown("Reference.Entry.Values"));
            Assert.False(host.IsShown("Reference.Entry.ValueSymbols"));
        }

        using (var host = ViewHost.Show(new ReferenceEntryView { DataContext = simplified }, 360, 2000))
        {
            host.Settle();
            Assert.False(host.IsShown("Reference.Entry.BeforeAfter"));
            Assert.True(host.IsShown("Reference.Entry.Changes"));
        }

        // With probe results but no feature, an attribute lists its values alone too.
        var probed = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!;
        var plain = Assert.IsType<AttributeEntryViewModel>(Entry(probed, f, cache, Key(ReferenceKind.Attribute, "colour")));
        Assert.False(plain.HasValueSymbols);
        Assert.True(plain.HasValues);
    }

    [Fact]
    public void Probe_values_match_lists_numbers_and_no_value()
    {
        var input = new ProbeInput(ProbeInputKind.Attribute, "colour", ["3,4,3", "3", "4", null]);
        Assert.Equal(0, ProbeOutcomes.ValueOf(input, "3, 4,3"));
        Assert.Equal(1, ProbeOutcomes.ValueOf(input, "3"));
        Assert.Equal(3, ProbeOutcomes.ValueOf(input, null));
        Assert.Equal(-1, ProbeOutcomes.ValueOf(input, "5", nearest: false));

        var depth = new ProbeInput(ProbeInputKind.Attribute, "valueOfSounding", ["29.9", "30", "30.1", null]);
        Assert.Equal(2, ProbeOutcomes.ValueOf(depth, "42"));
        Assert.Equal(0, ProbeOutcomes.ValueOf(depth, "12.5"));

        Assert.Equal("symbol:BCNLAT21", ProbeOutcomes.MainPart(new ProbeOutcome { Parts = ["symbol:QUESMRK1", "symbol:BCNLAT21", "text:Bn 1"] }));
        Assert.Equal("colour:DEPVS", ProbeOutcomes.MainPart(new ProbeOutcome { Parts = ["line:DEPCNT02", "colour:DEPVS"] }));
        Assert.Null(ProbeOutcomes.MainPart(new ProbeOutcome { Parts = ["text:1"] }));
    }
}
