using System;
using Avalonia;
using Avalonia.Styling;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IThemeService"/> backed by
/// <see cref="Application.Current"/>'s <see cref="ThemeVariant"/>.
/// Subscribes to <see cref="Application.ActualThemeVariantChanged"/> and
/// re-raises it as <see cref="IThemeService.ThemeChanged"/> so consumers
/// can observe theme changes regardless of whether the trigger was the
/// in-app toggle button or an external source (system theme follow).
/// </summary>
internal sealed class ThemeService : IThemeService
{
    public ThemeService()
    {
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnApplicationThemeVariantChanged;
    }

    public bool IsDarkTheme =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public event EventHandler? ThemeChanged;

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

    private void OnApplicationThemeVariantChanged(object? sender, EventArgs e)
        => ThemeChanged?.Invoke(this, EventArgs.Empty);
}
