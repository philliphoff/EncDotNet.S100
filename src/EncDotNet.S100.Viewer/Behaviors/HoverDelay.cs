using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace EncDotNet.S100.Viewer.Behaviors;

/// <summary>
/// Attached behavior that adds a <c>hovered</c> style class to a control
/// after the pointer has remained over it for a configurable delay, and
/// removes it immediately on exit. Used by the pane splitters so a brief
/// mouse pass-through doesn't flash the accent color while a sustained
/// hover still reveals it.
/// </summary>
internal static class HoverDelay
{
    /// <summary>
    /// Delay (in milliseconds) the pointer must remain over the control
    /// before the <c>hovered</c> class is applied. Setting to a value
    /// greater than zero wires the behavior; zero disables it.
    /// </summary>
    public static readonly AttachedProperty<int> DelayMillisecondsProperty =
        AvaloniaProperty.RegisterAttached<Control, int>(
            "DelayMilliseconds",
            typeof(HoverDelay));

    public static int GetDelayMilliseconds(Control control) =>
        control.GetValue(DelayMillisecondsProperty);

    public static void SetDelayMilliseconds(Control control, int value) =>
        control.SetValue(DelayMillisecondsProperty, value);

    // Per-control timer storage so multiple splitters don't share state.
    private static readonly AttachedProperty<DispatcherTimer?> TimerProperty =
        AvaloniaProperty.RegisterAttached<Control, DispatcherTimer?>(
            "Timer",
            typeof(HoverDelay));

    static HoverDelay()
    {
        DelayMillisecondsProperty.Changed.AddClassHandler<Control>(OnDelayChanged);
    }

    private static void OnDelayChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerEntered -= OnPointerEntered;
        control.PointerExited -= OnPointerExited;
        control.DetachedFromVisualTree -= OnDetached;
        ClearTimer(control);
        control.Classes.Remove("hovered");

        var delay = (int)(args.NewValue ?? 0);
        if (delay <= 0)
        {
            return;
        }

        control.PointerEntered += OnPointerEntered;
        control.PointerExited += OnPointerExited;
        control.DetachedFromVisualTree += OnDetached;
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control)
            return;

        var delay = GetDelayMilliseconds(control);
        if (delay <= 0)
            return;

        ClearTimer(control);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delay),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            control.SetValue(TimerProperty, null);
            control.Classes.Add("hovered");
        };
        control.SetValue(TimerProperty, timer);
        timer.Start();
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control)
            return;

        ClearTimer(control);
        control.Classes.Remove("hovered");
    }

    private static void OnDetached(object? sender, EventArgs e)
    {
        if (sender is not Control control)
            return;

        ClearTimer(control);
        control.Classes.Remove("hovered");
    }

    private static void ClearTimer(Control control)
    {
        var existing = control.GetValue(TimerProperty);
        if (existing is not null)
        {
            existing.Stop();
            control.SetValue(TimerProperty, null);
        }
    }
}
