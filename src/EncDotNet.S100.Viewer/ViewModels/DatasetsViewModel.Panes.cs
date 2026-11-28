using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The rows' pane chips while the map is split (split map step 2 §2.6, D18).</summary>
internal sealed partial class DatasetsViewModel
{
    private ITimelinePanes? _panes;
    private Action<Action>? _panesDispatch;
    private bool _panesRefreshPending;

    /// <summary>
    /// Follows the map panes: while the map is split, each dataset row and
    /// exchange-set header carries one number chip per pane in place of its
    /// eye (<see cref="DatasetEntry.PaneChips"/>,
    /// <see cref="ExchangeSetHeader.PaneChips"/>).
    /// </summary>
    /// <param name="panes">The panes in reading order, raising a change on any pane or visibility change.</param>
    /// <param name="dispatch">
    /// Runs the refresh on the UI thread. The app posts it, so a burst of
    /// changes (an exchange set's cells shown in a pane) refreshes once.
    /// </param>
    internal void AttachPanes(ITimelinePanes panes, Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(panes);
        ArgumentNullException.ThrowIfNull(dispatch);
        if (_panes is not null)
        {
            _panes.Changed -= OnPanesChanged;
            ExchangeSetHeaders.CollectionChanged -= OnHeadersChangedForPanes;
        }
        _panes = panes;
        _panesDispatch = dispatch;
        _panes.Changed += OnPanesChanged;
        ExchangeSetHeaders.CollectionChanged += OnHeadersChangedForPanes;
        RefreshPaneChips();
    }

    private void OnHeadersChangedForPanes(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => OnPanesChanged();

    private void OnPanesChanged()
    {
        if (_panesDispatch is not { } dispatch || _panesRefreshPending)
            return;
        _panesRefreshPending = true;
        dispatch(() =>
        {
            _panesRefreshPending = false;
            RefreshPaneChips();
        });
    }

    /// <summary>Gives every row and header the chips of the current panes, with their states.</summary>
    private void RefreshPaneChips()
    {
        if (_panes is null)
            return;
        var panes = _panes.Panes;
        foreach (var entry in Entries)
            entry.SyncPanes(panes);
        foreach (var header in ExchangeSetHeaders)
            header.SyncPanes(panes);
    }
}
