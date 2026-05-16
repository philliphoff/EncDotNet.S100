using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Modal window shown by the dataset loader when an open fails. The
/// primary message is shaped from the innermost structured S-100
/// exception (when present); the collapsible details pane carries the
/// full <see cref="object.ToString"/> output of the outermost exception
/// so the user can see the entire chain and stack trace.
/// </summary>
public partial class DatasetLoadFailureDialog : Window
{
    public DatasetLoadFailureDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Builds a <see cref="LoadFailureViewModel"/> from the failure
    /// inputs and shows the dialog modally over <paramref name="owner"/>.
    /// </summary>
    internal static Task ShowAsync(
        Window owner,
        string displayName,
        string filePath,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(exception);

        var vm = LoadFailureViewModel.FromException(displayName, filePath, exception);
        var dialog = new DatasetLoadFailureDialog
        {
            DataContext = vm,
        };
        return dialog.ShowDialog(owner);
    }

    private async void OnCopyDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LoadFailureViewModel vm)
            return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            await clipboard.SetTextAsync(vm.Details);

            var confirmation = this.FindControl<TextBlock>("CopyConfirmation");
            if (confirmation is null)
                return;

            confirmation.Opacity = 1.0;
            await Task.Delay(TimeSpan.FromSeconds(2));
            confirmation.Opacity = 0.0;
        }
        catch
        {
            // Best-effort. Clipboard access can fail on some Linux WMs;
            // swallowing here keeps the dialog usable.
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
