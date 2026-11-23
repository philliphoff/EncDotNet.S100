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
        var vm = new ViewerSettingsService(s);
        Assert.True(vm.ExaminerLinksEnabled);
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, vm.ExaminerBaseUrl);
    }

    [Fact]
    public void Settings_toggle_persists()
    {
        var s = NewSettings();
        var vm = new ViewerSettingsService(s) { ExaminerLinksEnabled = false };
        Assert.False(s.S100ExaminerLinksEnabled);
    }

    [Fact]
    public void Settings_base_url_persists_and_resets()
    {
        var s = NewSettings();
        var vm = new ViewerSettingsService(s) { ExaminerBaseUrl = "https://mirror.example.org/" };
        Assert.Equal("https://mirror.example.org/", s.S100ExaminerBaseUrl);

        new IntegrationsSettingsPageViewModel(vm).ResetExaminerBaseUrlCommand.Execute(null);
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, vm.ExaminerBaseUrl);
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, s.S100ExaminerBaseUrl);
    }

    [Fact]
    public void Settings_blank_base_url_falls_back_to_default()
    {
        var s = NewSettings();
        var vm = new ViewerSettingsService(s) { ExaminerBaseUrl = "   " };
        Assert.Equal(ViewerSettings.DefaultS100ExaminerBaseUrl, vm.ExaminerBaseUrl);
    }

    [Fact]
    public void Settings_change_raises_examiner_settings_changed()
    {
        var s = NewSettings();
        var vm = new ViewerSettingsService(s);
        var count = 0;
        vm.ExaminerSettingsChanged += () => count++;

        vm.ExaminerLinksEnabled = false;
        vm.ExaminerBaseUrl = "https://mirror.example.org/";

        Assert.Equal(2, count);
    }
}
