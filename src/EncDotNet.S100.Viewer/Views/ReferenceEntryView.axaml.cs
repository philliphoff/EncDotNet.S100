using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// One chart reference entry (symbol dictionary design note §4.4, R7,
/// #1650), backed by <see cref="ViewModels.ChartReference.ReferenceEntryViewModel"/>:
/// the Reference tab and the study page's entry pane both host it.
/// </summary>
public partial class ReferenceEntryView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ReferenceEntryView() => AvaloniaXamlLoader.Load(this);
}
