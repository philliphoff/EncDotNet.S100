using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Chart reference activity (#1637): product, search, Features / Symbols
/// and the list, backed by <see cref="ViewModels.ChartReference.ChartReferenceViewModel"/>.
/// </summary>
public partial class ChartReferenceView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ChartReferenceView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The index is built when the panel is first shown, not at startup.
        (DataContext as ChartReferenceViewModel)?.Activate();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
            (DataContext as ChartReferenceViewModel)?.Activate();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
