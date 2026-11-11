using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A short line drawn exactly like a dataset's coverage outline on the map
/// (<see cref="LibraryOutlineStyles"/>), so the Library list works as the map
/// legend. Pens are shared; one line is drawn per render.
/// </summary>
internal sealed class LibrarySwatch : Control
{
    /// <summary>The primary availability to draw.</summary>
    public static readonly StyledProperty<LibraryPrimaryAvailability> StateProperty =
        AvaloniaProperty.Register<LibrarySwatch, LibraryPrimaryAvailability>(nameof(State));

    /// <summary>The control's height in pixels; each line is drawn at its map width, centred.</summary>
    public const double Thickness = 2;

    private static readonly Dictionary<LibraryPrimaryAvailability, ImmutablePen> Pens = Enum
        .GetValues<LibraryPrimaryAvailability>()
        .ToDictionary(s => s, s => CreatePen(LibraryOutlineStyles.For(s)));

    static LibrarySwatch()
    {
        AffectsRender<LibrarySwatch>(StateProperty);
    }

    public LibrarySwatch()
    {
        Width = 16;
        Height = Thickness;
    }

    public LibraryPrimaryAvailability State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var y = Bounds.Height / 2;
        context.DrawLine(Pens[State], new Point(0, y), new Point(Bounds.Width, y));
    }

    private static ImmutablePen CreatePen(LibraryOutlineStyle style)
    {
        // Avalonia dash lengths are multiples of the line width; the map's are pixels.
        var dashes = style.DashArray is { } d ? new ImmutableDashStyle(d.Select(x => x / style.Width), 0) : null;
        return new ImmutablePen(new ImmutableSolidColorBrush(style.Color), style.Width, dashes,
            style.RoundCap ? PenLineCap.Round : PenLineCap.Flat);
    }
}
