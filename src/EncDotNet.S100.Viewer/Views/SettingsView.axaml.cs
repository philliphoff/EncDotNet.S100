using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _settings;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_settings is not null)
                _settings.SettingRequested -= OnSettingRequested;
            _settings = DataContext as SettingsViewModel;
            if (_settings is not null)
            {
                _settings.SettingRequested += OnSettingRequested;
                // Asked for before this view was made: the Settings tab opens with it.
                if (_settings.RequestedSetting is { } pending)
                    OnSettingRequested(_settings, pending);
            }
        };
    }

    // A setting asked for by name (Why this symbol, #1680): once its page is
    // shown and laid out, scroll to its control and focus it.
    private void OnSettingRequested(object? sender, string automationId) =>
        Dispatcher.UIThread.Post(() => Reveal(automationId), DispatcherPriority.Loaded);

    internal bool Reveal(string automationId)
    {
        var control = this.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.IsEffectivelyVisible && AutomationProperties.GetAutomationId(c) == automationId);
        if (control is null)
            return false;
        control.BringIntoView();
        control.Focus(Avalonia.Input.NavigationMethod.Tab);
        _settings?.SettingShown();
        return true;
    }
}
