using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Terms in the pick (#1678, symbol dictionary design note §4.3, R9):
/// Object information's feature type, attribute names and listed values are
/// chart reference terms, an S-57 pick maps to S-101 entries through the
/// feature catalogue's aliases, F1 and Open in reference open the feature
/// type's entry, the per-attribute eXaminer buttons are gone, and Copy
/// identity keeps the codes.
/// </summary>
public sealed class PickReferenceTermsTests
{
    private static readonly SpecRef S101 = new("S-101", default);

    private static ReferenceKey Key(ReferenceKind kind, string code) => new(S101, kind, code);

    private static PickAttribute Leaf(string code, string value, string? name = null, string? display = null) =>
        new() { Code = code, Name = name, RawValue = value, DisplayValue = display };

    /// <summary>Object information over the bundled S-101 chart reference, its index read before the pick.</summary>
    private sealed class Pick : IDisposable
    {
        private readonly ChartReferenceServiceTests.Fixture _fixture = new("S-101");

        public Pick(bool examiner = false)
        {
            Terms = ReferenceTerms.For(
                _fixture.Service,
                new ChartReferenceGlyphCache(PaletteType.Day),
                Navigator,
                new S100ExaminerLinkBuilder(new ViewerSettings { S100ExaminerLinksEnabled = examiner }),
                new NoUrls());
            var links = new S100ExaminerLinkBuilder(new ViewerSettings
            {
                S100ExaminerLinksEnabled = examiner,
                S100ExaminerBaseUrl = ViewerSettings.DefaultS100ExaminerBaseUrl,
            });
            Report = TestPickReport.Create(examinerLinks: TestPickReport.Examiner(links, new NoUrls()));
            Report.Terms = Terms;
        }

        public ReferenceNavigator Navigator { get; } = new();

        public ReferenceTerms Terms { get; }

        public PickReportViewModel Report { get; }

        public async Task<Pick> ReadyAsync()
        {
            Assert.NotNull(await Terms.IndexAsync(S101));
            return this;
        }

        public void Dispose() => _fixture.Dispose();
    }

    private sealed class NoUrls : IUrlOpener
    {
        public void Open(string url) { }
    }

    private static void S101Beacon(PickReportViewModel report) =>
        report.SetPick("LateralBeacon", "Lateral Beacon", "7", "101US00TEST.000", "S-101",
        [
            Leaf("colour", "3", "Colour", "Red"),
            Leaf("categoryOfLateralMark", "1", "Category of Lateral Mark", "Port-Hand Lateral Mark"),
            new PickAttribute
            {
                Code = "information",
                Name = "Information",
                RawValue = string.Empty,
                Children = [Leaf("text", "Unlit", "Text")],
            },
        ]);

    private static void S57Beacon(PickReportViewModel report) =>
        // An S-57 cell drawn with the S-101 catalogue, with its acronyms left as encoded.
        report.SetPick("BCNLAT", null, "12", "US5MA1BO.000", "S-57",
        [
            Leaf("COLOUR", "3,1"),
            Leaf("CATLAM", "1"),
        ]);

    [Fact]
    public async Task An_S101_pick_has_terms_for_its_type_attributes_and_values()
    {
        using var pick = await new Pick().ReadyAsync();
        var vm = pick.Report;

        S101Beacon(vm);

        Assert.Equal(Key(ReferenceKind.FeatureType, "LateralBeacon"), vm.FeatureTypeKey);
        Assert.Equal("Lateral Beacon", vm.PrimaryLabel);
        Assert.Equal(vm.FeatureTypeKey, vm.PrimaryLabelKey);
        Assert.True(vm.HasFeatureTypeEntry);

        var colour = vm.AttributeRows[0];
        Assert.Equal("Colour", colour.Name);
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), colour.NameKey);
        Assert.Equal("Red", colour.ValueText);
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), colour.ValueKey);
        Assert.Equal("3", colour.ValueCode);
        Assert.Equal("1", vm.AttributeRows[1].ValueCode);

        // A complex attribute isn't an entry; its sub-attributes are terms.
        var information = vm.AttributeRows[2];
        Assert.Null(information.NameKey);
        Assert.Equal(Key(ReferenceKind.Attribute, "text"), Assert.Single(information.Children).NameKey);
        // Free text names no listed value.
        Assert.Null(information.Children[0].ValueKey);
    }

    [Fact]
    public async Task A_named_feature_puts_the_term_on_its_class_pill()
    {
        using var pick = await new Pick().ReadyAsync();
        var vm = pick.Report;

        vm.SetPick("LateralBeacon", "Lateral Beacon", "7", "f.000", "S-101", [Leaf("featureName", "Bishop", "Feature Name")]);

        Assert.True(vm.HasSecondaryLabel);
        Assert.Equal("Lateral Beacon", vm.SecondaryLabel);
        Assert.Null(vm.PrimaryLabelKey);
        Assert.Equal(Key(ReferenceKind.FeatureType, "LateralBeacon"), vm.SecondaryLabelKey);
    }

    [Fact]
    public async Task An_S57_pick_maps_to_S101_Lateral_Beacon_and_Colour()
    {
        using var pick = await new Pick().ReadyAsync();
        var vm = pick.Report;

        S57Beacon(vm);

        Assert.Equal(Key(ReferenceKind.FeatureType, "LateralBeacon"), vm.FeatureTypeKey);
        Assert.Equal("Lateral Beacon", vm.PrimaryLabel);

        var colour = vm.AttributeRows[0];
        Assert.Equal("Colour", colour.Name);
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), colour.NameKey);
        Assert.True(colour.HasValueList);
        Assert.Equal("Red, White", colour.ValueText);
        var values = colour.ValueTerms!.Runs.Where(r => r.IsTerm).ToList();
        Assert.Equal(["3", "1"], values.Select(r => r.Value));
        Assert.All(values, r => Assert.Equal(Key(ReferenceKind.Attribute, "colour"), r.Key));

        var category = vm.AttributeRows[1];
        Assert.Equal("Category of Lateral Mark", category.Name);
        Assert.Equal(Key(ReferenceKind.Attribute, "categoryOfLateralMark"), category.ValueKey);
        Assert.Equal("1", category.ValueCode);
    }

    [Fact]
    public void An_S57_pick_reads_the_chart_reference_of_the_product_it_is_drawn_with()
    {
        Assert.Equal("S-101", PickTerms.SpecOf(new PickHit { FeatureRef = "1", FeatureType = "BCNLAT", ProductSpec = "S-57" })?.Name);
        Assert.Equal("S-124", PickTerms.SpecOf(new PickHit { FeatureRef = "1", FeatureType = "NavwarnPart", ProductSpec = "S-124" })?.Name);
        Assert.Null(PickTerms.SpecOf(new PickHit { FeatureRef = "1", FeatureType = "x" }));
    }

    [Fact]
    public async Task An_unknown_code_stays_plain()
    {
        using var pick = await new Pick().ReadyAsync();
        var vm = pick.Report;

        vm.SetPick("NoSuchType", null, "1", "f.000", "S-57", [Leaf("XYZABC", "7"), Leaf("COLOUR", "99")]);

        Assert.Null(vm.FeatureTypeKey);
        Assert.False(vm.HasFeatureTypeEntry);
        Assert.False(vm.OpenInReferenceCommand.CanExecute(null));
        Assert.Equal("NoSuchType", vm.PrimaryLabel);
        Assert.Null(vm.AttributeRows[0].NameKey);
        Assert.Null(vm.AttributeRows[0].ValueKey);
        // The attribute is known; a code that isn't one of its listed values stays plain.
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), vm.AttributeRows[1].NameKey);
        Assert.Null(vm.AttributeRows[1].ValueKey);
        Assert.False(vm.AttributeRows[1].HasValueList);
        Assert.Equal("99", vm.AttributeRows[1].ValueText);
    }

    [Fact]
    public async Task A_product_with_no_index_keeps_plain_names()
    {
        using var pick = new Pick();
        var vm = pick.Report;

        vm.SetPick("BathymetryCoverage", null, "1", "102.h5", "S-102", [Leaf("depth", "12.3", "Depth")]);
        // The read finds no index (the fixture has only S-101's catalogues).
        Assert.Null(await pick.Terms.IndexAsync(new SpecRef("S-102", default)));

        Assert.Null(vm.FeatureTypeKey);
        Assert.Null(vm.AttributeRows[0].NameKey);
    }

    [Fact]
    public async Task Terms_resolve_once_the_index_is_read()
    {
        using var pick = new Pick();
        var vm = pick.Report;

        S101Beacon(vm);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (vm.FeatureTypeKey is null && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Equal(Key(ReferenceKind.FeatureType, "LateralBeacon"), vm.FeatureTypeKey);
        Assert.Equal(Key(ReferenceKind.Attribute, "colour"), vm.AttributeRows[0].NameKey);
    }

    [Fact]
    public async Task Copy_identity_still_has_the_codes_and_S57_acronyms()
    {
        using var pick = await new Pick().ReadyAsync();
        var vm = pick.Report;
        string? copied = null;
        vm.CopyIdentityRequested += (_, text) => copied = text;

        S57Beacon(vm);
        vm.CopyIdentityCommand.Execute(null);

        Assert.NotNull(copied);
        Assert.Contains("BCNLAT (LateralBeacon)", copied, StringComparison.Ordinal);
        Assert.Contains("COLOUR (colour) = 3,1", copied, StringComparison.Ordinal);
        Assert.Contains("CATLAM (categoryOfLateralMark) = 1", copied, StringComparison.Ordinal);

        S101Beacon(vm);
        vm.CopyIdentityCommand.Execute(null);
        Assert.Contains("LateralBeacon (BCNLAT)", copied, StringComparison.Ordinal);
        Assert.Contains("colour (COLOUR) = 3", copied, StringComparison.Ordinal);
        Assert.Contains("\ninformation\n  text (INFORM) = Unlit", copied, StringComparison.Ordinal);
    }

    // ---- The view ----

    [AvaloniaFact]
    public async Task The_rows_show_names_and_values_as_terms_without_codes()
    {
        using var pick = await new Pick().ReadyAsync();
        S57Beacon(pick.Report);
        using var host = ViewHost.Show(new PickReportView { DataContext = pick.Report }, width: 420, height: 900);

        var terms = host.FindAll<ReferenceTerm>("Reference.Term").Where(t => t.IsEffectivelyVisible).ToList();
        Assert.Contains(terms, t => t.Text == "Lateral Beacon" && t.IsTerm);
        Assert.Contains(terms, t => t.Text == "Colour" && t.IsTerm);
        Assert.Contains(terms, t => t.Text == "Category of Lateral Mark" && t.IsTerm);
        // The colour list is one term per value.
        var list = host.Window.GetVisualDescendants().OfType<ReferenceTermText>().Single(t => t.IsEffectivelyVisible);
        Assert.Equal("Red, White", list.Terms?.Text);
        // Terms are focusable, and describe their entry.
        var colour = terms.Single(t => t.Text == "Colour");
        Assert.True(colour.Focusable);
        Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetHelpText(colour)));

        // The codes have left the rows.
        var words = host.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.DoesNotContain("COLOUR", words);
        Assert.DoesNotContain("CATLAM", words);
    }

    [AvaloniaFact]
    public async Task Open_in_reference_and_F1_open_the_entry_and_keep_the_pick()
    {
        using var pick = await new Pick().ReadyAsync();
        S101Beacon(pick.Report);
        var hit = pick.Report.SelectedHit;
        using var host = ViewHost.Show(new PickReportView { DataContext = pick.Report }, width: 420, height: 900);
        var beacon = Key(ReferenceKind.FeatureType, "LateralBeacon");

        var open = host.Find<Button>("PickReport.OpenInReference");
        Assert.True(open.IsEffectivelyVisible);
        Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(open)));
        host.Click(open);

        Assert.Equal(beacon, pick.Navigator.Current);
        Assert.True(pick.Report.HasPick);
        Assert.Same(hit, pick.Report.SelectedHit);

        // F1 with Object information focused opens it again.
        pick.Navigator.Open(Key(ReferenceKind.Attribute, "colour"));
        open.Focus();
        host.Press(PhysicalKey.F1);

        Assert.Equal(beacon, pick.Navigator.Current);
        Assert.Same(hit, pick.Report.SelectedHit);
    }

    [AvaloniaFact]
    public async Task The_per_attribute_eXaminer_buttons_are_gone()
    {
        using var pick = await new Pick(examiner: true).ReadyAsync();
        pick.Report.SetPick("CautionArea", "Caution Area", "7", "f.000", "S-101",
        [
            Leaf("colour", "3", "Colour", "Red"),
            new PickAttribute
            {
                Code = "information",
                Name = "Information",
                RawValue = string.Empty,
                Children = [Leaf("text", new string('x', 200), "Text")],
            },
        ]);
        using var host = ViewHost.Show(new PickReportView { DataContext = pick.Report }, width: 420, height: 900);

        Assert.True(pick.Report.IsExaminerAvailable);
        Assert.Empty(host.FindAll<Control>("PickReport.Attribute.OpenInExaminer"));
        Assert.Empty(host.FindAll<Control>("PickReport.TextCard.OpenAttributeInExaminer"));
        // The feature-level action stays, on the identity and on a text card.
        Assert.True(host.Find<Button>("PickReport.OpenFeatureInExaminer").IsEffectivelyVisible);
        Assert.True(host.Find<Button>("PickReport.TextCard.OpenInExaminer").IsEffectivelyVisible);
    }
}
