using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for the S-100 Feature Catalogue eXaminer wiring in the Settings
/// and Feature Catalogue view-models (issue #442).
/// </summary>
public class ExaminerViewModelTests
{
    private sealed class StubUrlOpener : IUrlOpener
    {
        public string? LastUrl { get; private set; }
        public void Open(string url) => LastUrl = url;
    }

    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void Settings_default_examiner_links_enabled()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        Assert.True(vm.ExaminerLinksEnabled);
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, vm.ExaminerBaseUrl);
    }

    [Fact]
    public void Settings_toggle_persists()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s) { ExaminerLinksEnabled = false };
        Assert.False(s.S100ExaminerLinksEnabled);
    }

    [Fact]
    public void Settings_base_url_persists_and_resets()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s) { ExaminerBaseUrl = "https://mirror.example.org/" };
        Assert.Equal("https://mirror.example.org/", s.S100ExaminerBaseUrl);

        vm.ResetExaminerBaseUrlCommand.Execute(null);
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, vm.ExaminerBaseUrl);
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, s.S100ExaminerBaseUrl);
    }

    [Fact]
    public void Settings_blank_base_url_falls_back_to_default()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s) { ExaminerBaseUrl = "   " };
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, vm.ExaminerBaseUrl);
    }

    [Fact]
    public void Fc_builtin_entry_supported_spec_can_open_and_links()
    {
        var settings = NewSettings();
        var opener = new StubUrlOpener();
        var links = new S100ExaminerLinkBuilder(settings);
        var vm = new FeatureCataloguesViewModel(settings, links, opener);

        vm.AddBuiltIn("S-101", "(built-in)");
        var entry = vm.Entries.Single(e => e.ProductSpec == "S-101");
        Assert.True(entry.CanOpenInExaminer);

        vm.OpenInExaminerCommand.Execute(entry);
        Assert.Equal("https://s100examiner.com/?catalog=S-101", opener.LastUrl);
    }

    [Fact]
    public void Fc_refresh_updates_can_open_when_links_disabled()
    {
        var settings = NewSettings();
        var links = new S100ExaminerLinkBuilder(settings);
        var vm = new FeatureCataloguesViewModel(settings, links, new StubUrlOpener());

        vm.AddBuiltIn("S-101", "(built-in)");
        var entry = vm.Entries.Single(e => e.ProductSpec == "S-101");
        Assert.True(entry.CanOpenInExaminer);

        // Simulate the user disabling the integration in Settings.
        settings.S100ExaminerLinksEnabled = false;
        vm.RefreshExaminerAvailability();
        Assert.False(entry.CanOpenInExaminer);

        // ...and re-enabling it.
        settings.S100ExaminerLinksEnabled = true;
        vm.RefreshExaminerAvailability();
        Assert.True(entry.CanOpenInExaminer);
    }

    [Fact]
    public void Settings_change_raises_examiner_settings_changed()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        var count = 0;
        vm.ExaminerSettingsChanged += () => count++;

        vm.ExaminerLinksEnabled = false;
        vm.ExaminerBaseUrl = "https://mirror.example.org/";

        Assert.Equal(2, count);
    }

    [Fact]
    public void Fc_refresh_raises_property_changed_on_entry()
    {
        var settings = NewSettings();
        var links = new S100ExaminerLinkBuilder(settings);
        var vm = new FeatureCataloguesViewModel(settings, links, new StubUrlOpener());

        vm.AddBuiltIn("S-101", "(built-in)");
        var entry = vm.Entries.Single(e => e.ProductSpec == "S-101");
        var raised = false;
        entry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogueEntry.CanOpenInExaminer))
                raised = true;
        };

        settings.S100ExaminerLinksEnabled = false;
        vm.RefreshExaminerAvailability();
        Assert.True(raised);
    }
}
