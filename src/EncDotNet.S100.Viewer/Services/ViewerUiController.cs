using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IViewerUiController"/> implementation — drives the
/// activity-panel state exposed by <see cref="MainViewModel"/> (dock open
/// flags and per-dock tab selection) through the Avalonia UI dispatcher.
/// </summary>
/// <remarks>
/// Reading and mutating the activity bar touches
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> state bound
/// to the UI, so every operation is marshalled through
/// <see cref="Dispatcher.UIThread"/> (a no-op when already on the UI
/// thread). Reads are marshalled too, so a caller always snapshots a
/// consistent panel state rather than racing the interactive activity bar.
/// </remarks>
internal sealed class ViewerUiController : IViewerUiController
{
    private readonly MainViewModel _viewModel;

    public ViewerUiController(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
    }

    public Task<IReadOnlyList<ViewerPanelState>> GetPanelsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.InvokeAsync(SnapshotPanels).GetTask();
    }

    public Task<PanelMutationOutcome> SetPanelVisibilityAsync(
        string panelId, bool visible, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        ct.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.InvokeAsync(() => ApplyVisibility(panelId, visible)).GetTask();
    }

    private IReadOnlyList<ViewerPanelState> SnapshotPanels()
    {
        // Left dock first, then right, then bottom; each dock in
        // activity-bar order (IActivityTab.Order ascending). MainViewModel
        // already sorts Tabs by Order, so a stable OrderBy on the dock is
        // sufficient to group without disturbing intra-dock ordering.
        return _viewModel.Tabs
            .OrderBy(t => DockRank(t.Dock))
            .Select(SnapshotPanel)
            .ToList();
    }

    private ViewerPanelState SnapshotPanel(IActivityTab tab)
    {
        var selected = IsSelected(tab);
        var dockOpen = IsDockOpen(tab.Dock);
        var available = tab.IsVisible;
        return new ViewerPanelState(
            Id: tab.Id,
            Title: tab.Title,
            Dock: tab.Dock.ToString(),
            Available: available,
            Selected: selected,
            DockOpen: dockOpen,
            Showing: available && selected && dockOpen);
    }

    private PanelMutationOutcome ApplyVisibility(string panelId, bool visible)
    {
        var tab = _viewModel.Tabs.FirstOrDefault(
            t => string.Equals(t.Id, panelId, StringComparison.OrdinalIgnoreCase));
        if (tab is null)
        {
            return new PanelMutationOutcome(Found: false, Available: false, State: null, PreviousShowing: false);
        }

        var previousShowing = SnapshotPanel(tab).Showing;

        if (!tab.IsVisible)
        {
            // An unavailable panel cannot be shown and is already hidden, so
            // both requests are no-ops. Report availability so the caller can
            // surface a "not available" error for an attempted show.
            return new PanelMutationOutcome(
                Found: true, Available: false, State: SnapshotPanel(tab), PreviousShowing: previousShowing);
        }

        if (visible)
        {
            ShowTab(tab);
        }
        else
        {
            HideTab(tab);
        }

        return new PanelMutationOutcome(
            Found: true, Available: true, State: SnapshotPanel(tab), PreviousShowing: previousShowing);
    }

    /// <summary>
    /// Selects the tab and opens its dock. Guards against
    /// <see cref="MainViewModel.SelectedLeftTab"/>'s toggle behaviour —
    /// re-selecting the already-selected left tab while its dock is open
    /// would close the dock, so a panel that is already showing is left
    /// untouched.
    /// </summary>
    private void ShowTab(IActivityTab tab)
    {
        if (IsSelected(tab) && IsDockOpen(tab.Dock))
        {
            return;
        }

        _viewModel.SelectTab(tab.Id);
    }

    /// <summary>
    /// Closes the panel's dock when the panel is the one currently shown
    /// there; otherwise a no-op (the panel is already hidden behind another
    /// tab, or the dock is already closed).
    /// </summary>
    private void HideTab(IActivityTab tab)
    {
        if (!IsSelected(tab) || !IsDockOpen(tab.Dock))
        {
            return;
        }

        switch (tab.Dock)
        {
            case TabDock.Left: _viewModel.IsLeftDockOpen = false; break;
            case TabDock.Right: _viewModel.IsRightDockOpen = false; break;
            case TabDock.Bottom: _viewModel.IsBottomDockOpen = false; break;
        }
    }

    private bool IsSelected(IActivityTab tab) => tab.Dock switch
    {
        TabDock.Left => string.Equals(_viewModel.SelectedLeftTabId, tab.Id, StringComparison.Ordinal),
        TabDock.Right => string.Equals(_viewModel.SelectedRightTabId, tab.Id, StringComparison.Ordinal),
        TabDock.Bottom => string.Equals(_viewModel.SelectedBottomTabId, tab.Id, StringComparison.Ordinal),
        _ => false,
    };

    private bool IsDockOpen(TabDock dock) => dock switch
    {
        TabDock.Left => _viewModel.IsLeftDockOpen,
        TabDock.Right => _viewModel.IsRightDockOpen,
        TabDock.Bottom => _viewModel.IsBottomDockOpen,
        _ => false,
    };

    private static int DockRank(TabDock dock) => dock switch
    {
        TabDock.Left => 0,
        TabDock.Right => 1,
        TabDock.Bottom => 2,
        _ => 3,
    };
}
