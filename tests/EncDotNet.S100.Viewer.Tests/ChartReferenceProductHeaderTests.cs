using Avalonia.Controls;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Chart reference product header (design note
/// <c>catalogue-panels.md</c> D8, D9, §6.3, CR4) over a real
/// <see cref="CatalogueRegistry"/>: the identity line, Catalogues… and the
/// eXaminer link, following the product and catalogue changes.
/// </summary>
public sealed class ChartReferenceProductHeaderTests : IDisposable
{
    private const string BuiltIn = CatalogueFixtures.BuiltInVersion;

    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();
    private readonly CatalogueRegistry _registry;
    private readonly ChartReferenceService _service;
    private readonly RecordingUrlOpener _opener = new();
    private readonly List<string> _shown = [];

    public ChartReferenceProductHeaderTests()
    {
        // Wired as App.axaml.cs wires them: the service reads the registry's managers.
        _settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(_files.Root, "settings.json"),
            S100ExaminerLinksEnabled = false,
        };
        _registry = new CatalogueRegistry(_settings, _features, _portrayals);
        _registry.Initialize(null, null);
        _service = new ChartReferenceService(_features, _portrayals, () => [], bundledSpecs: ["S-101", "S-102"]);
    }

    public void Dispose()
    {
        _service.Dispose();
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    private ChartReferenceViewModel Reference()
    {
        var reference = new ChartReferenceViewModel(_service, new ChartReferenceGlyphCache(PaletteType.Day));
        reference.ProductHeader = new ChartReferenceProductHeaderViewModel(
            reference,
            _registry,
            TestPickReport.Examiner(new S100ExaminerLinkBuilder(_settings), _opener),
            _shown.Add);
        SelectProduct(reference, "S-102");
        return reference;
    }

    private static DetectedCatalogue Detect(string path) => CatalogueSpecDetection.Detect(path).Catalogue!;

    private static void SelectProduct(ChartReferenceViewModel reference, string spec) =>
        reference.SelectedProduct = reference.Products.Single(p => p.Spec.Name == spec);

    [Fact]
    public void Built_in_catalogues_read_as_one_line_and_catalogues_opens_the_activity_at_the_product()
    {
        using var reference = Reference();
        var header = reference.ProductHeader!;

        Assert.Equal("S-102", header.Spec);
        Assert.True(header.IsVisible);
        Assert.Equal($"S-102 · Feature {BuiltIn} · Portrayal {BuiltIn} · {Strings.Catalogues_Origin_BuiltIn}", header.IdentityText);
        Assert.True(header.CanShowCatalogues);

        header.ShowCataloguesCommand.Execute(null);

        Assert.Equal(["S-102"], _shown);
    }

    [Fact]
    public void The_line_follows_the_selected_product()
    {
        using var reference = Reference();
        var header = reference.ProductHeader!;
        var changes = new List<string?>();
        header.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        SelectProduct(reference, "S-101");

        Assert.NotEmpty(changes);
        Assert.Equal("S-101", header.Spec);
        Assert.StartsWith("S-101 · Feature ", header.IdentityText, StringComparison.Ordinal);
        Assert.EndsWith($" · {Strings.Catalogues_Origin_BuiltIn}", header.IdentityText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_catalogue_shows_its_source_live_and_reverting_puts_built_in_back()
    {
        using var reference = Reference();
        var header = reference.ProductHeader!;

        _registry.Use(Detect(_files.PortrayalFolder("9.1.0")));

        Assert.Equal($"S-102 · Feature {BuiltIn} · Portrayal 9.1.0 ({Strings.Catalogues_Describe_Custom})", header.IdentityText);

        Assert.True(_registry.Revert("S-102", CatalogueKind.Portrayal));

        Assert.Equal($"S-102 · Feature {BuiltIn} · Portrayal {BuiltIn} · {Strings.Catalogues_Origin_BuiltIn}", header.IdentityText);
    }

    [Fact]
    public void A_product_the_registry_doesnt_know_hides_the_header()
    {
        using var service = new ChartReferenceService(_features, _portrayals, () => [], bundledSpecs: ["S-999"]);
        using var reference = new ChartReferenceViewModel(service, new ChartReferenceGlyphCache(PaletteType.Day));
        using var header = new ChartReferenceProductHeaderViewModel(reference, _registry, TestPickReport.Examiner(), _shown.Add);

        Assert.False(header.IsVisible);
        Assert.False(header.CanShowCatalogues);
        Assert.False(header.CanOpenInExaminer);
        Assert.Equal(string.Empty, header.IdentityText);
    }

    [Fact]
    public void The_examiner_link_follows_the_settings_and_names_the_published_edition_for_a_custom_feature_catalogue()
    {
        using var reference = Reference();
        var header = reference.ProductHeader!;

        // Off: no link (D9 — only where it applies).
        Assert.False(header.CanOpenInExaminer);

        _settings.S100ExaminerLinksEnabled = true;
        header.Refresh();

        Assert.True(header.CanOpenInExaminer);
        Assert.Equal(Strings.ChartReference_Header_Examiner, header.ExaminerLabel);
        Assert.Equal(Strings.Tooltip_ChartReferenceHeaderExaminer, header.ExaminerTooltip);
        header.OpenInExaminerCommand.Execute(null);
        Assert.Equal("https://s100examiner.com/?catalog=S-102", _opener.LastUrl);

        // A custom feature catalogue: the eXaminer only has the published edition (A9).
        _registry.Use(Detect(_files.FeatureFile("9.0.0")));

        Assert.True(header.IsFeatureCatalogueCustom);
        Assert.Equal(Strings.ChartReference_Header_ExaminerPublished, header.ExaminerLabel);
        Assert.Equal(Strings.Tooltip_ChartReferenceHeaderExaminerPublished, header.ExaminerTooltip);
        Assert.StartsWith($"S-102 · Feature 9.0.0 ({Strings.Catalogues_Describe_Custom})", header.IdentityText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_header_stops_following_the_registry()
    {
        using var reference = Reference();
        var header = reference.ProductHeader!;
        var before = header.IdentityText;

        header.Dispose();
        _registry.Use(Detect(_files.PortrayalFolder("9.1.0")));

        Assert.Equal(before, header.IdentityText);
    }

    [AvaloniaFact]
    public void The_activity_shows_the_header_and_its_links_update_live()
    {
        _settings.S100ExaminerLinksEnabled = true;
        using var reference = Reference();
        using var host = ViewHost.Show(new ChartReferenceView { DataContext = reference }, 320, 800);

        var identity = host.Find<TextBlock>("ChartReferenceHeader.Identity");
        Assert.Equal(reference.ProductHeader!.IdentityText, identity.Text);
        Assert.True(host.IsShown("ChartReferenceHeader.Catalogues"));
        Assert.True(host.IsShown("ChartReferenceHeader.Examiner"));
        Assert.Equal(
            Strings.ChartReference_Header_Examiner,
            Avalonia.Automation.AutomationProperties.GetName(host.Find<Button>("ChartReferenceHeader.Examiner")));

        host.Click(host.Find<Button>("ChartReferenceHeader.Catalogues"));
        Assert.Equal(["S-102"], _shown);

        _registry.Use(Detect(_files.FeatureFile("9.0.0")));
        host.Settle();

        Assert.StartsWith("S-102 · Feature 9.0.0 (", identity.Text, StringComparison.Ordinal);
        Assert.Equal(
            Strings.ChartReference_Header_ExaminerPublished,
            Avalonia.Automation.AutomationProperties.GetName(host.Find<Button>("ChartReferenceHeader.Examiner")));

        _settings.S100ExaminerLinksEnabled = false;
        reference.ProductHeader.Refresh();
        host.Settle();

        Assert.False(host.IsShown("ChartReferenceHeader.Examiner"));
    }

    [AvaloniaFact]
    public void The_page_shares_the_activitys_header()
    {
        using var reference = Reference();
        using var page = new ChartReferencePageViewModel(reference, _service);
        using var host = ViewHost.Show(new ChartReferencePageView { DataContext = page }, 1280, 720);

        Assert.Equal(reference.ProductHeader!.IdentityText, host.Find<TextBlock>("ChartReferenceHeader.Identity").Text);
        Assert.True(host.IsShown("ChartReferenceHeader.Catalogues"));
        Assert.False(host.IsShown("ChartReferenceHeader.Examiner"));

        _registry.Use(Detect(_files.PortrayalFolder("9.1.0")));
        host.Settle();

        Assert.EndsWith($"Portrayal 9.1.0 ({Strings.Catalogues_Describe_Custom})", host.Find<TextBlock>("ChartReferenceHeader.Identity").Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Without_a_header_the_activity_hides_it()
    {
        using var reference = new ChartReferenceViewModel(_service, new ChartReferenceGlyphCache(PaletteType.Day));
        using var host = ViewHost.Show(new ChartReferenceView { DataContext = reference }, 320, 800);

        Assert.False(host.IsShown("ChartReferenceHeader.Identity"));
    }

    private sealed class RecordingUrlOpener : IUrlOpener
    {
        public string? LastUrl { get; private set; }

        public void Open(string url) => LastUrl = url;
    }
}
