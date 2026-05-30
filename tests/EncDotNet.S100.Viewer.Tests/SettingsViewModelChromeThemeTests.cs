using System;
using System.IO;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SettingsViewModelChromeThemeTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void DefaultsTo_Light_When_Missing()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        Assert.Equal(ChromeTheme.Light, vm.SelectedChromeTheme);
    }

    [Theory]
    [InlineData("Dark", ChromeTheme.Dark)]
    [InlineData("dark", ChromeTheme.Dark)]
    [InlineData("S100Night", ChromeTheme.S100Night)]
    [InlineData("s100night", ChromeTheme.S100Night)]
    public void Parses_Persisted_Value_CaseInsensitive(string persisted, ChromeTheme expected)
    {
        var s = NewSettings();
        s.ChromeTheme = persisted;
        var vm = new SettingsViewModel(s);
        Assert.Equal(expected, vm.SelectedChromeTheme);
    }

    [Fact]
    public void InvalidValue_FallsBackTo_Light()
    {
        var s = NewSettings();
        s.ChromeTheme = "totally-bogus";
        var vm = new SettingsViewModel(s);
        Assert.Equal(ChromeTheme.Light, vm.SelectedChromeTheme);
    }

    [Fact]
    public void Setting_Theme_Persists_To_Settings()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        vm.SelectedChromeTheme = ChromeTheme.S100Night;
        Assert.Equal("S100Night", s.ChromeTheme);
    }

    [Fact]
    public void Setting_Theme_Raises_ChromeThemeChanged()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        ChromeTheme? raised = null;
        vm.ChromeThemeChanged += t => raised = t;

        vm.SelectedChromeTheme = ChromeTheme.Dark;

        Assert.Equal(ChromeTheme.Dark, raised);
    }

    [Fact]
    public void Setting_Same_Theme_Does_Not_Raise_Event()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        var count = 0;
        vm.ChromeThemeChanged += _ => count++;

        vm.SelectedChromeTheme = vm.SelectedChromeTheme;

        Assert.Equal(0, count);
    }

    [Fact]
    public void GetDefaultPaletteFor_Maps_Variants_Correctly()
    {
        Assert.Equal(EncDotNet.S100.Pipelines.PaletteType.Day, ChromeThemes.GetDefaultPaletteFor(ChromeTheme.Light));
        Assert.Equal(EncDotNet.S100.Pipelines.PaletteType.Day, ChromeThemes.GetDefaultPaletteFor(ChromeTheme.Dark));
        Assert.Equal(EncDotNet.S100.Pipelines.PaletteType.Night, ChromeThemes.GetDefaultPaletteFor(ChromeTheme.S100Night));
    }

    [Fact]
    public void IsDark_Treats_Dark_And_S100Night_As_Dark()
    {
        Assert.False(ChromeThemes.IsDark(ChromeTheme.Light));
        Assert.True(ChromeThemes.IsDark(ChromeTheme.Dark));
        Assert.True(ChromeThemes.IsDark(ChromeTheme.S100Night));
    }
}
