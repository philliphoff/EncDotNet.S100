using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// A feature type entry's variant table (symbol dictionary design note
/// §4.4, C1; §7, R13b, #1721), from the probe results committed with the
/// bundled S-101 catalogue: its rows and columns, the style switch, the
/// picked feature's ringed cell, the partial and no-results states, and the
/// fit in the 360 px dock.
/// </summary>
public sealed class ReferenceVariantTableTests : IDisposable
{
    private static readonly SpecRef S101 = new("S-101", default);

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public ReferenceVariantTableTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    private static ReferenceKey Key(ReferenceKind kind, string code) => new(S101, kind, code);

    [AvaloniaFact]
    public async Task Lateral_beacons_table_is_beacon_shape_by_colour()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var entry = await EntryAsync(f, cache, "LateralBeacon", simplified: true);

        var table = Assert.IsType<VariantTableViewModel>(entry.Variants);
        Assert.True(entry.HasVariants);
        Assert.False(entry.ShowsDrawnWith);
        Assert.False(entry.IsWorkingOutVariants);
        Assert.Equal("Beacon shape", table.RowHeading?.Text);
        Assert.Equal("Colour", table.ColumnHeading?.Text);
        // The headings and values are terms: the attribute, and a listed value.
        Assert.Equal(Key(ReferenceKind.Attribute, "beaconShape"), table.RowHeading!.Key);
        Assert.Equal(8, table.Rows.Count);
        Assert.Equal(5, table.Columns.Count);
        Assert.Equal(["Red, green, red", "Green, red, green", "Red", "Green", "Not given"], table.Columns.Select(c => c.Text));
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), table.Columns[2].Key);
        Assert.Equal("3", table.Columns[2].Value);
        Assert.Null(table.Columns[0].Key); // A list of values is plain words, so it wraps in its column.
        Assert.StartsWith("Stake", table.Rows[0].Label.Text, StringComparison.Ordinal);

        // The inputs left over are filters; the switch is never one.
        Assert.Equal(["Visual prominence", "Category of lateral mark"], table.Filters.Select(x => x.Name));
        Assert.True(table.HasStyleSwitch);
        Assert.True(table.ShowsSimplified);
        Assert.False(table.IsPartial);

        // A red stake, with Simplified symbols on, is BCNLAT21: a term that opens the symbol.
        var redStake = table.Rows[0].Cells[2];
        Assert.Equal(Key(ReferenceKind.Symbol, "BCNLAT21"), redStake.Key);
        Assert.True(redStake.IsKnown);
        Assert.Equal("BCNLAT21", redStake.Name);
        Assert.Contains("Beacon shape: Stake", redStake.Description, StringComparison.Ordinal);
        Assert.Contains("Colour: Red", redStake.Description, StringComparison.Ordinal);
        Assert.All(table.Cells, c => Assert.False(c.IsRinged));
        Assert.Null(table.Ringed);

        // Every cell's glyph is drawn through the glyph cache.
        entry.Start();
        await table.GlyphsDrawn;
        Assert.All(table.Cells.Where(c => c.Lead is not null), c => Assert.NotNull(c.Glyph));
    }

    [Fact]
    public async Task The_switch_changes_the_cells_but_not_the_setting()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath, SimplifiedSymbols = true });
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache, settings);
        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"));
        await tab.Loading;
        var entry = Assert.IsType<FeatureTypeEntryViewModel>(tab.Entry);
        var table = entry.Variants!;
        Assert.True(table.ShowsSimplified);
        Assert.Equal("BCNLAT21", table.Rows[0].Cells[2].Key?.Code);

        table.ShowsPaperChart = true;

        Assert.False(table.ShowsSimplified);
        Assert.Equal("BCNSTK02", table.Rows[0].Cells[2].Key?.Code);
        Assert.True(settings.SimplifiedSymbols);
        Assert.Same(entry, tab.Entry);

        // It starts on the style in use: with Simplified symbols off, the paper chart.
        settings.SimplifiedSymbols = false;
        await tab.Loading;
        var paper = Assert.IsType<FeatureTypeEntryViewModel>(tab.Entry).Variants!;
        Assert.True(paper.ShowsPaperChart);
        Assert.Equal("BCNSTK02", paper.Rows[0].Cells[2].Key?.Code);
    }

    [Fact]
    public async Task A_filter_changes_the_cells()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var table = (await EntryAsync(f, cache, "LateralBeacon", simplified: false)).Variants!;
        var before = table.Cells.Select(c => c.Name).ToList();

        var prominence = table.Filters.Single(x => x.Name == "Visual prominence");
        Assert.Equal(0, prominence.SelectedIndex);
        prominence.SelectedIndex = 1;

        Assert.NotEqual(before, table.Cells.Select(c => c.Name).ToList());
    }

    [Fact]
    public async Task Opened_from_the_synthetic_beacon_pick_the_red_stake_is_ringed()
    {
        var hit = await BeaconHitAsync();
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache);

        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"), ReferenceFeature.Of(hit));
        await tab.Loading;

        var table = Assert.IsType<FeatureTypeEntryViewModel>(tab.Entry).Variants!;
        var ringed = Assert.Single(table.Cells, c => c.IsRinged);
        Assert.Same(ringed, table.Ringed);
        Assert.Same(table.Rows[0].Cells[2], ringed); // Stake × Red.
        // The filters start on the feature's values: it has no visual prominence, and is a port-hand mark.
        Assert.Equal("Not given", table.Filters[0].Options[table.Filters[0].SelectedIndex]);
        Assert.Equal(0, table.Filters[1].SelectedIndex);
        Assert.Equal("BCNSTK02", ringed.Key?.Code);

        // The ring follows the feature's values, in either style.
        table.ShowsSimplified = true;
        Assert.Equal("BCNLAT21", table.Ringed?.Key?.Code);

        // Another value for a filter: no cell is the feature's.
        table.Filters[1].SelectedIndex = 1;
        Assert.Null(table.Ringed);

        // A link to its attribute keeps the pick; one to a symbol doesn't.
        var colour = Key(ReferenceKind.Attribute, "colour");
        tab.Entry!.Terms.Open(colour);
        await tab.Loading;
        Assert.NotNull(nav.CurrentFeature);
        tab.Entry!.Terms.Open(Key(ReferenceKind.Symbol, "BCNLAT21"));
        await tab.Loading;
        Assert.Null(nav.CurrentFeature);

        // Back to the beacon: its pick, and the ring, come back.
        nav.Back();
        nav.Back();
        await tab.Loading;
        Assert.NotNull(Assert.IsType<FeatureTypeEntryViewModel>(tab.Entry).Variants!.Ringed);
    }

    [Fact]
    public async Task The_picked_features_values_are_read_from_its_S57_acronyms()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!;
        var probe = index.Probes!.Find("LateralBeacon")!;
        var feature = new ReferenceFeature(
            "BCNLAT",
            [Leaf("COLOUR", "3,4,3"), Leaf("BCNSHP", "1"), Leaf("CATLAM", "2")],
            S100GeometryType.Point);

        var values = ProbeValues.IndexesOf(probe, index, feature, id => id == ProbeValues.SimplifiedSymbols ? "false" : null);

        int At(string code) => probe.Inputs.Select((x, i) => (x, i)).Single(p => p.x.Code == code).i;
        Assert.Equal(0, values[At("PrimitiveType")]);
        Assert.Equal(0, values[At("beaconShape")]);
        Assert.Equal(0, values[At("colour")]);
        Assert.Equal(1, values[At("categoryOfLateralMark")]);
        Assert.Equal(1, values[At("visualProminence")]); // Not given.
        Assert.Equal(1, values[At(ProbeValues.SimplifiedSymbols)]);
        Assert.Equal("BCNSTK02", ProbeValues.KeyOf(S101, ProbeValues.MainPart(ProbeValues.OutcomeFor(probe, values)!)!)?.Code);
    }

    [Fact]
    public async Task Listed_values_match_exactly_and_numbers_match_the_nearest_value_probed()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!;
        var beacon = index.Probes!.Find("LateralBeacon")!;
        int At(FeatureTypeProbe probe, string code) => probe.Inputs.Select((x, i) => (x, i)).Single(p => p.x.Code == code).i;

        // Colour 5 (yellow) wasn't probed: it doesn't read as the nearest code, 4 (green).
        var yellow = ProbeValues.IndexesOf(beacon, index, new ReferenceFeature("LateralBeacon", [Leaf("colour", "5")], S100GeometryType.Point));
        Assert.Null(yellow[At(beacon, "colour")]);
        // Beacon shape is left out of the feature, as one of the values probed is.
        Assert.Null(beacon.Inputs[At(beacon, "beaconShape")].Values[yellow[At(beacon, "beaconShape")]!.Value]);

        // A depth between those probed takes the nearest one.
        var depthArea = index.Probes.Find("DepthArea")!;
        var drval1 = depthArea.Inputs[At(depthArea, "depthRangeMinimumValue")];
        var deepest = drval1.Values.OfType<string>().MaxBy(v => double.Parse(v, CultureInfo.InvariantCulture))!;
        var target = (double.Parse(deepest, CultureInfo.InvariantCulture) + 0.01).ToString(CultureInfo.InvariantCulture);
        var area = ProbeValues.IndexesOf(depthArea, index, new ReferenceFeature("DepthArea", [Leaf("depthRangeMinimumValue", target)], S100GeometryType.Surface));
        Assert.Equal(deepest, drval1.Values[area[At(depthArea, "depthRangeMinimumValue")]!.Value]);

        // A feature type alone takes each attribute's first value probed.
        var typeOnly = ProbeValues.IndexesOf(beacon, index, ReferenceFeature.TypeOnly("LateralBeacon"));
        Assert.Equal(0, typeOnly[At(beacon, "colour")]);
        Assert.Equal(0, typeOnly[At(beacon, "beaconShape")]);
    }

    [Fact]
    public async Task The_main_part_of_a_depth_area_is_its_colour()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!;
        var depthArea = index.Probes!.Find("DepthArea")!;

        Assert.All(
            depthArea.Outcomes.Where(o => o.Parts.Any(p => p.StartsWith("colour:", StringComparison.Ordinal))),
            o => Assert.StartsWith("colour:", ProbeValues.MainPart(o), StringComparison.Ordinal));
        // The fallback symbol comes after anything else drawn.
        Assert.Equal("line:LINE1", ProbeValues.MainPart(new ProbeOutcome { Parts = ["symbol:QUESMRK1", "line:LINE1"] }));
        Assert.Equal("symbol:QUESMRK1", ProbeValues.MainPart(new ProbeOutcome { Parts = ["symbol:QUESMRK1", "line:_simple_"] }));
    }

    [Fact]
    public async Task A_capped_probe_says_the_table_is_partial()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var table = (await EntryAsync(f, cache, "Wreck", simplified: true)).Variants!;

        Assert.True(table.IsPartial);
        Assert.Equal("Partial: the first 512 combinations", table.PartialText);
    }

    [AvaloniaFact]
    public async Task Without_probe_results_the_entry_keeps_Drawn_with_and_says_when_it_is_working_them_out()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var index = (await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken))!.WithProbes(null);
        var feature = (FeatureTypeEntry)(index.TryGet(Key(ReferenceKind.FeatureType, "LateralBeacon"), out var e) ? e : null)!;

        // An XSLT product, or one whose probe hasn't started: today's list, and nothing more.
        var plain = new FeatureTypeEntryViewModel(feature, Context(index, cache));
        Assert.Null(plain.Variants);
        Assert.True(plain.ShowsDrawnWith);
        Assert.False(plain.IsWorkingOutVariants);

        // A probe running in the background: a quiet note over the list.
        var working = new FeatureTypeEntryViewModel(feature, Context(index, cache, pending: true));
        Assert.True(working.IsWorkingOutVariants);
        Assert.True(working.ShowsDrawnWith);

        var view = new ReferenceEntryView { DataContext = working };
        using var host = ViewHost.Show(new ScrollViewer { Content = view }, 360, 900);
        host.Settle();
        Assert.True(host.IsShown("Reference.Entry.Variants.Working"));
        Assert.True(host.IsShown("Reference.Entry.DrawnWith"));
        Assert.False(host.IsShown("Reference.Entry.Variants"));
    }

    [AvaloniaFact]
    public async Task The_table_fits_the_360_px_entry_and_scrolls_inside_itself()
    {
        var hit = await BeaconHitAsync();
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        using var host = ViewHost.Show(new ReferenceView { DataContext = tab }, 360, 900);

        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"), ReferenceFeature.Of(hit));
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        var table = ((FeatureTypeEntryViewModel)tab.Entry!).Variants!;
        await table.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();
        host.Settle();

        var entry = host.Find<ReferenceEntryView>("Reference.Entry");
        var scroller = host.Find<ScrollViewer>("Reference.Entry.Variants");
        Assert.True(entry.Bounds.Width <= 360, $"entry {entry.Bounds.Width} px wide");
        Assert.True(scroller.Bounds.Width <= 360 - 32, $"table {scroller.Bounds.Width} px wide");
        Assert.True(scroller.IsEffectivelyVisible);
        Assert.True(host.IsShown("Reference.Entry.Variants.Style"));
        Assert.False(host.IsShown("Reference.Entry.DrawnWith"));
        Assert.False(host.IsShown("Reference.Entry.Variants.Partial"));

        // A cell per variant, each a term with its glyph; the feature's is ringed.
        var cells = host.FindAll<ReferenceTerm>("Reference.Entry.Variants.Cell");
        Assert.Equal(40, cells.Count);
        Assert.All(cells, c => Assert.Equal(c.Bounds.Width, c.Bounds.Height));
        var ringed = Assert.Single(cells, c => c.Classes.Contains("ringed"));
        Assert.Equal("BCNSTK02", Avalonia.Automation.AutomationProperties.GetName(ringed));
        Assert.True(ringed.IsTerm);
        Assert.NotNull(ringed.GetVisualDescendants().OfType<Image>().Single().Source);

        // Every text wraps inside the dock, apart from the table's own, which scrolls.
        var inTable = scroller.GetVisualDescendants().ToHashSet();
        Assert.All(
            entry.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !inTable.Contains(t)),
            t => Assert.True(t.Bounds.Width <= 360, $"\"{t.Text}\" is {t.Bounds.Width} px wide"));

        // A click on a cell opens the symbol's entry.
        host.Click(cells[2]);
        await tab.Loading;
        Assert.Equal(Key(ReferenceKind.Symbol, "BCNSTK02"), nav.Current);

        // The switch, clicked: the cells change, the chart's setting doesn't.
        nav.Back();
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();
        host.Click(host.Find<RadioButton>("Reference.Entry.Variants.Style.Simplified"));
        host.Settle();
        var after = host.FindAll<ReferenceTerm>("Reference.Entry.Variants.Cell");
        Assert.Equal("BCNLAT21", Avalonia.Automation.AutomationProperties.GetName(after[2]));
    }

    private static PickAttribute Leaf(string code, string value) => new() { Code = code, RawValue = value };

    private static async Task<FeatureTypeEntryViewModel> EntryAsync(ChartReferenceServiceTests.Fixture f, ChartReferenceGlyphCache cache, string code, bool simplified)
    {
        var index = await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken);
        Assert.NotNull(index?.Probes);
        var feature = Assert.IsType<FeatureTypeEntry>(index.TryGet(Key(ReferenceKind.FeatureType, code), out var e) ? e : null);
        return new FeatureTypeEntryViewModel(feature, Context(index, cache, f.Service.GetArtwork(S101), simplified));
    }

    private static ReferenceEntryContext Context(
        ChartReferenceIndex index, ChartReferenceGlyphCache cache, ChartReferenceArtwork? artwork = null, bool simplified = false, bool pending = false) => new()
        {
            Index = index,
            Glyphs = cache,
            Artwork = artwork,
            SimplifiedSymbols = simplified,
            ProbesPending = pending,
            Open = _ => { },
        };

    private static readonly Lazy<Task<PickHit>> Beacon = new(ReadBeaconAsync);

    /// <summary>
    /// A pick of the synthetic red stake beacon (<see cref="SyntheticS57Cell.WriteWithLateralBeacon"/>):
    /// COLOUR 3, BCNSHP 1, CATLAM 1, no CONVIS.
    /// </summary>
    private static async Task<PickHit> BeaconHitAsync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", "US5MA1BO.000")))
            dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "Test data not found: tests/datasets/S57/US5MA1BO");
        return await Beacon.Value;
    }

    private static async Task<PickHit> ReadBeaconAsync()
    {
        var directory = Directory.CreateTempSubdirectory("variant-table-").FullName;
        var path = SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000");
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            path,
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
        await processor.BuildVectorPortrayalAsync(new RenderContext
        {
            EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.All },
            Mariner = MarinerSettings.Default,
        });
        var feature = processor.EnumerateFeatures().First(x => string.Equals(x.FeatureType, "LateralBeacon", StringComparison.Ordinal));
        var info = processor.GetFeatureInfo(feature.FeatureRef)!;
        return new PickHit
        {
            FeatureType = info.FeatureType,
            FeatureRef = info.FeatureRef,
            ProductSpec = "S-57",
            Attributes = info.Attributes,
            Geometry = info.Geometry,
        };
    }
}
