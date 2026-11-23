using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using EncDotNet.S100.Viewer.Views.Settings;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Chart reference terms and peeks (#1651, symbol dictionary design note
/// §4.2, §4.6, R8; legends D7, D19): the condition reader's term runs, the
/// peek's delay, pin and Escape, a click opening the entry, plain text for an
/// unknown key, terms in entries, the Legend tab's catalogue key rows and
/// Display settings, and the eXaminer link.
/// </summary>
public sealed class ReferenceTermTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new();

    public ReferenceTermTests() => ReferenceTerm.Clock = _clock;

    public void Dispose() => ReferenceTerm.Clock = TimeProvider.System;

    /// <summary>Moves the peek's clock on and runs what came due.</summary>
    private void Advance(ViewHost host, int milliseconds)
    {
        _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        host.Settle();
    }

    private static readonly SpecRef S101 = new("S-101", default);

    private static ReferenceKey Key(ReferenceKind kind, string code) => new(S101, kind, code);

    private sealed class Urls : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string url) => Opened.Add(url);
    }

    private static ReferenceTerms Terms(ChartReferenceServiceTests.Fixture f, ReferenceNavigator nav, bool examiner = true, Urls? urls = null) =>
        ReferenceTerms.For(
            f.Service,
            new ChartReferenceGlyphCache(PaletteType.Day),
            nav,
            new S100ExaminerLinkBuilder(new ViewerSettings { S100ExaminerLinksEnabled = examiner }),
            urls ?? new Urls());

    /// <summary>Pumps the dispatcher until <paramref name="done"/> holds, for up to five seconds.</summary>
    private static void Until(ViewHost host, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Settle();
            Thread.Sleep(10);
        }

        host.Settle();
        Assert.True(done(), "timed out");
    }

    // ---- The condition reader ----

    [Fact]
    public async Task The_condition_reader_keeps_the_attribute_and_value_keys_behind_each_word()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = await f.Service.GetIndexAsync(S101, TestContext.Current.CancellationToken);
        Assert.NotNull(index);
        var beacon = index.FeatureTypes.Single(e => e.Code == "LateralBeacon");

        var uses = beacon.Uses.Where(u => u.ItemId == "BCNLAT21").ToList();
        var cases = FeatureTypeEntryViewModel.CasesOf(uses, index, geometry: false, max: 10);
        var colour = cases.First(c => c.Text.StartsWith("Colour is red, green, red", StringComparison.Ordinal));

        // "Colour" names the attribute, and each "red" or "green" its listed value.
        var runs = colour.Runs;
        Assert.Equal("Colour", runs[0].Text);
        Assert.Equal((ReferenceKind.Attribute, "colour"), (runs[0].Key!.Value.Kind, runs[0].Key!.Value.Code));
        var red = runs.First(r => r.Text == "red");
        Assert.Equal(ReferenceKind.Attribute, red.Key!.Value.Kind);
        Assert.Equal("colour", red.Key.Value.Code);
        Assert.Equal("3", red.Value);
        Assert.Equal("4", runs.First(r => r.Text == "green").Value);
        Assert.Contains(runs, r => r.Text == "Beacon shape" && r.Key is { Kind: ReferenceKind.Attribute });
        Assert.Contains(runs, r => r.Text.Contains("is ", StringComparison.Ordinal) && !r.IsTerm);
        Assert.Equal(colour.Text, string.Concat(runs.Select(r => r.Text)));

        // The shared "When simplified symbols is on:" keeps its setting as a term.
        var lead = Assert.Single(FeatureTypeEntryViewModel.CasesOf(uses, index, false, 10), c => c.Text.StartsWith("When ", StringComparison.Ordinal));
        Assert.Contains(lead.Runs, r => r.Text == "simplified symbols" && r.Key is { Kind: ReferenceKind.DisplaySetting, Code: "SimplifiedSymbols" });

        // The strings are as before.
        Assert.Equal(
            RuleConditionText.Describe(uses[0].Conditions, index),
            TermText.Join(RuleConditionText.Separator, RuleConditionText.DescribeParts(uses[0].Conditions, index)).Text);
    }

    [Fact]
    public void Term_text_formats_joins_splits_and_lowers_without_losing_its_terms()
    {
        var colour = TermText.Term("Colour", Key(ReferenceKind.Attribute, "colour"));
        var text = TermText.Format("{0} is {1}", colour, TermText.Term("red", Key(ReferenceKind.Attribute, "colour"), "3"));
        Assert.Equal("Colour is red", text.Text);
        Assert.Equal(["Colour", " is ", "red"], text.Runs.Select(r => r.Text));

        var joined = TermText.Join(" · ", [text, "Always"]);
        Assert.Equal("Colour is red · Always", joined.Text);
        var parts = joined.Split(" · ");
        Assert.Equal(["Colour is red", "Always"], parts.Select(p => p.Text));
        Assert.True(parts[0].HasTerms);
        Assert.Equal("colour is red", parts[0].LowerFirst().Text);
        Assert.True(parts[0].LowerFirst().Runs[0].IsTerm);
        Assert.Equal("RADAR", TermText.Plain("RADAR").LowerFirst().Text);
    }

    // ---- The control ----

    [AvaloniaFact]
    public void A_peek_shows_after_500_ms_pins_with_Space_lets_Tab_through_and_closes_on_Escape()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var before = new Button { Content = "Before" };
        var term = new ReferenceTerm { Text = "Lateral beacon", Link = "S-101/FeatureType/LateralBeacon" };
        var after = new Button { Content = "After" };
        var panel = new StackPanel { Children = { before, term, after } };
        ReferenceTerm.SetTerms(panel, Terms(f, nav));
        using var host = ViewHost.Show(panel, 400, 400);
        Until(host, () => term.IsTerm);

        // A term: focusable, dotted, described by its peek's text.
        Assert.True(term.Focusable);
        Assert.Contains(":term", term.Classes);
        var text = term.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Text");
        Assert.NotNull(text.TextDecorations);
        Assert.NotEmpty(text.TextDecorations!.Single().StrokeDashArray!);
        var description = Avalonia.Automation.AutomationProperties.GetHelpText(term);
        Assert.StartsWith("Feature type, Lateral Beacon: ", description, StringComparison.OrdinalIgnoreCase);

        // Keyboard focus: nothing at once, nor at 499 ms; the peek at 500 ms.
        term.Focus(NavigationMethod.Tab);
        host.Settle();
        Assert.False(term.IsPeekOpen);
        Advance(host, 499);
        Assert.False(term.IsPeekOpen);
        Advance(host, 1);
        Assert.True(term.IsPeekOpen);
        Assert.False(term.IsPinned);
        var peek = term.Peek!;
        Assert.Equal("Feature type", peek.KindLabel);
        Assert.Equal("Lateral Beacon", peek.Name, ignoreCase: true);
        Assert.True(peek.HasDefinition);
        Assert.True(peek.ShowsGlyph);
        Assert.True(peek.HasExaminer);
        peek.GlyphDrawn.Wait(TestContext.Current.CancellationToken);
        Until(host, () => peek.Glyph is not null);
        Assert.Equal("Reference.Peek", Avalonia.Automation.AutomationProperties.GetAutomationId(term.PeekView!));

        // Escape closes it.
        host.Press(PhysicalKey.Escape);
        Assert.False(term.IsPeekOpen);
        Assert.True(term.IsFocused);

        // Space pins it and moves focus to its first link: Open in reference.
        host.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        host.Settle();
        Assert.True(term.IsFocused); // until Space is up, so its key-up clicks nothing
        host.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        host.Settle();
        Assert.True(term.IsPeekOpen);
        Assert.True(term.IsPinned);
        Assert.Null(nav.Current); // Space doesn't click
        var open = host.Find<Button>("Reference.Peek.Open");
        var examiner = host.Find<Button>("Reference.Peek.Examiner");
        Until(host, () => open.IsFocused);

        // Tab moves through its links and out, to what follows the term.
        var popup = TopLevel.GetTopLevel(open)!;
        popup.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        host.Settle();
        Assert.True(examiner.IsFocused);
        popup.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        host.Settle();
        Assert.False(term.IsPeekOpen);
        Assert.True(after.IsFocused);

        // Shift+Tab before the first link, and Escape, come back to the term.
        term.Focus(NavigationMethod.Tab);
        host.Press(PhysicalKey.Space);
        Until(host, () => open.IsFocused);
        TopLevel.GetTopLevel(open)!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        host.Settle();
        Assert.False(term.IsPeekOpen);
        Assert.True(term.IsFocused);
        host.Press(PhysicalKey.Space);
        Until(host, () => open.IsFocused);
        TopLevel.GetTopLevel(open)!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        host.Settle();
        Assert.False(term.IsPeekOpen);
        Assert.True(term.IsFocused);
    }

    [AvaloniaFact]
    public void Hover_shows_the_peek_after_the_delay_and_leaving_closes_it()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var term = new ReferenceTerm { Text = "BCNLAT21", Link = "S-101/Symbol/BCNLAT21", Margin = new Avalonia.Thickness(20) };
        var away = new Border { Height = 200, Background = Avalonia.Media.Brushes.Transparent };
        var panel = new StackPanel { Children = { term, away } };
        ReferenceTerm.SetTerms(panel, Terms(f, nav));
        using var host = ViewHost.Show(panel, 400, 400);
        Until(host, () => term.IsTerm);

        host.Window.MouseMove(term.TranslatePoint(new Avalonia.Point(5, 5), host.Window)!.Value);
        host.Settle();
        Advance(host, 400);
        Assert.False(term.IsPeekOpen);
        Advance(host, 100);
        Assert.True(term.IsPeekOpen);

        // A symbol's peek: its kind, its meaning, and no eXaminer page.
        Assert.Equal("Symbol", term.Peek!.KindLabel);
        Assert.False(term.Peek.HasExaminer);
        Assert.False(term.PeekView!.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Reference.Peek.Examiner").IsVisible);

        host.Window.MouseMove(away.TranslatePoint(new Avalonia.Point(200, 190), host.Window)!.Value);
        host.Settle();
        Assert.True(term.IsPeekOpen); // the pointer has a moment to reach the peek
        Advance(host, 300);
        Assert.False(term.IsPeekOpen);
    }

    [AvaloniaFact]
    public void A_click_on_a_term_opens_its_entry_and_so_does_Open_in_reference()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var term = new ReferenceTerm { Text = "Colour", Link = "S-101/Attribute/colour" };
        var panel = new StackPanel { Children = { term } };
        ReferenceTerm.SetTerms(panel, Terms(f, nav));
        using var host = ViewHost.Show(panel, 400, 400);
        Until(host, () => term.IsTerm);

        host.Click(term);
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), nav.Current);

        // From the peek.
        var other = new ReferenceTerm { Text = "Simplified symbols", Link = "S-101/DisplaySetting/SimplifiedSymbols" };
        panel.Children.Add(other);
        Until(host, () => other.IsTerm);
        other.ShowPeek(pin: false);
        host.Settle();
        host.Click(host.FindAll<Button>("Reference.Peek.Open").Single(b => b.IsEffectivelyVisible));
        Assert.Equal(Key(ReferenceKind.DisplaySetting, "SimplifiedSymbols"), nav.Current);
        Assert.False(other.IsPeekOpen);
        Assert.Equal(2, nav.History.Count);
    }

    [AvaloniaFact]
    public void A_term_for_a_key_the_index_does_not_hold_is_plain_text()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var known = new ReferenceTerm { Text = "Lateral beacon", Link = "S-101/FeatureType/LateralBeacon" };
        var unknown = new ReferenceTerm { Text = "NOSUCH01", Link = "S-101/Symbol/NOSUCH01" };
        var unparsed = new ReferenceTerm { Text = "Not a key", Link = "nonsense" };
        var marked = new ReferenceTerm { Text = "Lateral beacon", Link = "S-101/FeatureType/LateralBeacon", IsKnown = false };
        var panel = new StackPanel { Children = { known, unknown, unparsed, marked } };
        ReferenceTerm.SetTerms(panel, Terms(f, nav));
        using var host = ViewHost.Show(panel, 400, 400);
        Until(host, () => known.IsTerm);

        Assert.All([unknown, unparsed, marked], t =>
        {
            Assert.False(t.IsTerm);
            Assert.False(t.Focusable);
            Assert.DoesNotContain(":term", t.Classes);
            Assert.Null(t.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Text").TextDecorations);
            t.ShowPeek(pin: true);
            Assert.False(t.IsPeekOpen);
            host.Click(t);
        });
        Assert.Null(nav.Current);
    }

    // ---- Inside entries ----

    [AvaloniaFact]
    public async Task Names_and_the_words_of_Drawn_with_are_terms_in_an_entry()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache, post: a => Dispatcher.UIThread.Post(a))
        {
            Terms = Terms(f, nav),
        };
        using var host = ViewHost.Show(new ReferenceView { DataContext = tab }, 360, 900);

        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"));
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();

        var terms = host.FindAll<ReferenceTerm>("Reference.Term").Where(t => t.IsEffectivelyVisible).ToList();
        Assert.All(terms, t => Assert.True(t.IsTerm, $"\"{t.Text}\" is plain"));
        Assert.Contains(terms, t => t.Key is { Kind: ReferenceKind.Symbol, Code: "BCNLAT21" });
        var colour = terms.First(t => t.Text == "Colour");
        Assert.Equal(ReferenceKind.Attribute, colour.Key!.Value.Kind);
        var red = terms.First(t => t.Text == "red");
        Assert.Equal("3", red.Value);
        Assert.Contains(terms, t => t.Key is { Kind: ReferenceKind.DisplaySetting, Code: "SimplifiedSymbols" });

        // Terms are in the text colour, never blue.
        var accent = (Avalonia.Media.ISolidColorBrush?)host.Window.FindResource("AccentBrush");
        Assert.All(terms, t => Assert.NotEqual(accent?.Color, (t.GetVisualDescendants().OfType<TextBlock>().First().Foreground as Avalonia.Media.ISolidColorBrush)?.Color));

        // The word "red" peeks as a listed value and opens its attribute.
        red.ShowPeek(pin: false);
        host.Settle();
        Assert.Equal("Listed value of Colour", red.Peek!.KindLabel);
        Assert.Equal("Red", red.Peek.Name, ignoreCase: true);
        Assert.True(red.Peek.HasExaminer);
        red.ClosePeek();
        host.Click(colour);
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();
        Assert.IsType<AttributeEntryViewModel>(tab.Entry);
    }

    [AvaloniaFact]
    public async Task Punctuation_sits_tight_against_the_term_before_it()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var nav = new ReferenceNavigator();
        using var tab = new ReferenceViewModel(nav, f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        using var host = ViewHost.Show(new ReferenceView { DataContext = tab }, 360, 900);
        nav.Open(Key(ReferenceKind.FeatureType, "LateralBeacon"));
        await tab.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();

        // The rendered text, run by run: no space before a comma.
        var texts = host.Window.GetVisualDescendants().OfType<ReferenceTermText>().ToList();
        static string Rendered(ReferenceTermText t) => string.Concat(t.Inlines!.Select(i => i switch
        {
            Avalonia.Controls.Documents.Run run => run.Text,
            Avalonia.Controls.Documents.InlineUIContainer { Child: ReferenceTerm term } => term.Text,
            _ => string.Empty,
        }));
        var colour = texts.First(t => Rendered(t).StartsWith("Colour is red, green, red · Beacon shape is ", StringComparison.Ordinal));
        Assert.Equal("Colour is red, green, red · Beacon shape is a stake, pole, perch or post", Rendered(colour));
        Assert.All(texts, t => Assert.DoesNotContain(" ,", Rendered(t), StringComparison.Ordinal));
        Assert.Contains(texts, t => Rendered(t) == "When simplified symbols is on:");

        // Laid out: each term's box is as wide as its words, not rounded up,
        // and the gap from "red" to "green" is exactly the width of ", ", so
        // the comma's left edge touches the end of "red".
        var terms = colour.Inlines!.OfType<Avalonia.Controls.Documents.InlineUIContainer>().Select(c => (ReferenceTerm)c.Child).ToList();
        var red = terms.First(t => t.Text == "red");
        var green = terms.First(t => t.Text == "green");
        double Left(Control c) => c.TranslatePoint(new Point(0, 0), colour)!.Value.X;
        using var comma = new Avalonia.Media.TextFormatting.TextLayout(
            ", ",
            new Avalonia.Media.Typeface(colour.FontFamily, colour.FontStyle, colour.FontWeight),
            colour.FontSize,
            colour.Foreground);
        Assert.Equal(comma.WidthIncludingTrailingWhitespace, Left(green) - (Left(red) + red.Bounds.Width), 0.5);
        Assert.All(terms, t =>
        {
            using var words = new Avalonia.Media.TextFormatting.TextLayout(
                t.Text,
                new Avalonia.Media.Typeface(colour.FontFamily, colour.FontStyle, colour.FontWeight),
                colour.FontSize,
                colour.Foreground);
            Assert.True(Math.Abs(words.WidthIncludingTrailingWhitespace - t.Bounds.Width) < 0.1, $"\"{t.Text}\" is {t.Bounds.Width} px for {words.WidthIncludingTrailingWhitespace} px of text");
        });
    }

    // ---- The Legend tab (D7, D19) ----

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle class=\"sCHBLK\" cx=\"5\" cy=\"5\" r=\"4\"/></svg>";

    private sealed class PointGeometry(Dictionary<string, FeatureGeometry> points) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => points.GetValueOrDefault(featureReference);
    }

    private sealed class Features(IReadOnlyList<(string Ref, string Type)> features) : IDatasetProcessor
    {
        public SpecRef Spec { get; } = new("S-101", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public IEnumerable<FeatureSummary> EnumerateFeatures() =>
            features.Select(f => new FeatureSummary { FeatureRef = f.Ref, FeatureType = f.Type });
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    [AvaloniaFact]
    public void A_catalogue_key_row_is_a_term_and_still_rings_its_features_with_a_peek_showing()
    {
        var legend = new LegendServiceTests.Fixture();
        var dataset = legend.Add("101GB00TEST.000", "S-101", 50, -2, 51, -1);
        var points = new[] { ("LateralBuoy", "BOYLAT13", 50.5, -1.5), ("LateralBuoy", "BOYLAT13", 50.4, -1.4) };
        var refs = points.Select((p, i) => (Ref: $"{dataset.Id.Value}:{i}", p.Item1, p.Item2, p.Item3, p.Item4)).ToList();
        var subLayer = new VectorSubLayer
        {
            LayerKey = "s101.linework",
            LayerName = "Linework",
            Instructions = [.. refs.Select(r => (DrawingInstruction)new PointInstruction { FeatureReference = r.Ref, SymbolReference = r.Item3 })],
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "s101.linework",
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = new PointGeometry(refs.ToDictionary(r => r.Ref, r => new FeatureGeometry { Type = GeometryType.Point, Coordinates = [new(r.Item4, r.Item5)] })),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = dataset.Id.Value,
            Info = "test",
            SymbolProvider = _ => Svg,
            SymbolTitleProvider = _ => "lateral buoy, port-hand",
        };
        ((Dictionary<DatasetEntry, IDatasetProcessor>)legend.Loader.Processors).Add(dataset, new Features([.. refs.Select(r => (r.Ref, r.Item2))]));
        legend.Viewport.Publish(50, -2, 51, -1);
        legend.Loader.SetEntries(new LayerStackEntry(
            new MemoryLayer(dataset.Id.Value),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, dataset.Id.Value, subLayer.SourceFeatureType)));
        using var service = legend.Create();
        service.IsTabShown = true;
        service.Refresh();
        using var find = new LegendFindService(service, legend.Viewport, legend.Clock, action => action());
        using var catalogues = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        using var tab = new LegendViewModel(service, new ClockViewTime(new FakeTimeProvider()), new Utc(), find)
        {
            Terms = Terms(catalogues, nav),
        };
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, 360, 600);

        var row = Assert.Single(tab.Sections.Single().Rows);
        Assert.Equal(Key(ReferenceKind.Symbol, "BOYLAT13"), row.TermKey);
        var term = host.Find<ReferenceTerm>("Reference.Term");
        Until(host, () => term.IsTerm);
        Assert.Equal("Lateral buoy, port-hand", term.Text);

        // Hover rings the row's features (D19)…
        host.Window.MouseMove(term.TranslatePoint(new Avalonia.Point(4, 4), host.Window)!.Value);
        host.Settle();
        legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(2, find.Current?.MatchCount);

        // …and the peek after 500 ms doesn't stop that.
        Advance(host, 500);
        Assert.True(term.IsPeekOpen);
        legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(2, find.Current?.MatchCount);
        Assert.NotNull(find.HoveredRow);

        // A click opens the symbol's entry.
        host.Click(term);
        Assert.Equal(Key(ReferenceKind.Symbol, "BOYLAT13"), nav.Current);
    }

    // ---- Display settings ----

    [AvaloniaFact]
    public void A_Display_settings_name_opens_its_display_setting_entry()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = Path.Combine(Path.GetTempPath(), $"terms-{Guid.NewGuid():N}.json") });
        var page = new ChartSettingsPageViewModel(settings, new TideStationListViewModel(TideTestLists.Missing()), Terms(f, nav));
        using var host = ViewHost.Show(new ChartSettingsPage { DataContext = page }, 700, 2400);

        var terms = host.FindAll<ReferenceTerm>("Reference.Term");
        Until(host, () => terms.All(t => t.IsTerm));
        Assert.Equal(
            ["SafetyContour", "SafetyDepth", "ShallowContour", "DeepContour", "FourShades", "ShallowWaterDangers", "PlainBoundaries", "SimplifiedSymbols", "FullLightLines", "RadarOverlay", "IgnoreScaleMinimum", "NationalLanguage"],
            terms.Select(t => t.Key!.Value.Code));
        Assert.All(terms, t => Assert.Equal(ReferenceKind.DisplaySetting, t.Key!.Value.Kind));

        var plain = terms.Single(t => t.Key!.Value.Code == "PlainBoundaries");
        host.Click(plain);
        Assert.Equal(Key(ReferenceKind.DisplaySetting, "PlainBoundaries"), nav.Current);
    }

    // ---- The eXaminer ----

    [AvaloniaFact]
    public void The_eXaminer_link_shows_only_while_examiner_links_are_on()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var urls = new Urls();
        var on = new ReferenceTerm { Text = "Colour", Link = "S-101/Attribute/colour" };
        var onPanel = new StackPanel { Children = { on } };
        ReferenceTerm.SetTerms(onPanel, Terms(f, nav, examiner: true, urls));
        var off = new ReferenceTerm { Text = "Colour", Link = "S-101/Attribute/colour" };
        var offPanel = new StackPanel { Children = { off } };
        ReferenceTerm.SetTerms(offPanel, Terms(f, nav, examiner: false, urls));
        using var host = ViewHost.Show(new StackPanel { Children = { onPanel, offPanel } }, 400, 600);
        Until(host, () => on.IsTerm && off.IsTerm);

        off.ShowPeek(pin: false);
        host.Settle();
        Assert.False(off.Peek!.HasExaminer);
        Assert.False(off.PeekView!.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Reference.Peek.Examiner").IsVisible);
        off.ClosePeek();

        on.ShowPeek(pin: false);
        host.Settle();
        Assert.True(on.Peek!.HasExaminer);
        var link = on.PeekView!.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Reference.Peek.Examiner");
        Assert.True(link.IsEffectivelyVisible);
        host.Click(link);
        Assert.Equal(["https://s100examiner.com/?catalog=S-101&attribute=colour"], urls.Opened.Select(u => u.Replace("www.", string.Empty, StringComparison.Ordinal)));
        Assert.False(on.IsPeekOpen);
        Assert.Null(nav.Current);
    }
}
