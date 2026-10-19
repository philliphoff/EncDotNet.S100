using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Decides which S-124 warning parts the map draws (#993) and hides the rest
/// through <see cref="DatasetEntry.HiddenFeatures"/>, so the map, the
/// Navigational warnings panel, the Warning card, MCP and route findings
/// agree on one status (<see cref="NavigationalWarningSet"/>).
/// </summary>
/// <remarks>
/// <para>
/// S-98 Edition 2.0.0 §12.12: an ended or cancelled warning is never drawn
/// (§12.12.3); one not yet in force is drawn from its publication time only
/// when <see cref="ShowNotYetInForce"/> is on (§12.12.2 Example 1). See
/// <see cref="NavigationalWarningSet.IsDrawnAt"/>.
/// </para>
/// <para>
/// The warning on the open Warning card stays drawn whatever its status, so
/// Zoom to and the pick highlight have something to show, until the card
/// closes (<see cref="SetOpenWarning"/>).
/// </para>
/// <para>
/// The parts are judged again when the view time moves, the warnings change,
/// the setting changes, the open card changes, and once a minute so a live
/// view picks up a warning starting or ending. Only datasets whose hidden
/// parts change re-render.
/// </para>
/// </remarks>
internal sealed class NavigationalWarningDrawingService : INotifyPropertyChanged, IDisposable
{
    private readonly NavigationalWarningsService _warnings;
    private readonly IViewTime _viewTime;
    private readonly ViewerSettings? _settings;
    private readonly Action<Action> _dispatch;
    private readonly ITimer? _clock;
    private (string DatasetId, string FeatureRef)? _open;
    private bool _showNotYetInForce;

    /// <param name="warnings">The loaded warnings and their set.</param>
    /// <param name="viewTime">The time the map shows.</param>
    /// <param name="settings">Where <see cref="ShowNotYetInForce"/> is kept; <see langword="null"/> keeps it in memory.</param>
    /// <param name="loader">The loader, asked to hide parts before a dataset's first render; <see langword="null"/> skips that.</param>
    /// <param name="time">The clock for the once-a-minute check; <see langword="null"/> for none.</param>
    /// <param name="dispatch">Runs the minute check on the UI thread; inline when <see langword="null"/>.</param>
    public NavigationalWarningDrawingService(
        NavigationalWarningsService warnings,
        IViewTime viewTime,
        ViewerSettings? settings = null,
        IDatasetLoaderService? loader = null,
        TimeProvider? time = null,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(viewTime);
        _warnings = warnings;
        _viewTime = viewTime;
        _settings = settings;
        _dispatch = dispatch ?? (action => action());
        _showNotYetInForce = settings?.ShowWarningsNotYetInForce ?? false;
        _warnings.Changed += (_, _) => Apply();
        _viewTime.Changed += (_, _) => Apply();
        loader?.SetHiddenFeaturesOnLoad(HiddenOnLoad);
        if (time is not null)
            _clock = time.CreateTimer(_ => _dispatch(Apply), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        Apply();
    }

    /// <summary>
    /// Whether warnings not yet in force are drawn too, from their
    /// publication time ("Show warnings not yet in force"; off by default,
    /// kept across restarts).
    /// </summary>
    public bool ShowNotYetInForce
    {
        get => _showNotYetInForce;
        set
        {
            if (_showNotYetInForce == value)
                return;
            _showNotYetInForce = value;
            if (_settings is not null)
            {
                _settings.ShowWarningsNotYetInForce = value;
                _settings.Save();
            }
            Apply(force: true);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNotYetInForce)));
            ShowNotYetInForceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after <see cref="ShowNotYetInForce"/> changes.</summary>
    public event EventHandler? ShowNotYetInForceChanged;

    /// <summary>Raised after the drawn parts or <see cref="ShowNotYetInForce"/> change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Keeps the part on the open Warning card drawn, or ends that with
    /// <see langword="null"/>s when the card closes.
    /// </summary>
    /// <param name="datasetId">The card's dataset id (<see cref="NavigationalWarningEntry.DatasetId"/>).</param>
    /// <param name="featureRef">The card's part.</param>
    public void SetOpenWarning(string? datasetId, string? featureRef)
    {
        (string, string)? open = datasetId is null || featureRef is null ? null : (datasetId, featureRef);
        if (_open == open)
            return;
        _open = open;
        Apply();
    }

    /// <summary>
    /// Follows <paramref name="pickReport"/>'s open Warning card
    /// (<see cref="PickReportViewModel.WarningCard"/>), which stays drawn
    /// while it is open.
    /// </summary>
    /// <param name="pickReport">The pick report that shows the cards.</param>
    public void Follow(PickReportViewModel pickReport)
    {
        ArgumentNullException.ThrowIfNull(pickReport);
        void Update() => SetOpenWarning(pickReport.WarningCard?.Hit?.DatasetFileName, pickReport.WarningCard?.Hit?.FeatureRef);
        pickReport.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PickReportViewModel.WarningCard) or null)
                Update();
        };
        Update();
    }

    /// <summary>The time the map draws warnings at: the view time.</summary>
    public DateTimeOffset DrawTime => _viewTime.Current;

    /// <summary>Whether the map draws <paramref name="entry"/> at <see cref="DrawTime"/>.</summary>
    public bool IsDrawn(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return IsDrawnAt(entry, _viewTime.Current);
    }

    /// <summary>
    /// Whether <paramref name="entry"/> is drawn because its card is open,
    /// though it wouldn't be otherwise.
    /// </summary>
    public bool IsDrawnForCard(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return IsOpen(entry) && !_warnings.Set.IsDrawnAt(entry.Warning, _viewTime.Current, _showNotYetInForce);
    }

    /// <summary>Judges every part again and hides those not drawn.</summary>
    public void Apply() => Apply(force: false);

    private void Apply(bool force)
    {
        var time = _viewTime.Current;
        var changed = force;
        foreach (var group in _warnings.Warnings.GroupBy(w => w.Dataset))
        {
            var hidden = group.Where(w => !IsDrawnAt(w, time)).Select(w => w.FeatureRef).ToHashSet(StringComparer.Ordinal);
            var before = group.Key.HiddenFeatures;
            changed |= !(before is null ? hidden.Count == 0 : before.SetEquals(hidden));
            group.Key.HiddenFeatures = hidden;
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsDrawnAt(NavigationalWarningEntry entry, DateTimeOffset time) =>
        IsOpen(entry) || _warnings.Set.IsDrawnAt(entry.Warning, time, _showNotYetInForce);

    private bool IsOpen(NavigationalWarningEntry entry) =>
        _open is { } open
        && string.Equals(open.DatasetId, entry.DatasetId, StringComparison.Ordinal)
        && string.Equals(open.FeatureRef, entry.FeatureRef, StringComparison.Ordinal);

    // Before a dataset's first render, so a warning not in force never
    // flashes up. Judged against the warnings already loaded; the refresh
    // after the load judges every dataset with this one included.
    private IReadOnlySet<string>? HiddenOnLoad(DatasetEntry entry, IDatasetProcessor processor)
    {
        if (!NavigationalWarningsService.IsWarningProduct(processor.Spec.Name))
            return null;
        IReadOnlyList<NavigationalWarningFeature> parts;
        try
        {
            parts = processor.GetNavigationalWarnings();
        }
        catch (Exception)
        {
            return null;
        }
        var time = _viewTime.Current;
        return parts
            .Where(p => !_warnings.Set.IsDrawnAt(p.Warning, time, _showNotYetInForce))
            .Select(p => p.FeatureRef)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose() => _clock?.Dispose();
}
