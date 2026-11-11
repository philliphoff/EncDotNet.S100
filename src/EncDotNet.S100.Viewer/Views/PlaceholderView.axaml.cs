using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IconSymbol = FluentIcons.Common.Icon;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Reusable empty-state placeholder presenting a centered icon, a bold
/// primary <see cref="Header"/> message, and an optional secondary
/// <see cref="Description"/> line. Provides one consistent "nothing here
/// yet" treatment for every panel in the viewer that can be empty (datasets,
/// catalogues, display controls, search, timeline, vessels, layer stack,
/// pick report). Each panel passes the same icon it shows in the activity
/// bar so the empty state reads as that panel.
/// </summary>
public partial class PlaceholderView : UserControl
{
    /// <summary>
    /// The Fluent icon shown above the message. Uses the same
    /// <see cref="FluentIcons.Common.Icon"/> enum as
    /// <see cref="FluentIcons.Avalonia.FluentIcon"/> so callers can reuse
    /// their activity-bar icon (e.g. <c>Icon="Clock"</c>).
    /// </summary>
    public static readonly StyledProperty<IconSymbol> IconProperty =
        AvaloniaProperty.Register<PlaceholderView, IconSymbol>(nameof(Icon));

    /// <summary>
    /// The bold primary message describing the empty state.
    /// </summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<PlaceholderView, string?>(nameof(Header));

    /// <summary>
    /// Optional secondary descriptive text. The line is hidden when this is
    /// null or empty.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<PlaceholderView, string?>(nameof(Description));

    /// <summary>
    /// The Fluent icon shown above the message.
    /// </summary>
    public IconSymbol Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// The bold primary message describing the empty state.
    /// </summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Optional secondary descriptive text. The line is hidden when this is
    /// null or empty.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public PlaceholderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
