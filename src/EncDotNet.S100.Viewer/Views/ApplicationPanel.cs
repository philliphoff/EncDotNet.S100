using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A generic application panel: a titled chrome surface with a title bar
/// (an upper-cased <see cref="Title"/> plus an optional close button) above a
/// content area that fills the remaining space. Provides one consistent panel
/// treatment for the viewer's docks (left activity pane, right companion pane,
/// bottom strip) so each dock no longer hand-rolls its own header.
/// </summary>
/// <remarks>
/// The control derives from <see cref="ContentControl"/>, so its
/// <see cref="ContentControl.Content"/> is the panel body and fills the
/// content area by default. The title bar is supplied by the control template;
/// callers only set <see cref="Title"/>, and—when the panel is dismissable—
/// <see cref="ShowCloseButton"/>, <see cref="CloseCommand"/>,
/// <see cref="CloseCommandParameter"/> and <see cref="CloseButtonToolTip"/>.
/// </remarks>
public class ApplicationPanel : ContentControl
{
    /// <summary>
    /// The panel title. Rendered upper-cased in the title bar, so callers may
    /// pass a natural-cased string.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ApplicationPanel, string?>(nameof(Title));

    /// <summary>
    /// Whether the title bar shows a close button. Defaults to <c>false</c>
    /// (e.g. the left activity pane swaps content via the activity bar rather
    /// than being closed from its own header).
    /// </summary>
    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<ApplicationPanel, bool>(nameof(ShowCloseButton));

    /// <summary>
    /// Command invoked when the close button is pressed.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ApplicationPanel, ICommand?>(nameof(CloseCommand));

    /// <summary>
    /// Parameter passed to <see cref="CloseCommand"/> when the close button is
    /// pressed.
    /// </summary>
    public static readonly StyledProperty<object?> CloseCommandParameterProperty =
        AvaloniaProperty.Register<ApplicationPanel, object?>(nameof(CloseCommandParameter));

    /// <summary>
    /// Tooltip text for the close button. Should be a localized string.
    /// </summary>
    public static readonly StyledProperty<string?> CloseButtonToolTipProperty =
        AvaloniaProperty.Register<ApplicationPanel, string?>(nameof(CloseButtonToolTip));

    /// <summary>
    /// The panel title. Rendered upper-cased in the title bar.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Whether the title bar shows a close button.
    /// </summary>
    public bool ShowCloseButton
    {
        get => GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    /// <summary>
    /// Command invoked when the close button is pressed.
    /// </summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>
    /// Parameter passed to <see cref="CloseCommand"/>.
    /// </summary>
    public object? CloseCommandParameter
    {
        get => GetValue(CloseCommandParameterProperty);
        set => SetValue(CloseCommandParameterProperty, value);
    }

    /// <summary>
    /// Tooltip text for the close button.
    /// </summary>
    public string? CloseButtonToolTip
    {
        get => GetValue(CloseButtonToolTipProperty);
        set => SetValue(CloseButtonToolTipProperty, value);
    }
}
