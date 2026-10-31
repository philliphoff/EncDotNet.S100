using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The measure card (#1130, D1). Enter or leaving a position field commits
/// it; Esc reverts it, and a second Esc leaves the tool through the window's
/// Esc binding (D20). Copy puts the view model's text on the clipboard.
/// </summary>
public partial class MeasureCardView : UserControl
{
    private MeasureCardViewModel? _viewModel;

    public MeasureCardView()
    {
        InitializeComponent();
        FromBox.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
        ToBox.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
        FromBox.LostFocus += (_, _) => _viewModel?.CommitFrom();
        ToBox.LostFocus += (_, _) => _viewModel?.CommitTo();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.CopyRequested -= OnCopyRequested;
        _viewModel = DataContext as MeasureCardViewModel;
        if (_viewModel is not null)
            _viewModel.CopyRequested += OnCopyRequested;
    }

    private void OnCopyRequested(object? sender, string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.KeyModifiers != KeyModifiers.None)
            return;
        var isFrom = ReferenceEquals(sender, FromBox);
        switch (e.Key)
        {
            case Key.Enter:
                // Handled either way, so Enter never also finishes the path.
                _ = isFrom ? _viewModel.CommitFrom() : _viewModel.CommitTo();
                e.Handled = true;
                break;
            case Key.Escape:
                // Nothing to revert: let Esc reach the window and leave the tool.
                if (isFrom ? _viewModel.RevertFrom() : _viewModel.RevertTo())
                    e.Handled = true;
                break;
        }
    }
}
