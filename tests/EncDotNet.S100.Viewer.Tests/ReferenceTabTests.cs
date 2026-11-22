using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The right dock's Reference tab (#1650, symbol dictionary design note
/// §4.4, R7): <see cref="ReferenceNavigator"/>'s history, opening from the
/// activity and the page, every kind of entry against the bundled S-101
/// catalogue, the empty state, MCP <c>open_chart_reference</c> and the view
/// at the dock's 360 px.
/// </summary>
public sealed class ReferenceTabTests : IDisposable
{
    private static readonly SpecRef S101 = new("S-101", default);

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    private static ReferenceKey Key(ReferenceKind kind, string code) => new(S101, kind, code);

    // ---- The navigator ----

    [Fact]
    public void History_goes_back_and_forward_and_a_new_entry_drops_the_entries_ahead()
    {
        var nav = new ReferenceNavigator();
        int changed = 0, shown = 0;
        nav.Changed += (_, _) => changed++;
        nav.ShowRequested += (_, _) => shown++;
        Assert.Null(nav.Current);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.Back());

        var a = Key(ReferenceKind.FeatureType, "LateralBeacon");
        var b = Key(ReferenceKind.Symbol, "BCNLAT21");
        var c = Key(ReferenceKind.DisplaySetting, "SimplifiedSymbols");
        nav.Open(a);
        nav.Open(b);
        nav.Open(c);
        Assert.Equal(c, nav.Current);
        Assert.Equal((3, 3), (changed, shown));

        // The same entry again adds nothing, but still shows the tab.
        nav.Open(new ReferenceKey(S101, ReferenceKind.DisplaySetting, "simplifiedsymbols"));
        Assert.Equal(3, nav.History.Count);
        Assert.Equal((3, 4), (changed, shown));

        Assert.True(nav.Back());
        Assert.True(nav.Back());
        Assert.Equal(a, nav.Current);
        Assert.False(nav.CanGoBack);
        Assert.True(nav.CanGoForward);
        Assert.True(nav.Forward());
        Assert.Equal(b, nav.Current);

        // Branching: a new entry while back in the trail drops the one ahead.
        var d = Key(ReferenceKind.Attribute, "colour");
        nav.Open(d);
        Assert.Equal([a, b, d], nav.History);
        Assert.False(nav.CanGoForward);
        Assert.False(nav.Forward());
        Assert.True(nav.Back());
        Assert.Equal(b, nav.Current);
        Assert.True(nav.Forward());
        Assert.Equal(d, nav.Current);

        // The breadcrumb leads to the current entry; going to a crumb keeps the history.
        Assert.Equal([a, b, d], nav.Breadcrumb.Select(c => c.Key));
        Assert.True(nav.GoTo(0));
        Assert.Equal(a, nav.Current);
        Assert.Equal(3, nav.History.Count);
        Assert.Equal([a], nav.Breadcrumb.Select(c => c.Key));
    }

    [Fact]
    public void The_breadcrumb_and_history_are_capped()
    {
        var nav = new ReferenceNavigator();
        for (int i = 0; i < ReferenceNavigator.MaxHistory + 5; i++)
            nav.Open(Key(ReferenceKind.Symbol, $"SYM{i:D3}"));

        Assert.Equal(ReferenceNavigator.MaxHistory, nav.History.Count);
        Assert.Equal("SYM005", nav.History[0].Code);
        Assert.Equal(ReferenceNavigator.MaxCrumbs, nav.Breadcrumb.Count);
        Assert.Equal(nav.Current, nav.Breadcrumb[^1].Key);
    }

    [Theory]
    [InlineData("soundcharts://reference/S-101/FeatureType/LateralBeacon", ReferenceKind.FeatureType, "LateralBeacon")]
    [InlineData("S-101/FeatureType/LateralBeacon", ReferenceKind.FeatureType, "LateralBeacon")]
    [InlineData("soundcharts://reference/S-101/display_setting/SimplifiedSymbols", ReferenceKind.DisplaySetting, "SimplifiedSymbols")]
    [InlineData("  soundcharts://reference/s101/Symbol/BCNLAT21 ", ReferenceKind.Symbol, "BCNLAT21")]
    public void A_link_uses_the_keys_own_text_form(string link, ReferenceKind kind, string code)
    {
        Assert.True(ReferenceNavigator.TryParse(link, out var key));
        Assert.Equal("S-101", key.Spec.Name);
        Assert.Equal(kind, key.Kind);
        Assert.Equal(code, key.Code);

        var nav = new ReferenceNavigator();
        Assert.True(nav.Open(link));
        Assert.Equal(key, nav.Current);
        Assert.Equal($"soundcharts://reference/S-101/{kind}/{code}", ReferenceNavigator.LinkOf(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("soundcharts://reference/")]
    [InlineData("soundcharts://reference/S-101/Nonsense/X")]
    [InlineData("S-101/FeatureType")]
    public void A_link_that_names_no_key_opens_nothing(string link)
    {
        var nav = new ReferenceNavigator();
        Assert.False(nav.Open(link));
        Assert.Null(nav.Current);
    }

    // ---- The tab ----

    [Fact]
    public async Task Before_anything_is_opened_the_tab_shows_its_empty_state()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        using var tab = new ReferenceViewModel(new ReferenceNavigator(), f.Service, cache);

        Assert.True(tab.IsEmpty);
        Assert.False(tab.HasEntry);
        Assert.False(tab.IsLoading);
        Assert.False(tab.ShowNotFound);
        Assert.Empty(tab.Crumbs);
        Assert.False(tab.BackCommand.CanExecute(null));
        Assert.False(tab.ForwardCommand.CanExecute(null));

        // A key the catalogues don't have says so.
        tab.Navigator.Open(Key(ReferenceKind.Symbol, "NOSUCH01"));
        await tab.Loading;
        Assert.False(tab.IsEmpty);
        Assert.True(tab.ShowNotFound);
        Assert.Contains("S-101/Symbol/NOSUCH01", tab.NotFoundDescription, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_empty_state_points_to_Chart_reference()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        using var tab = new ReferenceViewModel(new ReferenceNavigator(), f.Service, cache);
        var shown = 0;
        tab.ShowChartReferenceCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => shown++);
        using var host = ViewHost.Show(new ReferenceView { DataContext = tab }, 360, 700);

        Assert.True(host.IsShown("Reference.Empty"));
        Assert.Contains("Ctrl+Shift+K", host.Find<TextBlock>("Reference.Empty.Description").Text, StringComparison.Ordinal);
        Assert.False(host.IsShown("Reference.Back"));
        Assert.False(host.IsShown("Reference.Entry"));
        host.Click(host.Find<Button>("Reference.OpenChartReference"));
        Assert.Equal(1, shown);
    }

    [Fact]
    public async Task Selecting_a_row_in_the_activity_opens_the_entry_in_the_tab_and_switches_the_dock()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var reference = new ChartReferenceViewModel(f.Service, cache) { OpenEntry = nav.Open };
        using var tab = new ReferenceViewModel(nav, f.Service, cache);
        var pick = new Stub();
        var pickTab = new ActivityTab<Stub, Border>(
            ShellLayoutViewModel.PickReportTabId, 10, "Object information", "Object information", () => new Border(), pick, false, null, TabDock.Right);
        var legendTab = new ActivityTab<Stub, Border>(
            ShellLayoutViewModel.LegendTabId, 20, "Legend", "Legend", () => new Border(), new Stub(), false, null, TabDock.Right);
        var referenceTab = new ActivityTab<ReferenceViewModel, ReferenceView>(
            ShellLayoutViewModel.ReferenceTabId, 30, "Reference", "Reference", () => new Border(), tab, false, null, TabDock.Right, autoOpenOnContentSignal: true);
        var layout = Layout(pickTab, legendTab, referenceTab);

        // The strip always shows all three, Reference last (legends D13, D22).
        Assert.Equal([ShellLayoutViewModel.PickReportTabId, ShellLayoutViewModel.LegendTabId, ShellLayoutViewModel.ReferenceTabId], layout.VisibleRightTabs.Select(t => t.Id));
        layout.SelectTab(ShellLayoutViewModel.PickReportTabId);
        layout.IsRightDockOpen = false;

        reference.Activate();
        await reference.Loading;
        reference.SelectedItem = reference.Rows.Single(r => r.Key.Code == "LateralBeacon");
        await tab.Loading;

        Assert.Equal(Key(ReferenceKind.FeatureType, "LateralBeacon"), nav.Current);
        Assert.Equal(ShellLayoutViewModel.ReferenceTabId, layout.SelectedRightTabId);
        Assert.True(layout.IsRightDockOpen);
        var entry = Assert.IsType<FeatureTypeEntryViewModel>(tab.Entry);
        Assert.Equal("Lateral beacon", entry.Name, ignoreCase: true);

        // Object information keeps its pick, one tab away.
        Assert.Same(pick, pickTab.ViewModel);
        layout.SelectTab(ShellLayoutViewModel.PickReportTabId);
        Assert.Equal(ShellLayoutViewModel.PickReportTabId, layout.SelectedRightTabId);

        // A link in the entry opens its entry in the tab, and back returns.
        entry.DrawnWith.Single(l => l.Code == "BCNLAT21").OpenCommand.Execute(null);
        await tab.Loading;
        Assert.Equal(ShellLayoutViewModel.ReferenceTabId, layout.SelectedRightTabId);
        Assert.IsType<SymbolEntryViewModel>(tab.Entry);
        Assert.Equal(2, tab.Crumbs.Count);
        Assert.Equal(entry.Name, tab.Crumbs[0].Name);
        Assert.True(tab.Crumbs[0].IsLink);
        Assert.True(tab.Crumbs[1].IsCurrent);
        Assert.True(tab.HasTrail);
        tab.BackCommand.Execute(null);
        await tab.Loading;
        Assert.IsType<FeatureTypeEntryViewModel>(tab.Entry);
        Assert.True(tab.ForwardCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_page_hosts_the_same_entry_view_and_its_links_stay_on_the_page()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var reference = new ChartReferenceViewModel(f.Service, cache) { OpenEntry = nav.Open };
        using var page = new ChartReferencePageViewModel(reference, f.Service);
        page.Show();
        await reference.Loading;

        page.SelectCommand.Execute(page.Tiles.Single(t => t.Key.Code == "LateralBeacon"));
        var entry = Assert.IsType<FeatureTypeEntryViewModel>(page.Entry);
        Assert.False(entry.CanShowInView); // The page covers the map.

        // A link selects the symbol's tile on the page, and never opens the tab.
        entry.DrawnWith.Single(l => l.Code == "BCNLAT21").OpenCommand.Execute(null);
        Assert.Equal(Key(ReferenceKind.Symbol, "BCNLAT21"), page.SelectedTile?.Key);
        Assert.IsType<SymbolEntryViewModel>(page.Entry);
        Assert.Null(nav.Current);
    }

    // ---- Every kind of entry, against the bundled S-101 catalogue ----

    [AvaloniaFact]
    public async Task A_feature_types_entry_has_its_codes_categories_Drawn_with_and_fallback()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = Assert.IsType<ChartReferenceIndex>(await f.Service.GetIndexAsync(S101));
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var map = new FakeMap { Count = 3 };
        var opened = new List<ReferenceKey>();
        var entry = Assert.IsType<FeatureTypeEntryViewModel>(ReferenceEntryViewModel.Create(
            ChartReferenceIndexes.Feature(index, "LateralBeacon"),
            new ReferenceEntryContext { Index = index, Artwork = f.Service.GetArtwork(S101), Glyphs = cache, Open = opened.Add, Map = map, SimplifiedSymbols = true }));
        entry.Start();

        Assert.Equal("Feature type", entry.KindLabel);
        Assert.Equal("LateralBeacon · S-57 BCNLAT", entry.CodeLine);
        Assert.True(entry.HasDescription);
        Assert.Equal(["Standard Display"], entry.Categories);
        Assert.StartsWith("P, Q, R, S – ", entry.Section, StringComparison.Ordinal);
        Assert.StartsWith("27020", entry.ViewingGroup, StringComparison.Ordinal);
        Assert.Equal(LeadGlyphKind.Symbol, entry.Lead!.Kind);
        Assert.Equal("BCNLAT21", entry.Lead.ItemId);

        // Drawn with: each symbol once, with its cases in words.
        var bcnlat21 = entry.DrawnWith.Single(l => l.Code == "BCNLAT21");
        Assert.StartsWith("BCNLAT21 · ", bcnlat21.Name, StringComparison.Ordinal);
        Assert.Contains("Simplified symbols is on · Colour is red · Beacon shape is a stake, pole, perch or post", bcnlat21.Cases);
        Assert.Equal(6, bcnlat21.Cases.Count);
        Assert.Equal("and 2 more cases", entry.DrawnWith.Single(l => l.Code == "BCNLAT15").Cases[^1]);

        // The fallback ? has its own line, not a row of Drawn with.
        Assert.True(entry.HasFallback);
        Assert.Equal("BCNDEF13", entry.Fallback!.Code);
        Assert.DoesNotContain(entry.DrawnWith, l => l.Code == "BCNDEF13");
        Assert.All(entry.DrawnByShared, l => Assert.Empty(l.Cases));

        // In view, with Find.
        Assert.True(entry.CanShowInView);
        Assert.Equal("3 in view", entry.InViewText);
        Assert.Contains("LateralBeacon", map.Counted.Single().FeatureTypes);
        Assert.Contains("BCNLAT", map.Counted.Single().FeatureTypes);
        entry.FindCommand.Execute(null);
        Assert.Single(map.Found);
        entry.Close();
        Assert.Single(map.Left);
        map.Count = 0;
        entry.RefreshInView();
        Assert.Equal("None in view", entry.InViewText);
        Assert.False(entry.FindCommand.CanExecute(null));

        // Links open their entries in the host.
        bcnlat21.OpenCommand.Execute(null);
        Assert.Equal([Key(ReferenceKind.Symbol, "BCNLAT21")], opened);

        // The large glyph and the glyph at chart size, in millimetres.
        entry.Start();
        await entry.GlyphsDrawn;
        Assert.IsAssignableFrom<Bitmap>(entry.HeroGlyph);
        Assert.IsAssignableFrom<Bitmap>(entry.ChartGlyph);
        Assert.Equal("At chart size, 1.65 × 5.32 mm", entry.ChartSize);
        Assert.Equal((6, 17), ((int)entry.ChartGlyphWidth, (int)entry.ChartGlyphHeight));
    }

    [AvaloniaFact]
    public async Task A_symbols_entry_has_three_palettes_its_setting_origin_and_Used_by_with_cases()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = Assert.IsType<ChartReferenceIndex>(await f.Service.GetIndexAsync(S101));
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var entry = Assert.IsType<SymbolEntryViewModel>(ReferenceEntryViewModel.Create(
            Item(index, ReferenceKind.Symbol, "BCNLAT21"),
            new ReferenceEntryContext { Index = index, Artwork = f.Service.GetArtwork(S101), Glyphs = cache, Open = _ => { } }));
        entry.Start();

        Assert.Equal("Symbol", entry.KindLabel);
        Assert.Equal("BCNLAT21", entry.CodeLine);
        Assert.Equal(["Day", "Dusk", "Night"], entry.Palettes.Select(p => p.Label));
        Assert.Equal("Simplified symbols", Assert.Single(entry.DependsOn).Name, ignoreCase: true);
        Assert.Contains("Simplified symbols is on", entry.DependsOn[0].Cases);
        Assert.Equal("S-52 Presentation Library (S52Preslib4.0)", entry.Origin);
        Assert.False(entry.IsNew);
        var usedBy = entry.UsedBy.Single(l => l.Code == "LateralBeacon");
        Assert.Contains("Simplified symbols is on · Colour is red · Beacon shape is a stake, pole, perch or post", usedBy.Cases);

        await entry.GlyphsDrawn;
        Assert.All(entry.Palettes, p => Assert.IsAssignableFrom<Bitmap>(p.Glyph));
        Assert.IsAssignableFrom<Bitmap>(entry.HeroGlyph);
        Assert.Equal("At chart size, 1.65 × 5.32 mm", entry.ChartSize);

        // A symbol from outside the S-52 Presentation Library is new in S-101.
        var fresh = index.Items.First(i => i is { Source: { Length: > 0 } s } && !s.StartsWith("S52", StringComparison.Ordinal) && i.Key.Kind == ReferenceKind.Symbol);
        var freshEntry = Assert.IsType<SymbolEntryViewModel>(ReferenceEntryViewModel.Create(fresh, new ReferenceEntryContext { Index = index, Glyphs = cache, Open = _ => { } }));
        Assert.True(freshEntry.IsNew);
        Assert.StartsWith("New in S-101", freshEntry.Origin, StringComparison.Ordinal);

        // A family lists its members; a member links to its family.
        var soundg = Assert.IsType<SymbolEntryViewModel>(ReferenceEntryViewModel.Create(
            Item(index, ReferenceKind.Symbol, "SOUNDG"), new ReferenceEntryContext { Index = index, Glyphs = cache, Open = _ => { } }));
        Assert.Equal("Symbol family", soundg.KindLabel);
        Assert.True(soundg.HasMembers);
        var member = index.Items.First(i => i.Family == "SOUNDG");
        var memberEntry = Assert.IsType<SymbolEntryViewModel>(ReferenceEntryViewModel.Create(member, new ReferenceEntryContext { Index = index, Glyphs = cache, Open = _ => { } }));
        Assert.Equal("SOUNDG", Assert.Single(memberEntry.Family).Code);
    }

    [Fact]
    public async Task An_attributes_entry_lists_its_values_and_the_feature_types_it_changes()
    {
        var index = await ChartReferenceIndexes.S101Async();
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        Assert.True(index.TryGet(Key(ReferenceKind.Attribute, "colour"), out var colour));
        var entry = Assert.IsType<AttributeEntryViewModel>(ReferenceEntryViewModel.Create(
            colour, new ReferenceEntryContext { Index = index, Glyphs = cache, Open = _ => { } }));
        entry.Start();

        Assert.Equal("Attribute", entry.KindLabel);
        Assert.Contains("S-57 COLOUR", entry.CodeLine, StringComparison.Ordinal);
        Assert.False(entry.ShowsGlyph);
        Assert.Contains(entry.Values, v => v.Code == "3" && v.Title == "3 · Red");
        Assert.Contains(entry.Changes, l => l.Code == "LateralBeacon");
        Assert.True(entry.HasDescription);
    }

    [Fact]
    public async Task A_display_settings_entry_has_its_default_current_state_and_Change()
    {
        var index = await ChartReferenceIndexes.S101Async();
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }) { SimplifiedSymbols = true };
        var changed = new List<string>();
        Assert.True(index.TryGet(Key(ReferenceKind.DisplaySetting, "SimplifiedSymbols"), out var simplified));
        var entry = Assert.IsType<DisplaySettingEntryViewModel>(ReferenceEntryViewModel.Create(
            simplified,
            new ReferenceEntryContext { Index = index, Glyphs = cache, Open = _ => { }, Settings = settings, ChangeSetting = changed.Add }));
        entry.Start();

        Assert.Equal("Display setting", entry.KindLabel);
        Assert.Equal("SimplifiedSymbols", entry.CodeLine);
        Assert.Matches("^Default: (On|Off)$", entry.Default);
        Assert.Equal("Now: On", entry.Current);
        Assert.Contains(entry.Changes, l => l.Code == "LateralBeacon");

        // The current state follows the settings while the entry is shown.
        settings.SimplifiedSymbols = false;
        Assert.Equal("Now: Off", entry.Current);

        Assert.True(entry.CanChange);
        entry.ChangeCommand.Execute(null);
        Assert.Equal(["SimplifiedSymbols"], changed);

        entry.Close();
        settings.SimplifiedSymbols = true;
        Assert.Equal("Now: Off", entry.Current);
    }

    // ---- MCP ----

    [Fact]
    public async Task Open_chart_reference_opens_an_entry_and_reports_the_one_shown()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var shown = 0;
        nav.ShowRequested += (_, _) => shown++;
        var tool = new OpenChartReferenceTool(f.Service, nav, new InlineInvoker());
        var ct = TestContext.Current.CancellationToken;

        // Nothing shown yet.
        var none = Value(await tool.InvokeAsync(null, ct));
        Assert.Null(none.Key);
        Assert.Empty(none.Trail);

        var beacon = Value(await tool.InvokeAsync("s-101/feature_type/lateralbeacon", ct));
        Assert.Equal("S-101/FeatureType/LateralBeacon", beacon.Key);
        Assert.Equal("feature_type", beacon.Kind);
        Assert.Equal("Lateral Beacon", beacon.Name, ignoreCase: true);
        Assert.Equal("soundcharts://reference/S-101/FeatureType/LateralBeacon", beacon.Link);
        Assert.False(beacon.CanGoBack);
        Assert.Equal(1, shown);

        var symbol = Value(await tool.InvokeAsync("soundcharts://reference/S-101/Symbol/BCNLAT21", ct));
        Assert.Equal("S-101/Symbol/BCNLAT21", symbol.Key);
        Assert.True(symbol.CanGoBack);
        Assert.Equal(["S-101/FeatureType/LateralBeacon", "S-101/Symbol/BCNLAT21"], symbol.Trail);

        // Without a key it only reports.
        nav.Back();
        var report = Value(await tool.InvokeAsync("", ct));
        Assert.Equal("S-101/FeatureType/LateralBeacon", report.Key);
        Assert.True(report.CanGoForward);
        Assert.Equal(2, shown);

        Assert.Equal("invalid_argument", Error(await tool.InvokeAsync("LateralBeacon", ct)).Code);
        Assert.Equal("chart_reference_entry_not_found", Error(await tool.InvokeAsync("S-101/Symbol/NOSUCH01", ct)).Code);
        Assert.Equal("chart_reference_product_not_found", Error(await tool.InvokeAsync("S-999/Symbol/X", ct)).Code);
        Assert.Equal(2, nav.History.Count);
    }

    // ---- The view, at the dock's 360 px ----

    [AvaloniaFact]
    public async Task The_tab_shows_an_entry_with_its_trail_at_360_px()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        using var host = ViewHost.Show(new ReferenceView { DataContext = tab }, 360, 900);

        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"));
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        await tab.Entry!.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();
        host.Settle();

        Assert.False(host.IsShown("Reference.Empty"));
        var entry = host.Find<ReferenceEntryView>("Reference.Entry");
        Assert.Equal("Feature type", host.Find<TextBlock>("Reference.Entry.Kind").Text);
        Assert.Equal("LateralBeacon · S-57 BCNLAT", host.Find<TextBlock>("Reference.Entry.Code").Text);
        Assert.False(host.Find<Button>("Reference.Back").IsEffectivelyEnabled);
        Assert.False(host.Find<Button>("Reference.Forward").IsEffectivelyEnabled);
        Assert.True(host.IsShown("Reference.Breadcrumb"));
        Assert.True(host.IsShown("Reference.Entry.ChartGlyph"));
        var links = host.FindAll<Button>("Reference.Link");
        Assert.Contains(links, b => Avalonia.Automation.AutomationProperties.GetName(b)!.StartsWith("BCNLAT21", StringComparison.Ordinal));

        // Everything wraps inside the 360 px dock.
        Assert.True(entry.Bounds.Width <= 360, $"entry {entry.Bounds.Width} px wide");
        Assert.All(
            entry.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => Assert.True(t.Bounds.Width <= 360, $"\"{t.Text}\" is {t.Bounds.Width} px wide"));

        // Following a link, then Alt+Left and Alt+Right.
        host.Click(links.First(b => Avalonia.Automation.AutomationProperties.GetName(b)!.StartsWith("BCNLAT21", StringComparison.Ordinal)));
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();
        Assert.Equal("Symbol", host.Find<TextBlock>("Reference.Entry.Kind").Text);
        Assert.True(host.Find<Button>("Reference.Back").IsEffectivelyEnabled);
        Assert.Single(host.FindAll<Button>("Reference.Breadcrumb.Item"));
        Assert.True(host.IsShown("Reference.Entry.Palettes"));

        host.Press(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();
        Assert.Equal(Key(ReferenceKind.FeatureType, "LateralBeacon"), nav.Current);
        Assert.Equal("Feature type", host.Find<TextBlock>("Reference.Entry.Kind").Text);

        host.Press(PhysicalKey.ArrowRight, RawInputModifiers.Alt);
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Key(ReferenceKind.Symbol, "BCNLAT21"), nav.Current);
    }

    private static PortrayalItemEntry Item(ChartReferenceIndex index, ReferenceKind kind, string code) =>
        Assert.IsType<PortrayalItemEntry>(index.TryGet(Key(kind, code), out var entry) ? entry : null);

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static ToolError Error<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetError(out var error));
        return error!;
    }

    private ShellLayoutViewModel Layout(params IActivityTab[] tabs)
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsPath };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        return new ShellLayoutViewModel(settings, tabs, timeline, action => action());
    }

    private sealed class Stub : ViewModelBase;

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class FakeMap : IChartReferenceMap
    {
        public event EventHandler? Changed { add { } remove { } }

        public int Count { get; set; }

        public List<ChartReferenceTarget> Counted { get; } = [];

        public List<ChartReferenceTarget> Found { get; } = [];

        public List<ChartReferenceTarget?> Left { get; } = [];

        public ChartReferenceDrawn DrawnInView(string spec) => ChartReferenceDrawn.None;

        public void Hover(ChartReferenceTarget target)
        {
        }

        public void Leave(ChartReferenceTarget? target) => Left.Add(target);

        public int CountInView(ChartReferenceTarget target)
        {
            Counted.Add(target);
            return Count;
        }

        public void Find(ChartReferenceTarget target) => Found.Add(target);
    }
}
