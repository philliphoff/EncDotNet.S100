using Avalonia.Controls;
using Avalonia.Input;
using EncDotNet.S100.Viewer.ViewModels;
using LiveChartsCore.Drawing;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Clearance tab (#902): the clearance profile along the checked route,
/// its readout and Sources. Bound to <see cref="ClearanceViewModel"/>.
/// </summary>
public partial class ClearanceView : UserControl
{
    public ClearanceView()
    {
        InitializeComponent();
    }

    // Hovering the profile moves the shared cursor, which moves the map ghost (D3).
    private void OnChartPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ClearanceViewModel vm || sender is not LiveChartsCore.SkiaSharpView.Avalonia.CartesianChart chart)
            return;
        var p = e.GetPosition(chart);
        var data = chart.ScalePixelsToData(new LvcPointD(p.X, p.Y));
        if (double.IsFinite(data.X))
            vm.SetCursorAtX(data.X);
    }
}
