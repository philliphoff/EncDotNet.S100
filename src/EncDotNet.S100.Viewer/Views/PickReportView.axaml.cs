using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Pick Report (Object Information) panel. Renders the currently
/// picked feature's identity, references, attributes, and (for
/// S-104 / S-111 station picks) a time-series chart.
/// </summary>
/// <remarks>
/// The visibility gate (<c>IsPickPanelVisible</c> on
/// <c>MainViewModel</c>) is applied by the host in
/// <c>MainWindow.axaml</c>; this control assumes its
/// <see cref="Control.DataContext"/> is a
/// <c>PickReportViewModel</c> and that it is only realised when a
/// pick exists.
///
/// The view owns clipboard access for the "copy location" command:
/// the view-model raises an event with the coordinate text and the
/// view writes it to the top-level clipboard, keeping the view-model
/// free of any UI-clipboard dependency.
///
/// TODO PR-M4: register PickReportView as an activity tab with
/// 'pop on pick' preference.
/// </remarks>
public partial class PickReportView : UserControl
{
    private PickReportViewModel? _subscribed;

    public PickReportView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.CopyLocationRequested -= OnCopyLocationRequested;

        _subscribed = DataContext as PickReportViewModel;

        if (_subscribed is not null)
            _subscribed.CopyLocationRequested += OnCopyLocationRequested;
    }

    private void OnCopyLocationRequested(object? sender, string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                _ = clipboard.SetTextAsync(text);
        }
        catch
        {
            // Best-effort; clipboard access can fail on some Linux WMs.
        }
    }
}
