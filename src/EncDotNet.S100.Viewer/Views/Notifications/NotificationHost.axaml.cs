using System.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views.Notifications;

/// <summary>
/// Overlay control that renders the active notifications produced by
/// <c>INotificationService</c>. Placed in the window's root panel and anchored
/// to a corner; cards stack with the newest closest to the corner. The items
/// source is assigned from code so the control has no DI dependency.
/// </summary>
internal sealed partial class NotificationHost : UserControl
{
    private readonly ItemsControl _itemsHost;

    /// <summary>Initializes the control.</summary>
    public NotificationHost()
    {
        InitializeComponent();

        // The repo's manual AvaloniaXamlLoader.Load pattern does not generate
        // strongly-typed named fields, so resolve the items host by name.
        _itemsHost = this.FindControl<ItemsControl>("ItemsHost")
            ?? throw new InvalidOperationException(
                "NotificationHost.axaml is missing the 'ItemsHost' ItemsControl.");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The notifications to display, typically
    /// <c>INotificationService.Active</c>.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => _itemsHost.ItemsSource;
        set => _itemsHost.ItemsSource = value;
    }
}
