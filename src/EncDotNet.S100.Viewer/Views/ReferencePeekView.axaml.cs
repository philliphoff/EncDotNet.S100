using Avalonia.Controls;
using Avalonia.Input;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>Which way focus leaves a pinned peek.</summary>
/// <param name="forward">Tab past the last link (<see langword="true"/>), or Shift+Tab before the first or Escape.</param>
internal sealed class PeekLeaveEventArgs(bool forward) : EventArgs
{
    /// <summary>Whether focus goes on past the term; otherwise back to it.</summary>
    public bool Forward { get; } = forward;
}

/// <summary>
/// A term's peek (symbol dictionary design note §4.2, R8, #1651), shown in
/// the term's popup. Tab and Shift+Tab move between its links; past either
/// end, and on Escape, it asks the term to take focus back
/// (<see cref="LeaveRequested"/>), so a pinned peek traps nothing.
/// </summary>
internal sealed partial class ReferencePeekView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ReferencePeekView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when focus should leave the peek.</summary>
    public event EventHandler<PeekLeaveEventArgs>? LeaveRequested;

    /// <summary>Focuses Open in reference.</summary>
    public void FocusFirstLink() => OpenLink.Focus(NavigationMethod.Tab);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            LeaveRequested?.Invoke(this, new PeekLeaveEventArgs(forward: false));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            Button[] links = ExaminerLink.IsVisible ? [OpenLink, ExaminerLink] : [OpenLink];
            int at = Array.FindIndex(links, l => l.IsFocused);
            int next = at + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            if (at >= 0 && next >= 0 && next < links.Length)
                links[next].Focus(NavigationMethod.Tab);
            else
                LeaveRequested?.Invoke(this, new PeekLeaveEventArgs(forward: next >= links.Length || at < 0));
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
