using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Add collection manifest" dialog (also "Choose
/// groups…"), resolved from <see cref="ViewModels.AddCollectionManifestDialogViewModel"/>
/// through the ShadUI dialog manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class AddCollectionManifestDialogView : UserControl
{
    public AddCollectionManifestDialogView()
    {
        InitializeComponent();
    }

    /// <summary>Room kept between the dialog and the window's top and bottom edges.</summary>
    private const double WindowMargin = 48;

    private TopLevel? _topLevel;

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WizardTints.Apply(this);

        // Never taller than the window: the dialog host does not shrink its content,
        // so without this the footer (Cancel / Add) is cut off in a short window.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
            FitToWindow();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty || e.Property == TopLevel.ClientSizeProperty)
            FitToWindow();
    }

    private void FitToWindow()
    {
        if (_topLevel is { ClientSize.Height: > 0 } topLevel)
            MaxHeight = Math.Max(0, topLevel.ClientSize.Height - WindowMargin);
    }
}
