using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class AppCommandsViewModelTests
{
    private sealed class SwitchableTheme : IThemeService
    {
        public bool IsDarkTheme { get; private set; }
        public event EventHandler? ThemeChanged;
        public bool ToggleTheme() { IsDarkTheme = !IsDarkTheme; ThemeChanged?.Invoke(this, EventArgs.Empty); return IsDarkTheme; }
        public ChromeTheme Current => IsDarkTheme ? ChromeTheme.Dark : ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { IsDarkTheme = ChromeThemes.IsDark(theme); ThemeChanged?.Invoke(this, EventArgs.Empty); }
    }

    private static AppCommandsViewModel Create(IThemeService theme) =>
        new(
            theme,
            new ShadUI.DialogManager(),
            feedbackDialogFactory: () => throw new InvalidOperationException("Not opened here."),
            aboutDialogFactory: () => throw new InvalidOperationException("Not opened here."));

    [Fact]
    public void ToggleThemeCommand_FlipsTheTheme()
    {
        var theme = new SwitchableTheme();
        var commands = Create(theme);
        Assert.False(commands.IsDarkTheme);

        commands.ToggleThemeCommand.Execute(null);

        Assert.True(theme.IsDarkTheme);
        Assert.True(commands.IsDarkTheme);
    }

    [Fact]
    public void IsDarkTheme_FollowsThemeChangesMadeElsewhere()
    {
        var theme = new SwitchableTheme();
        var commands = Create(theme);

        theme.SetTheme(ChromeTheme.Dark);

        Assert.True(commands.IsDarkTheme);
    }
}
