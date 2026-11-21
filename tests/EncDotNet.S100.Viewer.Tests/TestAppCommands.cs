using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Builds an <see cref="AppCommandsViewModel"/> for tests that never open its dialogs.</summary>
internal static class TestAppCommands
{
    public static AppCommandsViewModel Create() =>
        new(
            new LightTheme(),
            new ShadUI.DialogManager(),
            feedbackDialogFactory: () => throw new InvalidOperationException("This test doesn't open the feedback dialog."),
            aboutDialogFactory: () => throw new InvalidOperationException("This test doesn't open the About dialog."));

    private sealed class LightTheme : IThemeService
    {
        public bool IsDarkTheme => false;
        public event EventHandler? ThemeChanged { add { } remove { } }
        public bool ToggleTheme() => false;
        public ChromeTheme Current => ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { }
    }
}
