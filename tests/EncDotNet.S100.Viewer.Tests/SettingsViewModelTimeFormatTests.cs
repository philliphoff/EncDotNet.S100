using System;
using System.IO;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SettingsViewModelTimeFormatTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void DefaultsTo_Local_When_Missing()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        Assert.Equal(TimeFormat.Local, vm.SelectedTimeFormat);
    }

    [Fact]
    public void Parses_Persisted_Utc_CaseInsensitive()
    {
        var s = NewSettings();
        s.TimeFormat = "utc";
        var vm = new SettingsViewModel(s);
        Assert.Equal(TimeFormat.Utc, vm.SelectedTimeFormat);
    }

    [Fact]
    public void InvalidValue_FallsBackTo_Local()
    {
        var s = NewSettings();
        s.TimeFormat = "totally-bogus";
        var vm = new SettingsViewModel(s);
        Assert.Equal(TimeFormat.Local, vm.SelectedTimeFormat);
    }

    [Fact]
    public void Setting_Raises_Event_And_Persists()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        TimeFormat? raised = null;
        vm.TimeFormatChanged += f => raised = f;

        vm.SelectedTimeFormat = TimeFormat.Utc;

        Assert.Equal(TimeFormat.Utc, raised);
        Assert.Equal("Utc", s.TimeFormat);
        Assert.True(File.Exists(s.SettingsFilePath));
        File.Delete(s.SettingsFilePath);
    }
}

public class TimeFormatProviderTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void Current_Reflects_SettingsViewModel()
    {
        var settings = NewSettings();
        var vm = new SettingsViewModel(settings);
        var provider = new TimeFormatProvider(vm);
        Assert.Equal(TimeFormat.Local, provider.Current);

        vm.SelectedTimeFormat = TimeFormat.Utc;
        Assert.Equal(TimeFormat.Utc, provider.Current);

        if (File.Exists(settings.SettingsFilePath)) File.Delete(settings.SettingsFilePath);
    }

    [Fact]
    public void Provider_ReBroadcasts_Change()
    {
        var settings = NewSettings();
        var vm = new SettingsViewModel(settings);
        var provider = new TimeFormatProvider(vm);
        TimeFormat? heard = null;
        provider.TimeFormatChanged += f => heard = f;

        vm.SelectedTimeFormat = TimeFormat.Utc;
        Assert.Equal(TimeFormat.Utc, heard);

        if (File.Exists(settings.SettingsFilePath)) File.Delete(settings.SettingsFilePath);
    }
}
