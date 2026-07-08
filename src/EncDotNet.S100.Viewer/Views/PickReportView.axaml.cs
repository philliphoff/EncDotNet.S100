using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Resources;
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
        {
            _subscribed.CopyLocationRequested -= OnCopyTextRequested;
            _subscribed.CopyIdentityRequested -= OnCopyTextRequested;
        }

        _subscribed = DataContext as PickReportViewModel;

        if (_subscribed is not null)
        {
            _subscribed.CopyLocationRequested += OnCopyTextRequested;
            _subscribed.CopyIdentityRequested += OnCopyTextRequested;
        }
    }

    private void OnCopyTextRequested(object? sender, string text)
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

    // Egg-code value hover: surface the value's meaning in the description
    // region below the egg instead of a per-cell tooltip (which would obscure
    // neighbouring values the mariner may want to compare).
    private void OnEggCellPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: IceEggValue value }
            && DataContext is PickReportViewModel { SelectedEggCode: { } egg })
        {
            egg.HoveredDescription = BuildEggValueDescription(value);
        }
    }

    private void OnEggCellPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is PickReportViewModel { SelectedEggCode: { } egg })
            egg.HoveredDescription = null;
    }

    private static string BuildEggValueDescription(IceEggValue value)
    {
        // Compose "symbol value — meaning", e.g. "Sb 85 — Grey-White Ice" or,
        // for a value with no Feature-Catalogue definition, "Ct 90 — Total
        // concentration". The positional symbol carries the WMO subscript so
        // the role label stays terse.
        var meaning = value.Definition;
        if (string.IsNullOrWhiteSpace(meaning))
        {
            meaning = EggCodeRoleTooltipConverter.Instance.Convert(
                value.Role, typeof(string), null, Strings.Culture) as string;
        }

        var head = string.IsNullOrEmpty(value.Symbol)
            ? value.Text
            : $"{value.Symbol} {value.Text}";

        return string.IsNullOrWhiteSpace(meaning)
            ? head
            : $"{head} — {meaning}";
    }
}
