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
///
/// Subscribes to <see cref="Application.ActualThemeVariantChanged"/> and
/// re-raises it as <see cref="IThemeService.ThemeChanged"/> so consumers
/// can observe theme changes regardless of whether the trigger was the
/// in-app toggle button, the settings selector, or an external source
/// (system theme follow).
/// </summary>
internal sealed class ThemeService : IThemeService
{
    public ThemeService()
    {
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnApplicationThemeVariantChanged;
    }

    public event EventHandler? ThemeChanged;

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
        // ActualThemeVariantChanged fires from the assignment above and
        // drives ThemeChanged via OnApplicationThemeVariantChanged — no
        // explicit raise needed here.
    }

    public bool ToggleTheme()
    {
        var next = IsDarkTheme ? ChromeTheme.Light : ChromeTheme.Dark;
        SetTheme(next);
        return IsDarkTheme;
    }

    private void OnApplicationThemeVariantChanged(object? sender, EventArgs e)
        => ThemeChanged?.Invoke(this, EventArgs.Empty);
}
