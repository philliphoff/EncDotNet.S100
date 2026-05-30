using System;
using Avalonia;
using Avalonia.Styling;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IThemeService"/> backed by
/// <see cref="Application.Current"/>'s <see cref="ThemeVariant"/>.
/// Recognises the bundled stock variants (<see cref="ThemeVariant.Light"/>,
/// <see cref="ThemeVariant.Dark"/>) plus the custom
/// <see cref="ChromeThemes.S100Night"/> variant declared in App.axaml.
/// </summary>
internal sealed class ThemeService : IThemeService
{
    public event EventHandler<ChromeTheme>? ThemeChanged;

    public bool IsDarkTheme => ChromeThemes.IsDark(Current);

    public ChromeTheme Current =>
        ChromeThemes.FromVariant(Application.Current?.ActualThemeVariant)
            ?? ChromeTheme.Light;

    public void SetTheme(ChromeTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        var variant = ChromeThemes.ToVariant(theme);
        if (app.RequestedThemeVariant != variant)
            app.RequestedThemeVariant = variant;

        ThemeChanged?.Invoke(this, theme);
    }

    public bool ToggleTheme()
    {
        var next = IsDarkTheme ? ChromeTheme.Light : ChromeTheme.Dark;
        SetTheme(next);
        return IsDarkTheme;
    }
}
