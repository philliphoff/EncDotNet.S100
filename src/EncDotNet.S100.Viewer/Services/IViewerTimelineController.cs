namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Reads and drives the viewer's Timeline for agents (MCP
/// <c>get_timeline_state</c> / <c>set_view_time</c>, #715). Goes through the
/// same <see cref="GlobalTimeService"/> and Timeline commands the panel uses,
/// marshalled to the UI thread.
/// </summary>
internal interface IViewerTimelineController
{
    /// <summary>Snapshots the Timeline and the time each time-aware layer draws.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The current Timeline state.</returns>
    Task<ViewerTimelineState> GetStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Moves the view time to now (as the Timeline's Go live does, which
    /// then follows now) or to <paramref name="time"/>, which pins it.
    /// </summary>
    /// <param name="time">The time to show; <see langword="null"/> for now.</param>
    /// <param name="snapToNearestSample">
    /// When true, moves to the loaded sample nearest <paramref name="time"/>
    /// rather than to the exact time.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Whether the time was applied, why not, and the resulting state.</returns>
    Task<ViewTimeOutcome> SetViewTimeAsync(DateTime? time, bool snapToNearestSample, CancellationToken ct = default);

    /// <summary>
    /// Steps <paramref name="count"/> times by <paramref name="kind"/> (the
    /// Timeline's current step when null) in <paramref name="direction"/>, as
    /// ‹ › and the arrow keys do; it pins the time.
    /// </summary>
    Task<ViewTimeOutcome> StepAsync(TimelineStepKind? kind, int direction, int count, CancellationToken ct = default);

    /// <summary>Changes what the axis shows: a preset, a zoom step, or a window.</summary>
    Task<ViewTimeOutcome> SetViewAsync(TimelineViewChange change, CancellationToken ct = default);
}

/// <summary>A change to the Timeline's window (#708 C5): exactly one of the members is set.</summary>
/// <param name="Preset">A preset to apply.</param>
/// <param name="Zoom">+1 zooms in (half the span), −1 out (double), around the view time.</param>
/// <param name="Window">A window to show.</param>
internal sealed record TimelineViewChange(
    EncDotNet.S100.Viewer.ViewModels.TimelinePreset? Preset,
    int? Zoom,
    (DateTime Start, DateTime End)? Window);

/// <summary>The Timeline as the user sees it, plus the time each layer draws.</summary>
/// <param name="Active">True when at least one time-aware dataset is loaded.</param>
/// <param name="Now">The viewer's current time (UTC); moved by the test clock when enabled.</param>
/// <param name="ViewTime">The time the map shows (UTC), or null when inactive.</param>
/// <param name="FollowingNow">True while the view time follows now (Live).</param>
/// <param name="Minimum">The earliest loaded sample (UTC).</param>
/// <param name="Maximum">The latest loaded sample (UTC).</param>
/// <param name="SampleCount">The number of distinct loaded samples.</param>
/// <param name="Coverage">The merged windows in which some layer draws.</param>
/// <param name="Runs">The forecast runs loaded, e.g. "cbofs 12:00Z".</param>
/// <param name="NowInCoverage">True when now lies inside a loaded window, so Now can be applied.</param>
/// <param name="ForecastEnded">True when every loaded forecast has ended.</param>
/// <param name="Readout">The Timeline's time readout as displayed.</param>
/// <param name="Summary">The Timeline's range summary as displayed.</param>
/// <param name="Layers">The time-aware layers, in Datasets-list order.</param>
internal sealed record ViewerTimelineState(
    bool Active,
    DateTime Now,
    DateTime? ViewTime,
    bool FollowingNow,
    DateTime? Minimum,
    DateTime? Maximum,
    int SampleCount,
    IReadOnlyList<CoverageSegment> Coverage,
    IReadOnlyList<string> Runs,
    bool NowInCoverage,
    bool ForecastEnded,
    string Readout,
    string Summary,
    IReadOnlyList<TimelineLayerState> Layers)
{
    /// <summary>The view time's offset from now as displayed: "now", "in 11 h 30", "5 h ago".</summary>
    public string Offset { get; init; } = string.Empty;

    /// <summary>The status line's message, or null when all is well.</summary>
    public string? Message { get; init; }

    /// <summary>The message's action as displayed ("Check for new runs", "Next data ›", …), or null.</summary>
    public string? MessageAction { get; init; }

    /// <summary>The first time on the axis.</summary>
    public DateTime? WindowStart { get; init; }

    /// <summary>The last time on the axis.</summary>
    public DateTime? WindowEnd { get; init; }

    /// <summary>The window's preset as displayed ("All loaded", "Now ± 6 h", "Custom").</summary>
    public string Preset { get; init; } = string.Empty;

    /// <summary>What ‹ › step by.</summary>
    public TimelineStepKind Step { get; init; }

    /// <summary>The layer "Sample of" follows (and whose ticks show), or null.</summary>
    public string? StepDriver { get; init; }

    /// <summary>The collapsed gaps on the axis.</summary>
    public IReadOnlyList<AxisGap> Gaps { get; init; } = [];
}

/// <summary>What one time-aware layer draws at the view time.</summary>
/// <param name="Id">The dataset id, as <c>list_datasets</c> reports it.</param>
/// <param name="Spec">The product specification, e.g. "S-111".</param>
/// <param name="Visible">True when the user has the layer switched on.</param>
/// <param name="DrawnTime">The sample the layer draws, or null when it has no data at the view time and hides.</param>
/// <param name="PreviousSample">The latest sample at or before the view time, or null.</param>
/// <param name="NextSample">The earliest sample after the view time, or null.</param>
/// <param name="SampleCount">The number of samples in the layer.</param>
internal sealed record TimelineLayerState(
    string Id,
    string Spec,
    bool Visible,
    DateTime? DrawnTime,
    DateTime? PreviousSample,
    DateTime? NextSample,
    int SampleCount);

/// <summary>The outcome of <see cref="IViewerTimelineController.SetViewTimeAsync"/>.</summary>
/// <param name="Applied">True when the view time was changed or already matched.</param>
/// <param name="Reason">Why it was not applied, or null.</param>
/// <param name="State">The Timeline state afterwards.</param>
internal sealed record ViewTimeOutcome(bool Applied, string? Reason, ViewerTimelineState State);
