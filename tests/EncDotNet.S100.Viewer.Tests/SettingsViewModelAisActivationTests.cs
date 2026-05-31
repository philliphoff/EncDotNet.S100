using System;
using System.IO;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SettingsViewModelAisActivationTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void Defaults_to_AisOverlaySettings_default_when_unset()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        // When AisOverlay block is absent, VM falls back to the
        // AisOverlaySettings default (50°), matching the gate that
        // would apply at runtime if the user enabled AIS without
        // touching the threshold.
        Assert.Equal(50.0, vm.AisActivationViewportSpanDegrees);
    }

    [Fact]
    public void Hydrates_from_persisted_value()
    {
        var s = NewSettings();
        s.AisOverlay = new AisOverlaySettings { ActivationViewportSpanDegrees = 12.5 };
        var vm = new SettingsViewModel(s);
        Assert.Equal(12.5, vm.AisActivationViewportSpanDegrees);
    }

    [Fact]
    public void Setting_persists_to_settings_block()
    {
        var s = NewSettings();
        try
        {
            var vm = new SettingsViewModel(s);
            vm.AisActivationViewportSpanDegrees = 30.0;
            Assert.Equal(30.0, s.AisOverlay?.ActivationViewportSpanDegrees);
        }
        finally
        {
            if (File.Exists(s.SettingsFilePath)) File.Delete(s.SettingsFilePath);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Non_positive_normalises_to_null(double v)
    {
        var s = NewSettings();
        try
        {
            var vm = new SettingsViewModel(s);
            vm.AisActivationViewportSpanDegrees = v;
            Assert.Null(vm.AisActivationViewportSpanDegrees);
            Assert.Null(s.AisOverlay?.ActivationViewportSpanDegrees);
        }
        finally
        {
            if (File.Exists(s.SettingsFilePath)) File.Delete(s.SettingsFilePath);
        }
    }

    [Fact]
    public void Null_persists_as_null_meaning_no_gate()
    {
        var s = NewSettings();
        s.AisOverlay = new AisOverlaySettings { ActivationViewportSpanDegrees = 25.0 };
        try
        {
            var vm = new SettingsViewModel(s);
            vm.AisActivationViewportSpanDegrees = null;
            Assert.Null(vm.AisActivationViewportSpanDegrees);
            Assert.Null(s.AisOverlay?.ActivationViewportSpanDegrees);
        }
        finally
        {
            if (File.Exists(s.SettingsFilePath)) File.Delete(s.SettingsFilePath);
        }
    }
}
