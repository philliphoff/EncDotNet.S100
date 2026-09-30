using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Keeps a dialog's content no taller than its window. The ShadUI dialog host
/// does not shrink its content, so a dialog taller than the window has its
/// bottom (the footer with Cancel and the primary action) cut off. Views lay
/// out their body as a flexible row, so capping the height makes the body
/// shrink and scroll instead.
/// </summary>
internal sealed class DialogWindowFit
{
    /// <summary>Room kept between the dialog and the window's top and bottom edges.</summary>
    public const double WindowMargin = 48;

    private readonly Control _control;
    private TopLevel? _topLevel;

    /// <summary>Fits <paramref name="control"/> to its window while it is attached.</summary>
    public DialogWindowFit(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
        control.AttachedToVisualTree += (_, _) => Attach();
        control.DetachedFromVisualTree += (_, _) => Detach();
    }

    private void Attach()
    {
        Detach();
        _topLevel = TopLevel.GetTopLevel(_control);
        if (_topLevel is null)
            return;

        _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        Fit();
    }

    private void Detach()
    {
        if (_topLevel is not null)
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
        _topLevel = null;
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty || e.Property == TopLevel.ClientSizeProperty)
            Fit();
    }

    private void Fit()
    {
        if (_topLevel is { ClientSize.Height: > 0 } topLevel)
            _control.MaxHeight = Math.Max(0, topLevel.ClientSize.Height - WindowMargin);
    }
}
