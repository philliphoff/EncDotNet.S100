using EncDotNet.S100.Viewer.ViewModels;

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
        var vm = new SettingsViewModel(NewSettings(), dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        Assert.False(vm.OwnShipOverlayEnabled);
    }

    [Fact]
    public void ReflectsPersistedValue()
    {
        var s = NewSettings();
        s.OwnShipOverlayEnabled = true;
        var vm = new SettingsViewModel(s, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        Assert.True(vm.OwnShipOverlayEnabled);
    }

    [Fact]
    public void Setting_Persists_To_Settings()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        vm.OwnShipOverlayEnabled = true;
        Assert.True(s.OwnShipOverlayEnabled);
    }

    [Fact]
    public void Setting_Raises_OwnShipOverlayEnabledChanged()
    {
        var vm = new SettingsViewModel(NewSettings(), dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        bool? raised = null;
        vm.OwnShipOverlayEnabledChanged += v => raised = v;

        vm.OwnShipOverlayEnabled = true;

        Assert.Equal(true, raised);
    }

    [Fact]
    public void Setting_Same_Value_Does_Not_Raise_Event()
    {
        var vm = new SettingsViewModel(NewSettings(), dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var count = 0;
        vm.OwnShipOverlayEnabledChanged += _ => count++;

        vm.OwnShipOverlayEnabled = false;

        Assert.Equal(0, count);
    }
}
