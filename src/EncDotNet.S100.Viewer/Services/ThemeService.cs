using Avalonia;
using Avalonia.Styling;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IThemeService"/> backed by
/// <see cref="Application.Current"/>'s <see cref="ThemeVariant"/>.
/// </summary>
internal sealed class ThemeService : IThemeService
{
    public bool IsDarkTheme =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public bool ToggleTheme()
    {
        if (Application.Current is not { } app)
            return IsDarkTheme;

        var next = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        app.RequestedThemeVariant = next;
        return next == ThemeVariant.Dark;
    }
}
