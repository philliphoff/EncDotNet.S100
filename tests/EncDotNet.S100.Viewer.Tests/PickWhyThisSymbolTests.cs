using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Object information's Why this symbol (symbol dictionary R11, #1680): each
/// drawn part's reason, the rule's cases for it matched against the pick's
/// values and the display settings, on the S-57 cell US5MA1BO drawn with the
/// S-101 catalogue.
/// </summary>
public sealed class PickWhyThisSymbolTests
{
    private const string RedStake = "Colour is red · Beacon shape is a stake, pole, perch or post";

    private static readonly Lazy<Task<Cells>> Portrayed = new(PortrayAsync);

    /// <summary>
    /// US5MA1BO, its wreck made a red stake lateral beacon
    /// (<see cref="SyntheticS57Cell.WriteWithLateralBeacon"/>), portrayed with
    /// Simplified symbols on and off; and the real cell, whose wreck is drawn
    /// as an isolated danger.
    /// </summary>
    private sealed record Cells(Cell Simplified, Cell PaperChart, Cell Real);

    private sealed record Cell(VectorPortrayalResult Result, S57DatasetProcessor Processor);

    private static async Task<Cells> PortrayAsync()
    {
        var directory = Directory.CreateTempSubdirectory("pick-why-").FullName;
        var beacon = SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000");
        return new Cells(
            await PortrayAsync(beacon, simplified: true),
            await PortrayAsync(beacon, simplified: false),
            await PortrayAsync(Fixture("S57", "US5MA1BO", "US5MA1BO.000"), simplified: false));
    }

    private static async Task<Cell> PortrayAsync(string path, bool simplified)
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            path,
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext
        {
            EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.All },
            Mariner = MarinerSettings.Default with { SimplifiedSymbols = simplified },
        });
        return new Cell(result, processor);
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

    /// <summary>A cell on the stack, a legend over it, and Object information with Drawn as and Why this symbol.</summary>
    private sealed class Scene : IDisposable
    {
        private Scene(Cells cells, bool traced)
        {
            Cells = cells;
            Chart = Legend.Add("US5MA1BO.000", "S-57", 42, -71, 43, -70);
            Service = Legend.Create();
            Glyphs = new ChartReferenceGlyphCache(PaletteType.Day);
            Terms = new ReferenceTerms(Glyphs, _ => { }, spec => Catalogues.Service.GetIndexAsync(spec), Catalogues.Service.GetArtwork);
            DrawnAs = new PickDrawnAsViewModel(
                Service.DrawnParts,
                Terms,
                Catalogues.Service.GetArtwork,
                action => action(),
                parameter => ReferenceTargets.Value(Mariner, parameter),
                Changed.Add,
                parameter => ReferenceTargets.CurrentValue(Mariner, parameter),
                traced ? Trace : null);
            Tracer = new PickRuleTracer(Legend.Loader, () => Mariner, TestLuaEngine.Create(LuaEngineKind.LuaCSharp));
            Report = TestPickReport.Create();
            Report.Terms = Terms;
            Report.AttachLegend(Service);
            Report.AttachDrawnAs(DrawnAs);
            Show(cells.Simplified);
        }

        /// <summary>Creates the scene; with <paramref name="traced"/>, Why this symbol traces Lua rules (R12b, #1719).</summary>
        public static async Task<Scene> CreateAsync(bool traced = false)
        {
            var path = Fixture("S57", "US5MA1BO", "US5MA1BO.000");
            Assert.SkipUnless(File.Exists(path), $"Test data not found at {path}");
            return new(await Portrayed.Value, traced);
        }

        /// <summary>Traces through <see cref="Tracer"/>, or through <see cref="TraceOverride"/> when set.</summary>
        private Task<LuaFeatureTrace?> Trace(PickHit hit, CancellationToken cancellationToken) =>
            TraceOverride?.Invoke(hit, cancellationToken) ?? Tracer.TraceAsync(hit, cancellationToken);

        /// <summary>The tracer over the cell on the stack, with <see cref="Mariner"/>.</summary>
        public PickRuleTracer Tracer { get; }

        /// <summary>Replaces the tracer, to hold a trace in flight.</summary>
        public Func<PickHit, CancellationToken, Task<LuaFeatureTrace?>>? TraceOverride { get; set; }

        public Cells Cells { get; }

        public LegendServiceTests.Fixture Legend { get; } = new();

        public ChartReferenceServiceTests.Fixture Catalogues { get; } = new("S-101");

        public DatasetEntry Chart { get; }

        public LegendService Service { get; }

        public ChartReferenceGlyphCache Glyphs { get; }

        public ReferenceTerms Terms { get; }

        public PickDrawnAsViewModel DrawnAs { get; }

        public PickReportViewModel Report { get; }

        /// <summary>The display settings the reasons read: the defaults, with Simplified symbols on.</summary>
        public MarinerSettings Mariner { get; set; } = MarinerSettings.Default with { SimplifiedSymbols = true };

        /// <summary>The Simplified symbols switch the reasons read.</summary>
        public bool Simplified
        {
            get => Mariner.SimplifiedSymbols;
            set => Mariner = Mariner with { SimplifiedSymbols = value };
        }

        /// <summary>The settings a reason's link asked Settings to show.</summary>
        public List<string> Changed { get; } = [];

        /// <summary>Puts <paramref name="cell"/>'s display lists on the stack, and its processor behind the chart.</summary>
        public void Show(Cell cell)
        {
            ((Dictionary<DatasetEntry, IDatasetProcessor>)Legend.Loader.Processors)[Chart] = cell.Processor;
            Legend.Loader.SetEntries([.. cell.Result.SubLayers.Select(sub => new LayerStackEntry(
                new MemoryLayer(sub.LayerName),
                new SubLayerStackItem(new VectorStackPayload(cell.Result, sub), sub.Plane, 0, Chart.Id.Value, sub.SourceFeatureType)))]);
            Legend.Loader.FireLayerStackChanged();
        }

        /// <summary>A pick of the first feature of <paramref name="type"/> in <paramref name="cell"/>, as the pick service makes it.</summary>
        public PickHit Hit(Cell cell, string type)
        {
            var feature = cell.Processor.EnumerateFeatures().First(f => string.Equals(f.FeatureType, type, StringComparison.Ordinal));
            var info = cell.Processor.GetFeatureInfo(feature.FeatureRef)!;
            return new PickHit
            {
                FeatureType = info.FeatureType,
                FeatureRef = info.FeatureRef,
                DatasetId = Chart.Id.Value,
                ProductSpec = "S-57",
                Attributes = info.Attributes,
                Geometry = info.Geometry,
            };
        }

        /// <summary>Picks <paramref name="hit"/> and waits for its names, glyphs and reasons.</summary>
        public async Task PickAsync(PickHit hit)
        {
            Report.SetPicks([hit]);
            await DrawnAs.Loading;
        }

        /// <summary>The stack changed: reads the parts and their reasons again.</summary>
        public async Task RefreshAsync()
        {
            DrawnAs.Refresh();
            await DrawnAs.Loading;
        }

        public PickDrawnPartViewModel Main => DrawnAs.Parts.Single(p => p.IsMain);

        public void Dispose()
        {
            DrawnAs.Dispose();
            Glyphs.Dispose();
            Service.Dispose();
            Catalogues.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task A_red_stake_beacon_with_simplified_symbols_on_says_so_and_the_switch_changes_the_reason()
    {
        using var scene = await Scene.CreateAsync();
        var hit = scene.Hit(scene.Cells.Simplified, "LateralBeacon");

        await scene.PickAsync(hit);

        var main = scene.Main;
        Assert.Equal("BCNLAT21", main.Part.ItemId);
        var reason = main.Reason!;
        Assert.Equal(PickReasonKind.Matched, reason.Kind);
        Assert.Equal("Simplified symbols is on · " + RedStake, reason.Text);
        // The names are terms: the setting, the attributes and their values.
        var terms = reason.Lines[0].Runs.Where(r => r.IsTerm).Select(r => (r.Text, r.Key!.Value.Kind)).ToList();
        Assert.Contains(("Simplified symbols", ReferenceKind.DisplaySetting), terms);
        Assert.Contains(("Colour", ReferenceKind.Attribute), terms);
        Assert.Contains(("red", ReferenceKind.Attribute), terms);
        Assert.Contains(("Beacon shape", ReferenceKind.Attribute), terms);
        Assert.Equal("Change Simplified symbols in Display settings", Assert.Single(reason.Settings).Label);

        // Simplified symbols off: the paper-chart stake, and a reason that says why.
        scene.Simplified = false;
        scene.Show(scene.Cells.PaperChart);
        await scene.RefreshAsync();

        main = scene.Main;
        Assert.Equal("BCNSTK02", main.Part.ItemId);
        Assert.Equal(PickReasonKind.Matched, main.Reason!.Kind);
        Assert.Equal("Simplified symbols is off · Beacon shape is a stake, pole, perch or post", main.Reason.Text);
    }

    [AvaloniaFact]
    public async Task With_the_same_parts_a_changed_setting_still_changes_the_reason()
    {
        using var scene = await Scene.CreateAsync();
        await scene.PickAsync(scene.Hit(scene.Cells.Simplified, "LateralBeacon"));
        Assert.Equal(PickReasonKind.Matched, scene.Main.Reason!.Kind);

        // The stack hasn't been portrayed again yet: BCNLAT21 is drawn with the switch off.
        scene.Simplified = false;
        await scene.RefreshAsync();

        Assert.Equal("BCNLAT21", scene.Main.Part.ItemId);
        Assert.Equal(PickReasonKind.NoMatch, scene.Main.Reason!.Kind);
        Assert.Equal("No case of the rule matches this feature's values and the display settings", scene.Main.Reason.Text);
    }

    [AvaloniaFact]
    public async Task The_wreck_gives_its_one_case_with_its_values_and_the_safety_contour_changes_it()
    {
        using var scene = await Scene.CreateAsync();
        scene.Simplified = false;
        scene.Show(scene.Cells.Real);
        var wreck = scene.Hit(scene.Cells.Real, "Wreck");

        await scene.PickAsync(wreck);

        // UDWHAZ05 has two cases for ISODGR01. The first needs the surrounding
        // depth not given or at least the safety contour: 10 m against 30 m,
        // so it doesn't hold, and one case is left. Its test of the depth the
        // rule works out (DEPTH_VALUE) is left out, with a note.
        var main = scene.Main;
        Assert.Equal("ISODGR01", main.Part.ItemId);
        var reason = main.Reason!;
        Assert.Equal(PickReasonKind.Matched, reason.Kind);
        Assert.Single(reason.Lines);
        // A comparison with zero that holds ("is at least 0") is checked but not said.
        Assert.Equal(
            "Shallow water dangers is on · Surrounding depth (10 m) is less than the safety contour (30 m) · Water level effect is always under water/submerged",
            reason.Lines[0].Text);
        // A quiet note, not a line of the reason.
        Assert.Equal("Also depends on values the rule works out", reason.Note);
        Assert.DoesNotContain("Likely", reason.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Not checked", reason.Text, StringComparison.Ordinal);
        // The settings it depends on are terms with links.
        Assert.Equal(
            ["Change Shallow water dangers in Display settings", "Change Safety contour in Display settings"],
            reason.Settings.Select(s => s.Label));

        // A safety contour of 5 m: 10 m is at or above it, so the other case holds.
        scene.Mariner = scene.Mariner with { SafetyContour = Depth.FromMetres(5) };
        await scene.RefreshAsync();

        reason = scene.Main.Reason!;
        Assert.Equal(PickReasonKind.Matched, reason.Kind);
        Assert.Contains("Surrounding depth (10 m) is at least the safety contour (5 m)", reason.Lines[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Shallow water dangers", reason.Lines[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Not checked", reason.Text, StringComparison.Ordinal);

        // With shallow-water dangers off and the contour back at 30 m, no case holds.
        scene.Mariner = scene.Mariner with { SafetyContour = Depth.FromMetres(30), ShallowWaterDangers = false };
        await scene.RefreshAsync();
        Assert.Equal(PickReasonKind.NoMatch, scene.Main.Reason!.Kind);
    }

    [AvaloniaFact]
    public async Task Cases_are_told_apart_by_the_switch_and_the_fuller_case_says_why()
    {
        using var scene = await Scene.CreateAsync();
        var index = (await scene.Catalogues.Service.GetIndexAsync(new SpecRef("S-101", default)))!;
        var beacon = Assert.IsType<FeatureTypeEntry>(PickTerms.FeatureType(index, "LateralBeacon"));
        var cairn = new PickDrawnPart(PickDrawnPartKind.Symbol, "1", new ReferenceKey(index.Spec, ReferenceKind.Symbol, "CAIRNS01"));
        var facts = new PickFacts([new PickAttribute { Code = "beaconShape", RawValue = "6" }], S100GeometryType.Point, _ => null);

        // A setting the viewer doesn't set has the catalogue's default: off.
        var off = PickReasons.Explain(cairn, beacon, index, facts);
        Assert.Equal(PickReasonKind.Matched, off.Kind);
        Assert.Equal("Simplified symbols is off · Beacon shape is a cairn", off.Text);

        var on = PickReasons.Explain(cairn, beacon, index, facts with { Setting = id => id == "SimplifiedSymbols" ? "true" : null });
        Assert.Equal("Simplified symbols is on · Beacon shape is a cairn", on.Text);

        // A red, green, red stake draws BCNLAT21 by its own case and by red's:
        // the fuller case says why.
        var lateral = new PickDrawnPart(PickDrawnPartKind.Symbol, "1", new ReferenceKey(index.Spec, ReferenceKind.Symbol, "BCNLAT21"));
        var redGreenRed = PickReasons.Explain(
            lateral,
            beacon,
            index,
            new PickFacts(
                [new PickAttribute { Code = "colour", RawValue = "3,4,3" }, new PickAttribute { Code = "beaconShape", RawValue = "1" }],
                S100GeometryType.Point,
                id => id == "SimplifiedSymbols" ? "true" : null));
        Assert.Equal(PickReasonKind.Matched, redGreenRed.Kind);
        Assert.Equal("Simplified symbols is on · Colour is red, green, red · Beacon shape is a stake, pole, perch or post", redGreenRed.Text);

        // The same with the values as repeated rows, as an S-101 cell gives them.
        var rows = PickReasons.Explain(
            lateral,
            beacon,
            index,
            new PickFacts(
                [
                    new PickAttribute { Code = "colour", RawValue = "3" },
                    new PickAttribute { Code = "colour", RawValue = "4" },
                    new PickAttribute { Code = "colour", RawValue = "3" },
                    new PickAttribute { Code = "beaconShape", RawValue = "1" },
                ],
                S100GeometryType.Point,
                id => id == "SimplifiedSymbols" ? "true" : null));
        Assert.Equal(redGreenRed.Text, rows.Text);
    }

    [AvaloniaFact]
    public async Task An_ambiguous_case_says_likely_and_lists_what_holds()
    {
        using var scene = await Scene.CreateAsync();
        var index = (await scene.Catalogues.Service.GetIndexAsync(new SpecRef("S-101", default)))!;
        var colour = new RuleCondition { Kind = RuleConditionKind.Attribute, Operator = ConditionOperator.Equal, Subject = "colour", Index = 1, Value = "3" };
        var simplified = new RuleCondition { Kind = RuleConditionKind.ContextParameter, Operator = ConditionOperator.IsTrue, Subject = "SimplifiedSymbols" };
        var computed = new RuleCondition { Kind = RuleConditionKind.Expression, Operator = ConditionOperator.Holds, Text = "depth < contextParameters.SafetyContour" };
        // Two rules draw the symbol, each by a case that holds and neither fuller.
        var feature = new FeatureTypeEntry
        {
            Key = new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "LateralBeacon"),
            Name = "Lateral Beacon",
            PermittedPrimitives = ["Point"],
            Uses =
            [
                new SymbolUse { ItemId = "BCNLAT21", ItemKind = ReferenceKind.Symbol, RuleFile = "A", Conditions = [simplified, colour] },
                new SymbolUse { ItemId = "BCNLAT21", ItemKind = ReferenceKind.Symbol, RuleFile = "B", Conditions = [simplified] },
                new SymbolUse { ItemId = "BCNLAT21", ItemKind = ReferenceKind.Symbol, RuleFile = "C", Conditions = [colour with { Index = 2 }] },
                new SymbolUse { ItemId = "BCNLAT15", ItemKind = ReferenceKind.Symbol, RuleFile = "D", Conditions = [colour, computed] },
            ],
        };
        var facts = new PickFacts([new PickAttribute { Code = "colour", RawValue = "3,3" }], S100GeometryType.Point, _ => "true");
        var symbol = new PickDrawnPart(PickDrawnPartKind.Symbol, "1", new ReferenceKey(index.Spec, ReferenceKind.Symbol, "BCNLAT21"));

        var several = PickReasons.Explain(symbol, feature, index, facts);

        Assert.Equal(PickReasonKind.Likely, several.Kind);
        // What the cases share is said once, as the Reference tab says it.
        Assert.Equal(
            ["Likely, one of these:", "When colour is red:", "Simplified symbols is on", "With no further condition"],
            several.Lines.Select(l => l.Text));

        Assert.DoesNotContain("Not checked", several.Text, StringComparison.Ordinal);

        // One case with a condition on a value the rule works out: that case,
        // plainly, with a quiet note that the rest isn't checked.
        var computedCase = PickReasons.Explain(symbol with { Key = new ReferenceKey(index.Spec, ReferenceKind.Symbol, "BCNLAT15") }, feature, index, facts);
        Assert.Equal(PickReasonKind.Matched, computedCase.Kind);
        Assert.Equal("Colour is red", Assert.Single(computedCase.Lines).Text);
        Assert.Equal("Also depends on values the rule works out", computedCase.Note);
    }

    [AvaloniaFact]
    public async Task A_comparison_with_zero_is_checked_but_not_said()
    {
        using var scene = await Scene.CreateAsync();
        var index = (await scene.Catalogues.Service.GetIndexAsync(new SpecRef("S-101", default)))!;
        var atLeastZero = new RuleCondition { Kind = RuleConditionKind.Expression, Operator = ConditionOperator.Holds, Text = "feature.surroundingDepth >= scaledDecimalZero" };
        var structured = new RuleCondition { Kind = RuleConditionKind.Attribute, Operator = ConditionOperator.GreaterThan, Subject = "surroundingDepth", Value = "0" };
        var shallow = new RuleCondition { Kind = RuleConditionKind.Expression, Operator = ConditionOperator.Holds, Text = "feature.surroundingDepth < contextParameters.SafetyContour" };
        var feature = new FeatureTypeEntry
        {
            Key = new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "Wreck"),
            Name = "Wreck",
            PermittedPrimitives = ["Point"],
            Uses = [new SymbolUse { ItemId = "ISODGR01", ItemKind = ReferenceKind.Symbol, RuleFile = "A", Conditions = [atLeastZero, structured, shallow] }],
        };
        var symbol = new PickDrawnPart(PickDrawnPartKind.Symbol, "1", new ReferenceKey(index.Spec, ReferenceKind.Symbol, "ISODGR01"));
        PickFacts Depth(string metres) => new(
            [new PickAttribute { Code = "surroundingDepth", RawValue = metres, DisplayValue = metres + " m" }],
            S100GeometryType.Point,
            id => id == "SafetyContour" ? "30" : null,
            id => id == "SafetyContour" ? "30 m" : null);

        var held = PickReasons.Explain(symbol, feature, index, Depth("10"));
        Assert.Equal(PickReasonKind.Matched, held.Kind);
        Assert.Equal("Surrounding depth (10 m) is less than the safety contour (30 m)", held.Text);

        // Still checked: below zero, the case doesn't hold.
        Assert.Equal(PickReasonKind.NoMatch, PickReasons.Explain(symbol, feature, index, Depth("-1")).Kind);
    }

    [AvaloniaFact]
    public async Task A_fallback_says_nothing_else_matched_and_what_was_missing()
    {
        using var scene = await Scene.CreateAsync();
        var index = (await scene.Catalogues.Service.GetIndexAsync(new SpecRef("S-101", default)))!;
        var beacon = Assert.IsType<FeatureTypeEntry>(PickTerms.FeatureType(index, "LateralBeacon"));
        var fallback = new PickDrawnPart(PickDrawnPartKind.Symbol, "1", new ReferenceKey(index.Spec, ReferenceKind.Symbol, "BCNDEF13"));

        var reason = PickReasons.Explain(fallback, beacon, index, new PickFacts([], S100GeometryType.Point, _ => "true"));

        Assert.Equal(PickReasonKind.Fallback, reason.Kind);
        Assert.Equal("Nothing else in the rule matched, so the catalogue's fallback symbol is drawn", reason.Lines[0].Text);
        Assert.StartsWith("Not given: ", reason.Lines[1].Text, StringComparison.Ordinal);
        Assert.Contains("Colour", reason.Lines[1].Text, StringComparison.Ordinal);
        Assert.Contains("Beacon shape", reason.Lines[1].Text, StringComparison.Ordinal);
        Assert.Contains(reason.Lines[1].Runs, r => r is { IsTerm: true, Text: "Colour" });
    }

    [AvaloniaFact]
    public async Task Reasons_are_folded_by_default_and_a_setting_link_opens_settings_at_the_switch()
    {
        using var scene = await Scene.CreateAsync();
        await scene.PickAsync(scene.Hit(scene.Cells.Simplified, "LateralBeacon"));
        using var host = ViewHost.Show(new PickReportView { DataContext = scene.Report }, width: 420, height: 1600);
        host.Settle();

        Assert.True(scene.DrawnAs.HasReasons);
        var why = host.Find<Expander>("PickReport.WhyThisSymbol");
        Assert.True(host.IsShown("PickReport.WhyThisSymbol"));
        Assert.Equal("Why this symbol", AutomationProperties.GetName(why));
        Assert.False(why.IsExpanded);
        Assert.False(scene.DrawnAs.WhyExpanded);

        why.IsExpanded = true;
        host.Settle();
        Assert.True(scene.DrawnAs.WhyExpanded);
        var reasons = host.FindAll<StackPanel>("PickReport.WhyThisSymbol.Reason").Where(r => r.IsEffectivelyVisible).ToList();
        var main = Assert.Single(reasons);
        Assert.Equal("Reason", AutomationProperties.GetName(main));
        Assert.Equal(scene.Main.Reason!.HelpText, AutomationProperties.GetHelpText(main));
        Assert.Contains(main.GetVisualDescendants().OfType<ReferenceTerm>(), t => t.Key?.Kind == ReferenceKind.DisplaySetting);

        // Each other part (the text) is one line, its reason folded underneath.
        var others = host.FindAll<Expander>("PickReport.WhyThisSymbol.Part").Where(e => e.IsEffectivelyVisible).ToList();
        Assert.Equal(scene.DrawnAs.Parts.Count(p => !p.IsMain), others.Count);
        Assert.All(others, e => Assert.False(e.IsExpanded));

        var link = host.Find<Button>("PickReport.WhyThisSymbol.ChangeSetting");
        Assert.Equal("Change Simplified symbols in Display settings", AutomationProperties.GetName(link));
        link.Command!.Execute(null);
        Assert.Equal(["SimplifiedSymbols"], scene.Changed);
    }

    [AvaloniaFact]
    public async Task The_traced_wreck_gives_one_reason_with_no_note_and_the_safety_contour_changes_it()
    {
        using var scene = await Scene.CreateAsync(traced: true);
        scene.Simplified = false;
        scene.Show(scene.Cells.Real);

        await scene.PickAsync(scene.Hit(scene.Cells.Real, "Wreck"));

        // The trace shows the case UDWHAZ05 took: its words, as the match
        // says them, without "Likely" and without the note. The test of the
        // depth the rule works out (DEPTH_VALUE) is left out quietly.
        var main = scene.Main;
        Assert.Equal("ISODGR01", main.Part.ItemId);
        var reason = main.Reason!;
        Assert.Equal(PickReasonKind.Traced, reason.Kind);
        Assert.True(reason.IsTraced);
        Assert.Equal(
            "Shallow water dangers is on · Surrounding depth (10 m) is less than the safety contour (30 m) · Water level effect is always under water/submerged",
            Assert.Single(reason.Lines).Text);
        Assert.Null(reason.Note);
        Assert.False(reason.HasNote);
        Assert.DoesNotContain("Likely", reason.Text, StringComparison.Ordinal);
        Assert.Equal(
            ["Change Shallow water dangers in Display settings", "Change Safety contour in Display settings"],
            reason.Settings.Select(s => s.Label));

        // A safety contour of 5 m: the rule takes its other case.
        scene.Mariner = scene.Mariner with { SafetyContour = Depth.FromMetres(5) };
        await scene.RefreshAsync();

        reason = scene.Main.Reason!;
        Assert.Equal(PickReasonKind.Traced, reason.Kind);
        Assert.Contains("Surrounding depth (10 m) is at least the safety contour (5 m)", Assert.Single(reason.Lines).Text, StringComparison.Ordinal);
        Assert.Null(reason.Note);
    }

    [AvaloniaTheory]
    [InlineData(true, "BCNLAT21", "Simplified symbols is on · " + RedStake)]
    [InlineData(false, "BCNSTK02", "Simplified symbols is off · Beacon shape is a stake, pole, perch or post")]
    public async Task The_traced_lateral_beacon_gives_its_exact_case(bool simplified, string symbol, string text)
    {
        using var scene = await Scene.CreateAsync(traced: true);
        scene.Simplified = simplified;
        var cell = simplified ? scene.Cells.Simplified : scene.Cells.PaperChart;
        scene.Show(cell);

        await scene.PickAsync(scene.Hit(cell, "LateralBeacon"));

        var main = scene.Main;
        Assert.Equal(symbol, main.Part.ItemId);
        Assert.Equal(PickReasonKind.Traced, main.Reason!.Kind);
        // Paper chart's case is an else: its condition's line never runs, and
        // the line the branch draws on shows it was taken.
        Assert.Equal(text, main.Reason.Text);
        Assert.Null(main.Reason.Note);
    }

    [AvaloniaFact]
    public async Task A_part_the_trace_cant_map_keeps_the_match()
    {
        using var scene = await Scene.CreateAsync(traced: true);
        await scene.PickAsync(scene.Hit(scene.Cells.Simplified, "LateralBeacon"));
        Assert.Equal(PickReasonKind.Traced, scene.Main.Reason!.Kind);

        // The stack still shows BCNLAT21, but with the switch off the traced
        // portrayal draws the paper-chart stake: the trace can't say why
        // BCNLAT21 was drawn, so the match stands.
        scene.Simplified = false;
        await scene.RefreshAsync();
        Assert.Equal("BCNLAT21", scene.Main.Part.ItemId);
        Assert.Equal(PickReasonKind.NoMatch, scene.Main.Reason!.Kind);

        // A trace in which none of the part's uses ran keeps the match, "Likely" and all.
        var index = (await scene.Catalogues.Service.GetIndexAsync(new SpecRef("S-101", default)))!;
        var colour = new RuleCondition { Kind = RuleConditionKind.Attribute, Operator = ConditionOperator.Equal, Subject = "colour", Index = 1, Value = "3" };
        var simplified = new RuleCondition { Kind = RuleConditionKind.ContextParameter, Operator = ConditionOperator.IsTrue, Subject = "SimplifiedSymbols" };
        var feature = new FeatureTypeEntry
        {
            Key = new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "LateralBeacon"),
            Name = "Lateral Beacon",
            PermittedPrimitives = ["Point"],
            Uses =
            [
                new SymbolUse { ItemId = "BCNLAT21", ItemKind = ReferenceKind.Symbol, RuleFile = "A", Line = 10, Conditions = [simplified] },
                new SymbolUse { ItemId = "BCNLAT21", ItemKind = ReferenceKind.Symbol, RuleFile = "B", Line = 20, Conditions = [colour] },
            ],
        };
        var facts = new PickFacts([new PickAttribute { Code = "colour", RawValue = "3" }], S100GeometryType.Point, _ => "true");
        var symbol = new PickDrawnPart(PickDrawnPartKind.Symbol, "1", new ReferenceKey(index.Spec, ReferenceKind.Symbol, "BCNLAT21"));
        var drawn = new PointInstruction { FeatureReference = "1", SymbolReference = "BCNLAT21" };

        var unmapped = PickReasons.Explain(symbol, feature, index, facts, new LuaFeatureTrace("1", [drawn], [new LuaTraceLine("A", 9)]));
        Assert.Equal(PickReasonKind.Likely, unmapped.Kind);
        Assert.Equal(PickReasons.Explain(symbol, feature, index, facts).Text, unmapped.Text);

        // The line of one use ran: that case, and only it.
        var mapped = PickReasons.Explain(symbol, feature, index, facts, new LuaFeatureTrace("1", [drawn], [new LuaTraceLine("B", 20)]));
        Assert.Equal(PickReasonKind.Traced, mapped.Kind);
        Assert.Equal("Colour is red", mapped.Text);

        // Both ran: the trace doesn't say which drew it, so the match stands.
        var both = PickReasons.Explain(symbol, feature, index, facts, new LuaFeatureTrace("1", [drawn], [new LuaTraceLine("A", 10), new LuaTraceLine("B", 20)]));
        Assert.Equal(PickReasonKind.Likely, both.Kind);

        // The trace didn't draw the part: the match.
        var other = PickReasons.Explain(symbol, feature, index, facts, new LuaFeatureTrace("1", [], [new LuaTraceLine("B", 20)]));
        Assert.Equal(PickReasonKind.Likely, other.Kind);
    }

    [AvaloniaFact]
    public async Task A_pick_change_cancels_the_trace_in_flight_and_shows_the_match_meanwhile()
    {
        using var scene = await Scene.CreateAsync(traced: true);
        var held = new TaskCompletionSource<LuaFeatureTrace?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        scene.TraceOverride = (_, token) =>
        {
            // The trace starts off the UI thread, just after the match is
            // posted, so the test waits for both.
            lock (tokens)
                tokens.Add(token);
            token.Register(() => held.TrySetCanceled(token));
            return held.Task;
        };
        var beacon = scene.Hit(scene.Cells.Simplified, "LateralBeacon");

        scene.Report.SetPicks([beacon]);
        // The match shows while the trace runs.
        await WaitUntilAsync(() =>
        {
            lock (tokens)
                return tokens.Count > 0 && scene.DrawnAs.Parts.Any(p => p is { IsMain: true, Reason: not null });
        });
        Assert.Equal(PickReasonKind.Matched, scene.Main.Reason!.Kind);
        CancellationToken first;
        lock (tokens)
            first = Assert.Single(tokens);
        Assert.False(first.IsCancellationRequested);

        // Another pick: the trace in flight is cancelled, and nothing it gives is shown.
        scene.TraceOverride = (hit, token) => scene.Tracer.TraceAsync(hit, token);
        scene.Report.SetPicks([]);
        Assert.True(first.IsCancellationRequested);
        await scene.DrawnAs.Loading;
        Assert.Empty(scene.DrawnAs.Parts);

        // Picking again traces again.
        await scene.PickAsync(beacon);
        Assert.Equal(PickReasonKind.Traced, scene.Main.Reason!.Kind);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out");
            await Task.Yield();
        }
    }

    [AvaloniaFact]
    public void Settings_shows_the_chart_page_at_the_switch()
    {
        var settings = new SettingsViewModel([new TestSettingsPage(SettingsCategory.General, 0), new TestSettingsPage(SettingsCategory.Chart, 1)]);
        string? requested = null;
        settings.SettingRequested += (_, id) => requested = id;

        settings.ShowSetting(SettingsCategory.Chart, "SimplifiedSymbols");

        Assert.Equal(SettingsCategory.Chart, settings.SelectedCategory);
        Assert.Equal("Settings.SimplifiedSymbols", requested);
        Assert.Equal("Settings.SimplifiedSymbols", settings.RequestedSetting);
    }

    private sealed class TestSettingsPage(SettingsCategory category, int order) : ISettingsPage
    {
        public SettingsCategory Category => category;

        public int Order => order;

        public string Title => category.ToString();

        public string AutomationId => "Settings.Test." + category;

        public string? StatusText => null;

        public void OnShown()
        {
        }
    }
}
