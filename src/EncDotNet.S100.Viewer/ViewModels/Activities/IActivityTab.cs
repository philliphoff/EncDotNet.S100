using System;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Describes a single tab in the viewer's activity bar. One implementation
/// per tab, registered via
/// <see cref="ActivityTabServiceCollectionExtensions.AddActivityTab{TViewModel, TView}"/>
/// and consumed by <see cref="MainViewModel"/> as
/// <c>IEnumerable&lt;IActivityTab&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tabs are identified by stable string <see cref="Id"/> values (e.g.
/// <c>"Datasets"</c>) so they round-trip cleanly through
/// <see cref="ViewerSettings.LastSelectedActivity"/>. Render order in the
/// activity bar is controlled by <see cref="Order"/> ascending.
/// </para>
/// <para>
/// This type is kept <c>internal</c>: external / plugin-supplied tabs are
/// explicitly out of scope for PR-M1.
/// </para>
/// </remarks>
internal interface IActivityTab
{
    /// <summary>
    /// Stable identifier (e.g. <c>"Datasets"</c>). Used to bind the
    /// activity-bar buttons to <see cref="MainViewModel.SelectedTabId"/>
    /// and to persist the last-selected tab in
    /// <see cref="ViewerSettings.LastSelectedActivity"/>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Render order in the activity bar — ascending top-to-bottom. Tabs
    /// with <see cref="Order"/> &gt;= 1000 are pinned to the bottom of
    /// the bar (currently only <c>Settings</c>).
    /// </summary>
    int Order { get; }

    /// <summary>Resolved pane header text (already localised).</summary>
    string Title { get; }

    /// <summary>
    /// Creates a fresh icon control for this tab. Called by the
    /// <c>ItemsControl</c> item template — Avalonia controls have a single
    /// parent so each consumer gets its own instance.
    /// </summary>
    Control CreateIcon();

    /// <summary>Resolved tool-tip text (already localised).</summary>
    string Tooltip { get; }

    /// <summary>
    /// The view-model instance for this tab. Bound as the
    /// <c>DataContext</c> of the view by
    /// <see cref="ActivityTabViewTemplate"/>.
    /// </summary>
    object ViewModel { get; }

    /// <summary>
    /// The <see cref="UserControl"/> type to instantiate when this tab is
    /// active. The view must have a parameterless constructor (PR-M1
    /// limitation — see template TODO for future DI-resolved views).
    /// </summary>
    Type ViewType { get; }

    /// <summary>
    /// When <c>true</c>, selecting this tab updates
    /// <see cref="ViewerSettings.LastSelectedActivity"/>. <c>false</c> for
    /// the Settings tab so it doesn't become "sticky" across restarts.
    /// </summary>
    bool PersistAsLastSelected { get; }

    /// <summary>
    /// Which dock this tab lives in. Drives the
    /// <c>LeftTabs</c>/<c>RightTabs</c>/<c>BottomTabs</c> partitioning
    /// on <see cref="MainViewModel"/>. Added in PR-M4 so the Pick
    /// Report and Timeline can be modelled as ordinary tabs rather
    /// than bespoke panels.
    /// </summary>
    TabDock Dock { get; }

    /// <summary>
    /// When <c>true</c>, <see cref="MainViewModel"/> subscribes to the
    /// tab view-model's <see cref="IActivityTabContentSignal.ContentBecameAvailable"/>
    /// event (if the view-model implements it) and auto-opens the
    /// owning dock and selects the tab when that event fires. The user
    /// can still close the dock; the auto-open re-runs on the next
    /// false→true content transition.
    /// </summary>
    bool AutoOpenOnContentSignal { get; }
}
