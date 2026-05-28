using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Pick Report (Object Information) panel. Renders the currently
/// picked feature's identity, references, attributes, and (for
/// S-104 / S-111 station picks) a time-series chart.
/// </summary>
/// <remarks>
/// The visibility gate (<c>IsPickPanelVisible</c> on
/// <c>MainViewModel</c>) is applied by the host in
/// <c>MainWindow.axaml</c>; this control assumes its
/// <see cref="Control.DataContext"/> is a
/// <c>PickReportViewModel</c> and that it is only realised when a
/// pick exists.
///
/// TODO PR-M4: register PickReportView as an activity tab with
/// 'pop on pick' preference.
/// </remarks>
public partial class PickReportView : UserControl
{
    public PickReportView()
    {
        InitializeComponent();
    }
}
