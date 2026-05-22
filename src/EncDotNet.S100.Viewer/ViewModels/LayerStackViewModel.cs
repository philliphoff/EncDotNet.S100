using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the Layer Stack panel (PR-L3). Subscribes to
/// <see cref="IDatasetLoaderService.LayerStackChanged"/> and reflects
/// the rich <see cref="LayerStackEntry"/> snapshot from
/// <see cref="IDatasetLoaderService.CurrentStackEntries"/> as a list
/// of <see cref="LayerStackPlaneViewModel"/> groups — one per
/// <see cref="S98DisplayPlane"/> — ordered top-of-stack first
/// (S-98 §A-3.2.1.1 + MSC.530(106)/Rev.1 §Appendix 2).
/// </summary>
/// <remarks>
/// <para>
/// Empty planes (those with no entries) are hidden by default. The
/// user can flip the <see cref="ShowEmptyPlanes"/> toggle to see
/// every canonical S-98 plane regardless.
/// </para>
/// <para>
/// Per-plane expansion state is preserved across rebuilds keyed by
/// the plane enum value so a load/remove/active-toggle doesn't
/// collapse the user's previously expanded planes.
/// </para>
/// </remarks>
internal sealed class LayerStackViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;
    private readonly ITimeFormatProvider? _timeFormat;
    // PR-L4 reserve: kept on the field so the constructor still
    // captures it. _ = is enough to suppress unused-field warnings.
    // The plan reserves ITimeFormatProvider for time-aware status
    // text on per-entry rows.

    /// <summary>Plane group order: top-of-paint-stack first.</summary>
    private static readonly S98DisplayPlane[] PlanesTopFirst =
    {
        S98DisplayPlane.EcdisAlerts,
        S98DisplayPlane.MarinerOverlay,
        S98DisplayPlane.DynamicArrows,
        S98DisplayPlane.CautionsAndWarnings,
        S98DisplayPlane.OtherChartOverlays,
        S98DisplayPlane.BaseChartOver,
        S98DisplayPlane.OnDemandSurface,
        S98DisplayPlane.Bathymetry,
        S98DisplayPlane.BaseChartUnder,
    };

    public ObservableCollection<LayerStackPlaneViewModel> Planes { get; } = new();

    private readonly Dictionary<S98DisplayPlane, bool> _planeExpansion = new();

    private bool _showEmptyPlanes;
    /// <summary>
    /// When true the panel shows every canonical S-98 display plane
    /// even if no loaded dataset contributes layers to it. Default
    /// false to keep the tree compact for typical single-product
    /// sessions.
    /// </summary>
    public bool ShowEmptyPlanes
    {
        get => _showEmptyPlanes;
        set
        {
            if (SetProperty(ref _showEmptyPlanes, value))
            {
                Rebuild();
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    /// <summary>True when no plane row is currently displayed.</summary>
    public bool IsEmpty => Planes.Count == 0;

    /// <summary>Empty-state body text (S-128 PdC picker arrives in PR-L4).</summary>
    public string EmptyMessage => Strings.LayerStack_EmptyPlane_Placeholder;

    /// <summary>"Active vs. Visibility" help text for the panel footer.</summary>
    public string ActiveVsVisibilityHelp => Strings.LayerStack_VisibilityVsActive_HelpText;

    public LayerStackViewModel(IDatasetLoaderService loader, ITimeFormatProvider? timeFormat = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _timeFormat = timeFormat;
        _ = _timeFormat; // reserved for PR-L4 per-entry timestamp display
        _loader.LayerStackChanged += OnLayerStackChanged;
        Rebuild();
    }

    private void OnLayerStackChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Rebuild();
        else
            Dispatcher.UIThread.Post(Rebuild);
    }

    /// <summary>
    /// Rebuilds the plane tree from the loader's current
    /// <see cref="LayerStackEntry"/> snapshot. Preserves
    /// <see cref="LayerStackPlaneViewModel.IsExpanded"/> per plane
    /// across rebuilds via <see cref="_planeExpansion"/>.
    /// </summary>
    public void Rebuild()
    {
        // Snapshot expansion state from existing plane VMs so it
        // survives the collection reset.
        foreach (var existing in Planes)
            _planeExpansion[existing.Plane] = existing.IsExpanded;

        Planes.Clear();

        var entries = _loader.CurrentStackEntries;
        // Group by plane preserving entry order (bottom-of-paint-stack
        // first → within-plane priority asc, which is the order the
        // authority hands them back).
        var byPlane = new Dictionary<S98DisplayPlane, List<LayerStackEntry>>();
        foreach (var e in entries)
        {
            if (!byPlane.TryGetValue(e.Plane, out var list))
            {
                list = new List<LayerStackEntry>();
                byPlane[e.Plane] = list;
            }
            list.Add(e);
        }

        foreach (var plane in PlanesTopFirst)
        {
            byPlane.TryGetValue(plane, out var planeEntries);
            var planeEntriesList = planeEntries ?? new List<LayerStackEntry>();
            if (planeEntriesList.Count == 0 && !_showEmptyPlanes) continue;

            // Within-plane order: render the child list top-of-plane
            // first (highest WithinPlanePriority first) so the tree
            // reads like the paint stack — top wins.
            var children = planeEntriesList
                .OrderByDescending(e => e.WithinPlanePriority)
                .ThenBy(e => e.SourceDatasetId, StringComparer.Ordinal)
                .Select(e => new LayerStackEntryViewModel(_loader, e))
                .ToList();

            var isExpanded = !_planeExpansion.TryGetValue(plane, out var prev) || prev;
            var vm = new LayerStackPlaneViewModel(plane, children, isExpanded);
            Planes.Add(vm);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// One <see cref="S98DisplayPlane"/> group row in the Layer Stack
/// panel — its title, contributing entries, expansion state, and
/// child count.
/// </summary>
internal sealed class LayerStackPlaneViewModel : ViewModelBase
{
    public S98DisplayPlane Plane { get; }
    public IReadOnlyList<LayerStackEntryViewModel> Children { get; }

    public string Title => Plane switch
    {
        S98DisplayPlane.BaseChartUnder => Strings.LayerStack_Plane_BaseChartUnder,
        S98DisplayPlane.Bathymetry => Strings.LayerStack_Plane_Bathymetry,
        S98DisplayPlane.OnDemandSurface => Strings.LayerStack_Plane_OnDemandSurface,
        S98DisplayPlane.BaseChartOver => Strings.LayerStack_Plane_BaseChartOver,
        S98DisplayPlane.OtherChartOverlays => Strings.LayerStack_Plane_OtherChartOverlays,
        S98DisplayPlane.CautionsAndWarnings => Strings.LayerStack_Plane_CautionsAndWarnings,
        S98DisplayPlane.DynamicArrows => Strings.LayerStack_Plane_DynamicArrows,
        S98DisplayPlane.MarinerOverlay => Strings.LayerStack_Plane_MarinerOverlay,
        S98DisplayPlane.EcdisAlerts => Strings.LayerStack_Plane_EcdisAlerts,
        _ => Plane.ToString(),
    };

    public int ChildCount => Children.Count;

    public string ChildCountText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Strings.LayerStack_ChildCountFormat, ChildCount);

    public bool IsEmpty => Children.Count == 0;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public LayerStackPlaneViewModel(
        S98DisplayPlane plane,
        IReadOnlyList<LayerStackEntryViewModel> children,
        bool isExpanded)
    {
        Plane = plane;
        Children = children;
        _isExpanded = isExpanded;
    }
}

/// <summary>
/// One <see cref="LayerStackEntry"/> child row — its source
/// dataset id, optional source feature type, within-plane priority,
/// and a two-way bound <see cref="IsActive"/> checkbox routed
/// through <see cref="IDatasetLoaderService.SetActive"/>.
/// </summary>
internal sealed class LayerStackEntryViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;
    public LayerStackEntry Entry { get; }

    public string DatasetId => Entry.SourceDatasetId;

    public string DatasetLabel
    {
        get
        {
            // Show just the file name for typical file paths to keep
            // the tree narrow; tooltip carries the full id.
            try { return System.IO.Path.GetFileName(Entry.SourceDatasetId) is { Length: > 0 } n ? n : Entry.SourceDatasetId; }
            catch { return Entry.SourceDatasetId; }
        }
    }

    public string? SourceFeatureType => Entry.SourceFeatureType;

    public bool HasSourceFeatureType => !string.IsNullOrEmpty(Entry.SourceFeatureType);

    public int WithinPlanePriority => Entry.WithinPlanePriority;

    public string PriorityText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Strings.LayerStack_PriorityFormat, Entry.WithinPlanePriority);

    public bool IsActive
    {
        get => _loader.GetActive(Entry.SourceDatasetId);
        set
        {
            if (_loader.GetActive(Entry.SourceDatasetId) == value) return;
            _loader.SetActive(Entry.SourceDatasetId, value);
            OnPropertyChanged();
        }
    }

    public LayerStackEntryViewModel(IDatasetLoaderService loader, LayerStackEntry entry)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(entry);
        _loader = loader;
        Entry = entry;
    }
}
