using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Copy chip next to a fingerprint in a Keys &amp; certificates dialog:
/// copies the field's value and reads "Copied" for a moment.
/// </summary>
internal static class KeysCopyChip
{
    public static void OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: KeysField field } button)
            return;
        try
        {
            if (TopLevel.GetTopLevel(button)?.Clipboard is { } clipboard)
                _ = clipboard.SetTextAsync(field.Value);
        }
        catch
        {
            // Best-effort; clipboard access can fail on some Linux WMs.
            return;
        }

        button.Content = Strings.Keys_Copied;
        DispatcherTimer.RunOnce(() => button.Content = Strings.Keys_Copy, TimeSpan.FromSeconds(1.5));
    }
}
