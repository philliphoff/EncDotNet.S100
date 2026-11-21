using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Predicted tide and current stations as live feeds (data source design note
/// D8, slice 7d), in place of their Layer stack toggles: each is a Datasets
/// entry subscribed to in the Library's Live feeds. SoundCharts builds them,
/// since they follow its view time, viewport and loaded S-104 and S-111
/// (<see cref="TideStationsOverlay"/>).
/// </summary>
/// <remarks>
/// The processors don't own the sources: the timeline's tide lane, the legend
/// and the MCP tools read the same ones. <see cref="TideFeedHostExtension"/>
/// turns a source off when its entry is removed.
/// </remarks>
internal static class TideFeeds
{
    /// <summary>The tide stations' registration id.</summary>
    public const string TideStationsId = "tides.stations.predicted";

    /// <summary>The current stations' registration id.</summary>
    public const string CurrentStationsId = "tides.currents.predicted";

    /// <summary>The tide stations' registration.</summary>
    /// <param name="overlay">The sources and their bindings, resolved when the feed is first created.</param>
    public static SourceRegistration TideStations(Func<TideStationsOverlay> overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return new SourceRegistration
        {
            Id = TideStationsId,
            DisplayName = Strings.Tides_StationsLayer,
            Description = Strings.Tides_StationsLayer_Description,
            Spec = SpecName.TidePredictions,
            Group = SourceGroup.Predictions,
            Create = _ => new DynamicSourceProcessor(
                overlay().Source,
                TideStationPortrayal.Instance,
                new SpecRef(SpecName.TidePredictions, default),
                ownsSource: false),
        };
    }

    /// <summary>The current stations' registration.</summary>
    /// <param name="overlay">The sources and their bindings, resolved when the feed is first created.</param>
    public static SourceRegistration CurrentStations(Func<TideStationsOverlay> overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return new SourceRegistration
        {
            Id = CurrentStationsId,
            DisplayName = Strings.Tides_CurrentsLayer,
            Description = Strings.Tides_CurrentsLayer_Description,
            Spec = SpecName.TidePredictions,
            Group = SourceGroup.Predictions,
            Create = _ => new DynamicSourceProcessor(
                overlay().Currents,
                CurrentStationPortrayal.Instance,
                new SpecRef(SpecName.TidePredictions, default),
                ownsSource: false),
        };
    }
}

/// <summary>
/// Follows a predicted tide or current station feed's Datasets entry: turns
/// its source off when the entry goes, since its processor doesn't own it
/// (<see cref="TideFeeds"/>), and has the row say what it's waiting for, such
/// as the station list download or a closer zoom (#1630).
/// </summary>
/// <param name="sourceId">The registration id.</param>
/// <param name="source">
/// The source, resolved when an entry loads: the overlay depends on the
/// loaded datasets, which depend on the source host, so it can't be built
/// with the host.
/// </param>
/// <param name="list">The station list the source predicts from.</param>
internal sealed class TideFeedHostExtension(
    string sourceId,
    Func<IPredictedStationSource> source,
    Func<TideStationListService> list) : ISourceHostExtension
{
    /// <inheritdoc />
    public string SourceId => sourceId;

    /// <inheritdoc />
    public IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor) =>
        new Attachment(source(), list());

    /// <summary>
    /// The row's status for a station feed, or <see langword="null"/> when
    /// it's drawing and the generic status applies.
    /// </summary>
    /// <param name="activity">What the station list is doing.</param>
    /// <param name="progress">The download's progress, or <see langword="null"/>.</param>
    /// <param name="hasList">Whether the list has been read.</param>
    /// <param name="lastError">Why the last download failed, or <see langword="null"/>.</param>
    /// <param name="viewTooWide">Whether the view is wider than the stations are drawn at.</param>
    internal static SourceStatus? StatusFor(
        TideStationListActivity activity,
        EncDotNet.S100.Tides.Stations.Noaa.NoaaDownloadProgress? progress,
        bool hasList,
        string? lastError,
        bool viewTooWide)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        if (!hasList)
        {
            return activity switch
            {
                TideStationListActivity.Downloading => new(
                    SourceStatusKind.Starting,
                    progress is { TotalStations: > 0 } p
                        ? string.Format(culture, Strings.Tides_FeedStatus_DownloadingListFormat, p.CompletedStations, p.TotalStations)
                        : Strings.Tides_FeedStatus_DownloadingList,
                    Strings.Tides_FeedStatus_DownloadingDetail),
                TideStationListActivity.Loading => new(SourceStatusKind.Starting, Strings.Tides_FeedStatus_ReadingList),
                _ when lastError is not null => new(
                    SourceStatusKind.Failed,
                    Strings.Tides_FeedStatus_DownloadFailed,
                    string.Format(culture, Strings.Tides_FeedStatus_DownloadFailedDetailFormat, lastError)),
                _ => new(SourceStatusKind.Starting, Strings.Tides_FeedStatus_WaitingForList),
            };
        }

        return viewTooWide
            ? new(
                SourceStatusKind.WaitingForZoom,
                string.Format(culture, Strings.Tides_FeedStatus_ZoomInFormat, TideStationPortrayal.StationMaxScaleDenominator))
            : null;
    }

    private sealed class Attachment : IDisposable, ISourceStatusOverride
    {
        private readonly IPredictedStationSource _source;
        private readonly TideStationListService _list;

        public Attachment(IPredictedStationSource source, TideStationListService list)
        {
            _source = source;
            _list = list;
            _source.Refreshed += OnChanged;
            _list.Changed += OnChanged;
        }

        public SourceStatus? Status =>
            StatusFor(_list.Activity, _list.DownloadProgress, _list.Catalogue is not null, _list.LastError, _source.IsViewTooWide);

        public event EventHandler? StatusChanged;

        public void Dispose()
        {
            _source.Refreshed -= OnChanged;
            _list.Changed -= OnChanged;
            _source.IsActive = false;
        }

        private void OnChanged(object? sender, EventArgs e) => StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
