using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Reusable view for the S-411 sea-ice display-mode selector. Bind its
/// <see cref="Control.DataContext"/> to a
/// <see cref="ViewModels.DisplayModeToolbarViewModel"/>; the control renders
/// nothing unless the active dataset declares more than one display mode.
/// Used by both the map Display Settings flyout and the ECDIS Display
/// Controls panel so the two stay in sync via the shared view model.
/// </summary>
public partial class DisplayModeSelectorView : UserControl
{
    /// <summary>
    /// Identifies the <see cref="ShowDivider"/> styled property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowDividerProperty =
        AvaloniaProperty.Register<DisplayModeSelectorView, bool>(
            nameof(ShowDivider),
            defaultValue: true);

    /// <summary>Initializes a new instance of the <see cref="DisplayModeSelectorView"/> class.</summary>
    public DisplayModeSelectorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the leading horizontal divider
    /// rule is shown above the section header. Defaults to <c>true</c> for the
    /// map Display Settings flyout, where the rule separates the sea-ice
    /// section from the Palette block above. Set to <c>false</c> in the ECDIS
    /// Display Controls panel, where the per-spec section above already
    /// provides separation and the extra rule is redundant.
    /// </summary>
    public bool ShowDivider
    {
        get => GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }
}
