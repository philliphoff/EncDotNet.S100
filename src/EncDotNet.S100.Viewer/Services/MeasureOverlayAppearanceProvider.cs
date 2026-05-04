using System;
using Avalonia;
using Avalonia.Media;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Bridges <see cref="SettingsViewModel.AccentColor"/> and
/// <see cref="IThemeService.IsDarkTheme"/> into a single
/// <see cref="IMeasureOverlayAppearanceProvider"/> so map tools can
/// observe one source of truth instead of subscribing to several
/// disparate notifications. Listens for both accent-colour changes and
/// the global Avalonia <c>ActualThemeVariantChanged</c> event so the
/// overlay updates regardless of whether the theme is toggled via the
/// view-model command or some other code path.
/// </summary>
internal sealed class MeasureOverlayAppearanceProvider : IMeasureOverlayAppearanceProvider, IDisposable
{
    private readonly IThemeService _theme;
    private readonly SettingsViewModel _settings;
    private readonly Application? _application;
    private bool _disposed;

    public event EventHandler? Changed;

    public MeasureOverlayAppearance Current
    {
        get
        {
            var c = _settings.AccentColor;
            return new MeasureOverlayAppearance((c.R, c.G, c.B), _theme.IsDarkTheme);
        }
    }

    public MeasureOverlayAppearanceProvider(IThemeService theme, SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(settings);
        _theme = theme;
        _settings = settings;

        _settings.AccentColorChanged += OnAccentChanged;

        _application = Application.Current;
        if (_application is not null)
            _application.ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    private void OnAccentChanged(Color _) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnThemeVariantChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.AccentColorChanged -= OnAccentChanged;
        if (_application is not null)
            _application.ActualThemeVariantChanged -= OnThemeVariantChanged;
    }
}
