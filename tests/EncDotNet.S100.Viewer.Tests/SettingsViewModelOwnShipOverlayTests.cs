using System;
using System.IO;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SettingsViewModelOwnShipOverlayTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void DefaultsTo_False()
    {
        var vm = new SettingsViewModel(NewSettings());
        Assert.False(vm.OwnShipOverlayEnabled);
    }

    [Fact]
    public void ReflectsPersistedValue()
    {
        var s = NewSettings();
        s.OwnShipOverlayEnabled = true;
        var vm = new SettingsViewModel(s);
        Assert.True(vm.OwnShipOverlayEnabled);
    }

    [Fact]
    public void Setting_Persists_To_Settings()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        vm.OwnShipOverlayEnabled = true;
        Assert.True(s.OwnShipOverlayEnabled);
    }

    [Fact]
    public void Setting_Raises_OwnShipOverlayEnabledChanged()
    {
        var vm = new SettingsViewModel(NewSettings());
        bool? raised = null;
        vm.OwnShipOverlayEnabledChanged += v => raised = v;

        vm.OwnShipOverlayEnabled = true;

        Assert.Equal(true, raised);
    }

    [Fact]
    public void Setting_Same_Value_Does_Not_Raise_Event()
    {
        var vm = new SettingsViewModel(NewSettings());
        var count = 0;
        vm.OwnShipOverlayEnabledChanged += _ => count++;

        vm.OwnShipOverlayEnabled = false;

        Assert.Equal(0, count);
    }
}
