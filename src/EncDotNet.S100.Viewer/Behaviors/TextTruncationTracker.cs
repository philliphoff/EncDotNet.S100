using System;
using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Behaviors;

/// <summary>
/// Attached behavior that reports whether a <see cref="TextBlock"/>'s text is
/// clipped by its line cap (for example a body capped at two lines with
/// <c>TextTrimming=CharacterEllipsis</c>). The result is exposed through the
/// one-way-to-source <see cref="IsTruncatedProperty"/> so a bound view-model
/// can show or hide a "Show more" affordance.
/// </summary>
/// <remarks>
/// Avalonia's <c>TextBlock</c> has no public "is trimmed" signal, so this
/// inspects the laid-out <see cref="TextBlock.TextLayout"/> after each layout
/// pass and looks for a collapsed (ellipsized) line. Truncation is only
/// recomputed while the text is constrained; an expanded (unlimited) block
/// never reports truncation, so callers keep the link visible via their own
/// "is expanded" state.
/// </remarks>
internal static class TextTruncationTracker
{
    /// <summary>
    /// Enables tracking on the attached <see cref="TextBlock"/>. Set to
    /// <see langword="true"/> to wire layout observation.
    /// </summary>
    public static readonly AttachedProperty<bool> TrackProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>(
            "Track",
            typeof(TextTruncationTracker));

    public static bool GetTrack(TextBlock control) => control.GetValue(TrackProperty);

    public static void SetTrack(TextBlock control, bool value) =>
        control.SetValue(TrackProperty, value);

    /// <summary>
    /// Reports whether the tracked text is currently clipped. Intended to be
    /// bound <see cref="Avalonia.Data.BindingMode.OneWayToSource"/> to a
    /// view-model flag.
    /// </summary>
    public static readonly AttachedProperty<bool> IsTruncatedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>(
            "IsTruncated",
            typeof(TextTruncationTracker),
            defaultBindingMode: Avalonia.Data.BindingMode.OneWayToSource);

    public static bool GetIsTruncated(TextBlock control) =>
        control.GetValue(IsTruncatedProperty);

    public static void SetIsTruncated(TextBlock control, bool value) =>
        control.SetValue(IsTruncatedProperty, value);

    static TextTruncationTracker()
    {
        TrackProperty.Changed.AddClassHandler<TextBlock>(OnTrackChanged);
    }

    private static void OnTrackChanged(TextBlock control, AvaloniaPropertyChangedEventArgs args)
    {
        control.LayoutUpdated -= OnLayoutUpdated;
        if (args.NewValue is true)
            control.LayoutUpdated += OnLayoutUpdated;
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not TextBlock control)
            return;

        SetIsTruncated(control, IsTrimmed(control));
    }

    private static bool IsTrimmed(TextBlock control)
    {
        var layout = control.TextLayout;
        if (layout is null)
            return false;

        foreach (var line in layout.TextLines)
        {
            if (line.HasCollapsed)
                return true;
        }

        return false;
    }
}
