using Avalonia.Controls;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Catalogues activity (design note <c>catalogue-panels.md</c> §6.1),
/// bound and laid out headlessly, driven through its <c>Catalogues.*</c>
/// automation ids.
/// </summary>
public sealed class CataloguesViewTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();

    public CataloguesViewTests()
    {
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
    }

    public void Dispose()
    {
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    private CatalogueRegistry Registry()
    {
        var registry = new CatalogueRegistry(_settings, _features, _portrayals);
        registry.Initialize(null, null);
        return registry;
    }

    private static ViewHost Show(CataloguesViewModel vm) =>
        ViewHost.Show(new CataloguesView { DataContext = vm }, width: 420, height: 900);

    [AvaloniaFact]
    public void The_changed_pill_lists_a_custom_product_and_revert_in_its_details_puts_the_built_in_back()
    {
        var registry = Registry();
        registry.Use(CatalogueSpecDetection.Detect(_files.PortrayalFolder("9.1.0")).Catalogue!);
        var vm = TestCatalogues.ViewModel(registry, _settings);
        using var host = Show(vm);

        Assert.False(host.IsShown("Catalogues.Pill.Problems"));
        host.Click(host.Find<RadioButton>("Catalogues.Pill.Changed"));
        host.Settle();

        var list = host.Find<ListBox>("Catalogues.List");
        var row = Assert.Single(host.FindAll<ListBoxItem>("Catalogues.Row"));
        Assert.Same(vm.SelectedRow, list.SelectedItem);
        Assert.Equal("S-102", vm.SelectedRow?.Spec);
        Assert.Contains("Custom", Avalonia.Automation.AutomationProperties.GetName(row), StringComparison.Ordinal);
        Assert.True(host.IsShown("Catalogues.Details.Reload"));

        host.Click(host.Find<Button>("Catalogues.Details.Revert"));
        host.Settle();

        Assert.Equal(CatalogueFixtures.BuiltInVersion, _portrayals.GetProvider("S-102").Catalogue.Version);
        Assert.False(host.IsShown("Catalogues.Details.Revert"));
    }

    [AvaloniaFact]
    public void Typing_in_the_filter_narrows_the_list_and_updates_the_count()
    {
        var vm = TestCatalogues.ViewModel(Registry(), _settings);
        using var host = Show(vm);

        host.Click(host.Find<TextBox>("Catalogues.Filter"));
        host.Type("S-104");
        host.Settle();

        Assert.Single(host.FindAll<ListBoxItem>("Catalogues.Row"));
        Assert.Equal($"1 of {vm.AllCount}", host.Find<TextBlock>("Catalogues.Count").Text);
        Assert.Equal("S-104", vm.SelectedRow?.Spec);
    }

    [AvaloniaFact]
    public void A_product_missing_its_portrayal_offers_add_portrayal_catalogue_and_remove_product()
    {
        var registry = Registry();
        registry.Use(CatalogueSpecDetection.Detect(_files.FeatureFile("0.1.0", product: "S-199")).Catalogue!);
        var vm = TestCatalogues.ViewModel(registry, _settings);
        using var host = Show(vm);

        vm.ShowProduct("S-199");
        host.Settle();

        Assert.True(host.IsShown("Catalogues.Pill.Problems"));
        Assert.True(host.IsShown("Catalogues.Details.AddPortrayal"));
        Assert.True(host.IsShown("Catalogues.Details.RemoveProduct"));
        Assert.False(host.IsShown("Catalogues.Details.Revert"));
    }
}
