using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Settings panel's category shell (#845): a category list beside the
/// selected category's page, with every setting reachable on exactly one page.
/// </summary>
public sealed class SettingsViewTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"settings-view-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    [AvaloniaFact]
    public void Clicking_a_category_shows_its_page()
    {
        var vm = TestSettings.Shell(new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }));
        using var host = ViewHost.Show(new SettingsView { DataContext = vm });
        Assert.True(host.IsShown("Settings.DistanceUnit"));
        Assert.False(host.IsShown("Settings.AisEnabled"));

        host.Click(host.Find<TabItem>("Settings.Category.Vessels"));

        Assert.Equal(SettingsCategory.Vessels, vm.SelectedCategory);
        Assert.True(host.IsShown("Settings.AisEnabled"));
        Assert.False(host.IsShown("Settings.DistanceUnit"));
    }

    [AvaloniaFact]
    public void Setting_the_category_selects_its_tab()
    {
        var vm = TestSettings.Shell(new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }));
        using var host = ViewHost.Show(new SettingsView { DataContext = vm });

        vm.SelectedCategory = SettingsCategory.Advanced;
        host.Settle();

        Assert.True(host.Find<TabItem>("Settings.Category.Advanced").IsSelected);
        Assert.True(host.IsShown("Settings.ClearCaches"));
    }

    [AvaloniaFact]
    public void Every_setting_is_on_exactly_one_category_page()
    {
        var vm = TestSettings.Shell(new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }));
        using var host = ViewHost.Show(new SettingsView { DataContext = vm });
        var pages = new Dictionary<string, List<SettingsCategory>>();

        foreach (var category in vm.Pages.Select(p => p.Category))
        {
            vm.SelectedCategory = category;
            host.Settle();
            foreach (var id in host.All<Control>()
                .Select(AutomationProperties.GetAutomationId)
                .OfType<string>()
                .Where(id => id.StartsWith("Settings.", StringComparison.Ordinal) && !id.StartsWith("Settings.Categor", StringComparison.Ordinal))
                .Distinct())
            {
                (pages.TryGetValue(id, out var list) ? list : pages[id] = []).Add(category);
            }
        }

        // Every Settings.* id declared by the pages' XAML (tiled-mode knobs
        // included, which the default scene mode shows).
        var declared = Directory.EnumerateFiles(
                Path.Combine(Path.GetDirectoryName(LibraryTestContext.RepoFile("src", "EncDotNet.S100.Viewer", "Views", "SettingsView.axaml"))!, "Settings"),
                "*.axaml")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "AutomationId=\"(Settings\\.[A-Za-z.]+)\"").Select(m => m.Groups[1].Value))
            .Where(id => !id.StartsWith("Settings.Categor", StringComparison.Ordinal))
            .ToHashSet();

        Assert.Empty(declared.Except(pages.Keys));
        Assert.All(pages, p => Assert.Single(p.Value));
    }

    [AvaloniaFact]
    public void The_category_list_shows_the_pages_in_their_order_with_their_automation_ids()
    {
        var vm = new SettingsViewModel(TestSettings.Pages(new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath })).Reverse());
        using var host = ViewHost.Show(new SettingsView { DataContext = vm });

        Assert.Equal(
            ["Settings.Category.General", "Settings.Category.Appearance", "Settings.Category.Chart", "Settings.Category.Vessels", "Settings.Category.Integrations", "Settings.Category.Advanced"],
            host.All<TabItem>().Select(AutomationProperties.GetAutomationId));
        Assert.Equal(vm.Pages.Select(p => p.Title), host.All<TabItem>().Select(AutomationProperties.GetName));
        Assert.Equal(SettingsCategory.General, vm.SelectedCategory);
    }
}
