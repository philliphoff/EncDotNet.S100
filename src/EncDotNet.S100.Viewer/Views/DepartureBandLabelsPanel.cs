using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Lays out the departure band's axis labels (#1038, handoff D5): its three
/// children are the start (left), middle (centred) and end (right) labels.
/// The labels never overlap. The middle label is the one the band can do
/// without, so it goes first: it is arranged empty (and clipped away) when it
/// would come within <see cref="Gap"/> of either neighbour. When even the start
/// and end labels don't fit side by side, the end label ("{time} · S-104 ends")
/// moves to a second line, still right-aligned, so it is never shortened.
/// </summary>
internal sealed class DepartureBandLabelsPanel : Panel
{
    /// <summary>The least space between two labels on one line.</summary>
    public const double Gap = 12;

    /// <summary>Whether the middle label is shown at the last arrange.</summary>
    public bool IsMiddleShown { get; private set; }

    /// <summary>Whether the end label is on its own line at the last arrange.</summary>
    public bool IsEndWrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
            child.Measure(unbounded);

        if (Children.Count != 3)
            return default;

        var (start, end) = (Children[0].DesiredSize, Children[2].DesiredSize);
        var width = double.IsInfinity(availableSize.Width) ? start.Width + Gap + end.Width : availableSize.Width;
        var height = EndWraps(width)
            ? start.Height + end.Height
            : Math.Max(Math.Max(start.Height, end.Height), Children[1].DesiredSize.Height);
        return new Size(Math.Min(width, Math.Max(start.Width, end.Width)), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3)
            return finalSize;

        var (start, middle, end) = (Children[0], Children[1], Children[2]);
        var width = finalSize.Width;
        var (s, m, e) = (start.DesiredSize, middle.DesiredSize, end.DesiredSize);

        start.Arrange(new Rect(0, 0, s.Width, s.Height));

        IsEndWrapped = EndWraps(width);
        var endTop = IsEndWrapped ? s.Height : 0;
        end.Arrange(new Rect(Math.Max(0, width - e.Width), endTop, e.Width, e.Height));

        // The middle label sits centred under the band's midpoint, or not at all.
        var middleLeft = (width - m.Width) / 2;
        var rightNeighbour = IsEndWrapped ? width : width - e.Width;
        IsMiddleShown = m.Width > 0
            && middleLeft >= s.Width + Gap
            && middleLeft + m.Width <= rightNeighbour - Gap;
        middle.Arrange(IsMiddleShown
            ? new Rect(middleLeft, 0, m.Width, m.Height)
            : new Rect(width / 2, 0, 0, 0));

        return finalSize;
    }

    private bool EndWraps(double width)
        => Children[0].DesiredSize.Width + Gap + Children[2].DesiredSize.Width > width;
}
