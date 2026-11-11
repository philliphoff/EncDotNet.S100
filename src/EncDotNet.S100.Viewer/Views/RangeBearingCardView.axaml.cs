using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using EncDotNet.S100.Viewer.ViewModels;
using Field = EncDotNet.S100.Viewer.ViewModels.RangeBearingViewModel.Field;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Range &amp; bearing card. Its fields commit on Enter or when focus
/// leaves them, Esc reverts a field (and only then reaches the window, which
/// leaves the tool), and ↑/↓ nudge the range and bearing (D14).
/// </summary>
internal sealed partial class RangeBearingCardView : UserControl
{
    public RangeBearingCardView()
    {
        InitializeComponent();
        Wire(OriginBox, Field.Origin);
        Wire(RangeBox, Field.Range);
        Wire(BearingBox, Field.Bearing);
        Wire(RingIntervalBox, Field.RingInterval);
        Wire(RingCountBox, Field.RingCount);
    }

    private RangeBearingViewModel? ViewModel => DataContext as RangeBearingViewModel;

    private void Wire(TextBox box, Field field)
    {
        box.GotFocus += (_, _) => ViewModel?.BeginEdit(field);
        box.LostFocus += (_, _) => ViewModel?.Commit(field);
        // Tunnel, so Enter and the arrows reach us before the window's tool
        // bindings and the text box's own caret handling.
        box.AddHandler(KeyDownEvent, (_, e) => OnFieldKeyDown(e, field), RoutingStrategies.Tunnel);
    }

    private void OnFieldKeyDown(KeyEventArgs e, Field field)
    {
        if (ViewModel is not { } vm)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                vm.Commit(field);
                e.Handled = true;
                break;
            case Key.Escape when vm.HasPendingEdit(field):
                vm.Revert(field);
                e.Handled = true;
                break;
            case Key.Up or Key.Down when field == Field.Bearing:
                vm.NudgeBearing((e.Key == Key.Up ? 1 : -1) * ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1));
                vm.BeginEdit(field);
                e.Handled = true;
                break;
            case Key.Up or Key.Down when field == Field.Range:
                vm.NudgeRange(e.Key == Key.Up ? 1 : -1);
                vm.BeginEdit(field);
                e.Handled = true;
                break;
        }
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.CopyText is { } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
