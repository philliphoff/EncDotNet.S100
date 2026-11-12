using System.ComponentModel;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A Settings page over <see cref="ViewerSettingsService"/> (#1398). It
/// re-raises the service's property changes, so a page bound to a setting
/// shows a change made elsewhere, and lets the page raise the properties it
/// derives from one.
/// </summary>
internal abstract class SettingsPageViewModel : ViewModelBase, ISettingsPage
{
    /// <summary>Creates the page.</summary>
    /// <param name="settings">The settings it shows and edits.</param>
    protected SettingsPageViewModel(ViewerSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>The settings the page shows and edits.</summary>
    protected ViewerSettingsService Settings { get; }

    /// <inheritdoc />
    public abstract SettingsCategory Category { get; }

    /// <inheritdoc />
    public abstract string Title { get; }

    /// <inheritdoc />
    public abstract int Order { get; }

    /// <inheritdoc />
    public abstract string AutomationId { get; }

    /// <inheritdoc />
    public string? StatusText => null;

    /// <inheritdoc />
    public virtual void OnShown()
    {
    }

    /// <summary>A setting changed: raise the page properties derived from it.</summary>
    /// <param name="propertyName">The service property that changed.</param>
    protected virtual void OnSettingChanged(string? propertyName)
    {
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e);
        OnSettingChanged(e.PropertyName);
    }
}
