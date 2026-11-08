using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the source step of the "Add online source" wizard: headers
/// are not selectable, the chosen source is scrolled into view (for example
/// after one is added by URL) with the list's own
/// <see cref="ListBox.ScrollIntoView(object)"/>, the layout follows the
/// body's width (D2), a catalogue URL is copied to the clipboard, and the URL
/// form takes focus when "Add by URL" opens My sources (D10).
/// </summary>
public partial class CatalogueDirectoryDialogView : UserControl
{
    /// <summary>At or above this width the chosen source's details sit in a side pane (D8).</summary>
    public const double WideMinWidth = 840;

    /// <summary>Below this width the category sidebar becomes a segmented strip (D2).</summary>
    public const double CompactMinWidth = 720;

    private CatalogueDirectoryDialogViewModel? _viewModel;

    public CatalogueDirectoryDialogView()
    {
        InitializeComponent();
        CatalogueList.ContainerPrepared += (_, e) =>
            e.Container.IsEnabled = CatalogueList.ItemFromContainer(e.Container) is not CatalogueEntryViewModel { IsHeaderText: true };
        ApplyLayout(0);
    }

    /// <summary>"wide", "compact" or "narrow": the layout for <paramref name="width"/>.</summary>
    public static string LayoutFor(double width) =>
        width >= WideMinWidth ? "wide" : width >= CompactMinWidth ? "compact" : "narrow";

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged)
            ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        var layout = LayoutFor(width);
        Classes.Set("wide", layout == "wide");
        Classes.Set("compact", layout == "compact");
        Classes.Set("narrow", layout == "narrow");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CopyRequested -= OnCopyRequested;
            _viewModel.UrlEntryRequested -= OnUrlEntryRequested;
        }

        _viewModel = DataContext as CatalogueDirectoryDialogViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.CopyRequested += OnCopyRequested;
            _viewModel.UrlEntryRequested += OnUrlEntryRequested;
        }
    }

    // The form is shown by the category change; focus it once it is laid out.
    private void OnUrlEntryRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => UrlBox.Focus(), DispatcherPriority.Loaded);

    private void OnCopyRequested(object? sender, string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                _ = clipboard.SetTextAsync(text);
        }
        catch
        {
            // Best-effort, as in the Library panel; clipboard access can fail on some Linux WMs.
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CatalogueDirectoryDialogViewModel.SelectedEntry) || _viewModel?.SelectedEntry is not { } entry)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.Rows.Contains(entry) == true)
                CatalogueList.ScrollIntoView(entry);
        }, DispatcherPriority.Loaded);
    }
}
