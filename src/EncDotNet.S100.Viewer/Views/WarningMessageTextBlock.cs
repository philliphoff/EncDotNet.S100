using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A navigational warning's message text (#932): selectable, line breaks and
/// indentation kept, with the warning numbers that name another listed
/// warning drawn as links. Clicking a link, without dragging a selection,
/// runs <see cref="LinkCommand"/> with the warning it names.
/// </summary>
/// <remarks>
/// The links are runs inside one text block rather than buttons, so the text
/// still selects and copies across them and wraps as one block.
/// </remarks>
internal sealed class WarningMessageTextBlock : SelectableTextBlock
{
    /// <summary>The text as plain runs and links.</summary>
    public static readonly StyledProperty<IReadOnlyList<WarningTextSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<WarningMessageTextBlock, IReadOnlyList<WarningTextSegment>?>(nameof(Segments));

    /// <summary>Run with a link's <see cref="WarningTextSegment.Target"/>.</summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<WarningMessageTextBlock, ICommand?>(nameof(LinkCommand));

    /// <summary>The links' colour.</summary>
    public static readonly StyledProperty<IBrush?> LinkForegroundProperty =
        AvaloniaProperty.Register<WarningMessageTextBlock, IBrush?>(nameof(LinkForeground));

    private WarningTextSegment? _pressedLink;

    static WarningMessageTextBlock()
    {
        SegmentsProperty.Changed.AddClassHandler<WarningMessageTextBlock>((t, _) => t.Rebuild());
        LinkForegroundProperty.Changed.AddClassHandler<WarningMessageTextBlock>((t, _) => t.Rebuild());
    }

    /// <inheritdoc cref="SegmentsProperty"/>
    public IReadOnlyList<WarningTextSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <inheritdoc cref="LinkCommandProperty"/>
    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    /// <inheritdoc cref="LinkForegroundProperty"/>
    public IBrush? LinkForeground
    {
        get => GetValue(LinkForegroundProperty);
        set => SetValue(LinkForegroundProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        foreach (var segment in Segments ?? [])
        {
            var run = new Run(segment.Text);
            if (segment.IsLink)
            {
                run.Foreground = LinkForeground;
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            }
            inlines.Add(run);
        }
        Inlines = inlines;
    }

    /// <summary>The link under <paramref name="point"/>, in this control's coordinates; <c>null</c> over plain text.</summary>
    private WarningTextSegment? LinkAt(Point point)
    {
        if (Segments is not { Count: > 0 } segments)
            return null;
        var layout = TextLayout;
        point -= new Point(Padding.Left, Padding.Top);

        // HitTestPoint gives the nearest caret position, which is after the
        // character when the point is on its trailing half: take the
        // character whose box holds the point.
        var caret = layout.HitTestPoint(point).TextPosition;
        int? index = null;
        foreach (var candidate in (ReadOnlySpan<int>)[caret, caret - 1])
        {
            if (candidate < 0)
                continue;
            var box = layout.HitTestTextPosition(candidate);
            if (point.X >= box.Left && point.X < box.Right && point.Y >= box.Top && point.Y < box.Bottom)
            {
                index = candidate;
                break;
            }
        }
        if (index is null)
            return null;

        var start = 0;
        foreach (var segment in segments)
        {
            var end = start + segment.Text.Length;
            if (index >= start && index < end)
                return segment.IsLink ? segment : null;
            start = end;
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = LinkAt(e.GetPosition(this)) is not null
            ? new Cursor(StandardCursorType.Hand)
            : new Cursor(StandardCursorType.Ibeam);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressedLink = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? LinkAt(e.GetPosition(this)) : null;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pressed = _pressedLink;
        _pressedLink = null;
        // A drag that selected text is a selection, not a click.
        if (pressed is null || SelectionStart != SelectionEnd)
            return;
        if (!ReferenceEquals(LinkAt(e.GetPosition(this)), pressed))
            return;
        if (LinkCommand is { } command && command.CanExecute(pressed.Target))
        {
            command.Execute(pressed.Target);
            e.Handled = true;
        }
    }
}
