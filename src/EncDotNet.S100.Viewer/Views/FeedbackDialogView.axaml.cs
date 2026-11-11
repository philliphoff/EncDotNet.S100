using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Report Feedback" modal dialog content. The view
/// is resolved from <see cref="ViewModels.FeedbackDialogViewModel"/> via
/// a data template and hosted by ShadUI's dialog manager.
/// </summary>
public partial class FeedbackDialogView : UserControl
{
    public FeedbackDialogView()
    {
        InitializeComponent();
    }
}
