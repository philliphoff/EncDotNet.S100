using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Fills Object information's "Shown in" chips and "Other maps" rows while
/// the map is split (split map design note D11, #1733). The report itself is
/// at the picked map's time. The picked map is the one the pick names
/// (<see cref="PickReportViewModel.PickedMap"/>, #1763), which need not be
/// active: an MCP <c>pick_features</c> with <c>select</c> and a <c>map</c>
/// doesn't activate it. A pick that names no map is reported at the map
/// active when it was made.
/// </summary>
internal sealed class PickMapPresencePresenter : IDisposable
{
    private readonly PickReportViewModel _report;
    private readonly MapSplitServices _split;
    private readonly ITimeFormatProvider _timeFormat;
    private readonly Action<string> _selectDataset;
    private readonly Action<Action> _dispatch;
    private IMapViewCapabilities? _pickedView;
    private bool _disposed;

    /// <param name="report">Object information.</param>
    /// <param name="split">The maps and panes.</param>
    /// <param name="timeFormat">The Local/UTC setting, for the other maps' times.</param>
    /// <param name="selectDataset">Selects a dataset row in Datasets, by its session id (a hidden chip's click).</param>
    /// <param name="dispatch">
    /// Runs an update on the UI thread. A map's session can change what it
    /// draws on any thread (<see cref="PickPresence.Changed"/>).
    /// </param>
    public PickMapPresencePresenter(
        PickReportViewModel report,
        MapSplitServices split,
        ITimeFormatProvider timeFormat,
        Action<string> selectDataset,
        Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(timeFormat);
        ArgumentNullException.ThrowIfNull(selectDataset);
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
        _report = report;
        _split = split;
        _timeFormat = timeFormat;
        _selectDataset = selectDataset;

        _report.PropertyChanged += OnReportChanged;
        _split.Split.LayoutChanged += OnChanged;
        _timeFormat.TimeFormatChanged += OnTimeFormatChanged;
        // A dataset shown or hidden in a pane changes the chips without a new pick.
        _split.Picks.Changed += OnPresenceChanged;
        Update();
    }

    private void OnReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PickReportViewModel.Location) or nameof(PickReportViewModel.PickedMap):
                // A new pick: the map it names, else the active map.
                _pickedView = PickedView();
                Update();
                break;
            case nameof(PickReportViewModel.SelectedHit) or nameof(PickReportViewModel.HasPick):
                Update();
                break;
        }
    }

    private IMapViewCapabilities? PickedView() =>
        _report.PickedMap is { } map && _split.Views.Find(map.Value) is { } view
            ? view
            : _split.Views.Active;

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void OnTimeFormatChanged(TimeFormat format) => Update();

    private void OnPresenceChanged(object? sender, EventArgs e) => _dispatch(Update);

    /// <summary>Recomputes the chips and rows from the current pick and panes.</summary>
    public void Update()
    {
        if (_disposed)
            return;
        var panes = _split.Split.Panes;
        if (!_split.Split.IsSplit || !_report.HasPick || _report.SelectedHit is not { } hit)
        {
            _report.SetMapPresence([], []);
            return;
        }

        var chips = new List<PickShownInChip>(panes.Count);
        foreach (var pane in panes)
        {
            var shown = PickPresence.Draws(pane.View, hit);
            var format = shown ? Strings.SplitMap_ShownInFormat : Strings.SplitMap_HiddenInFormat;
            var command = !shown && hit.DatasetId is { Length: > 0 } datasetId
                ? new RelayCommand(() => _selectDataset(datasetId))
                : null;
            chips.Add(new PickShownInChip(
                string.Format(CultureInfo.CurrentCulture, format, pane.Label),
                shown,
                $"PickReport.ShownIn.{pane.Id.Value}",
                command));
        }

        // Other maps at a time other than the picked map's (step 3). In
        // step 1 every map has the same time, so there are none.
        var picked = _pickedView is { } view && panes.Any(p => ReferenceEquals(p.View, view))
            ? view
            : _split.Views.Active;
        var pickedTime = picked?.DatasetSession.GetTimeSnapshot().Current;
        var rows = new List<PickOtherMapRow>();
        foreach (var pane in panes)
        {
            if (ReferenceEquals(pane.View, picked)
                || pane.View.DatasetSession.GetTimeSnapshot().Current is not { } time
                || time == pickedTime)
            {
                continue;
            }
            rows.Add(new PickOtherMapRow(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.SplitMap_OtherMapAtFormat,
                    pane.Label,
                    TimeFormatting.FormatClock(time, _timeFormat.Current)),
                $"PickReport.OtherMaps.{pane.Id.Value}"));
        }

        _report.SetMapPresence(chips, rows);
    }

    /// <summary>Unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _report.PropertyChanged -= OnReportChanged;
        _split.Split.LayoutChanged -= OnChanged;
        _timeFormat.TimeFormatChanged -= OnTimeFormatChanged;
        _split.Picks.Changed -= OnPresenceChanged;
    }
}
