using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Keeps a dialog's content no taller and no wider than its window. The ShadUI
/// dialog host does not shrink its content, so a dialog taller than the window
/// has its bottom (the footer with Cancel and the primary action) cut off, and
/// one wider than it loses its sides. Views lay out their body as a flexible
/// row, so capping the height makes the body shrink and scroll instead; the
/// width is capped too, but never below <see cref="MinimumWidth"/>.
/// </summary>
internal sealed class DialogWindowFit
{
    /// <summary>Room kept between the dialog and the window's top and bottom edges.</summary>
    public const double WindowMargin = 48;

    /// <summary>Room kept between the dialog and the window's left and right edges.</summary>
    public const double WindowSideMargin = 96;

    /// <summary>The narrowest the width cap goes; narrower dialogs are never affected.</summary>
    public const double MinimumWidth = 640;

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
        if (_topLevel is not { } topLevel)
            return;

        if (topLevel.ClientSize.Height > 0)
            _control.MaxHeight = MaxHeightFor(topLevel.ClientSize.Height);
        if (topLevel.ClientSize.Width > 0)
            _control.MaxWidth = MaxWidthFor(topLevel.ClientSize.Width);
    }

    /// <summary>The tallest a dialog may be in a window <paramref name="windowHeight"/> tall.</summary>
    internal static double MaxHeightFor(double windowHeight) => Math.Max(0, windowHeight - WindowMargin);

    /// <summary>The widest a dialog may be in a window <paramref name="windowWidth"/> wide (never below <see cref="MinimumWidth"/>).</summary>
    internal static double MaxWidthFor(double windowWidth) => Math.Max(MinimumWidth, windowWidth - WindowSideMargin);
}
