using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The status bar's depth basis pill (#1191). <see cref="Fit"/> picks the
/// longest whole form of the value that fits the room the status bar has
/// (<c>docs/design/depth-basis-indicator.md</c> §2.2.1).
/// </summary>
public partial class DepthBasisIndicatorView : UserControl
{
    public DepthBasisIndicatorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Draws the longest form whose pill fits in <paramref name="room"/>
    /// device-independent pixels, else the compact form.
    /// </summary>
    /// <param name="room">The width the pill may take.</param>
    public void Fit(double room)
    {
        if (DataContext is not DepthBasisIndicatorViewModel vm || !vm.IsVisible)
            return;
        if (this.FindControl<TextBlock>("ValueText") is not { } value)
            return;

        // The pill's width without its value text: the label, icon, padding and margin.
        var chrome = DesiredSize.Width - value.DesiredSize.Width;
        var widths = vm.FormTexts.Select(text => Measure(value, text)).ToList();
        vm.Form = Choose(room, chrome, widths);
    }

    /// <summary>
    /// The longest form that fits: the first of <paramref name="formWidths"/>
    /// (Full, Short, Compact) whose pill is no wider than <paramref name="room"/>;
    /// Compact when none does.
    /// </summary>
    /// <param name="room">The width the pill may take.</param>
    /// <param name="chrome">The pill's width without its value text.</param>
    /// <param name="formWidths">Each form's text width, Full first.</param>
    /// <returns>The form to draw.</returns>
    internal static DepthBasisForm Choose(double room, double chrome, IReadOnlyList<double> formWidths)
    {
        ArgumentNullException.ThrowIfNull(formWidths);
        for (var i = 0; i < formWidths.Count; i++)
        {
            if (chrome + formWidths[i] <= room + 0.5)
                return (DepthBasisForm)i;
        }

        return DepthBasisForm.Compact;
    }

    private static double Measure(TextBlock like, string text)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontFamily = like.FontFamily,
            FontSize = like.FontSize,
            FontWeight = like.FontWeight,
            FontStyle = like.FontStyle,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }
}
