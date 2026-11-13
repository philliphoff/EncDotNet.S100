using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.Services.Tides;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The cached NOAA station list (tide predictions design D15).</summary>
[Description("The cached NOAA tide and current station list that predictions come from.")]
internal sealed record TideStationListDto(
    [property: Description("True when a station list is cached and read.")] bool Present,
    [property: Description("Who provides it, such as 'NOAA CO-OPS'; null when there's none.")] string? Provider,
    [property: Description("When it was downloaded (UTC); null when not known.")] DateTimeOffset? Date,
    [property: Description("The number of tide stations.")] int TideStations,
    [property: Description("The number of tidal current stations (per depth bin).")] int CurrentStations,
    [property: Description("True when NOAA has published newer data than the list holds, or, when no check could run, the list is over 12 months old (D21).")] bool Stale,
    [property: Description("What it's doing: idle, loading, downloading or checking.")] string Activity,
    [property: Description("While downloading, the stations read so far; null otherwise.")] int? DownloadedStations,
    [property: Description("While downloading, the stations to read; null otherwise.")] int? TotalStations,
    [property: Description("Why the last download failed; null when it didn't.")] string? LastError);

/// <summary>The tide prediction display state (#1453, design D17).</summary>
[Description("Tide predictions in SoundCharts: the 'Tide stations · predicted' overlay, the 'Use tide predictions where there's no S-104' water-level source, and the station list.")]
internal sealed record TideDisplayDto(
    [property: Description("The 'Tide stations · predicted' Layer stack row under Other chart overlays: on or off. Off by default, kept after a restart.")] bool TideStations,
    [property: Description("'Use tide predictions where there's no S-104' (Mariner Settings): predictions feed the Depth card, the route check and chart adjustment. Off by default.")] bool UsePredictions,
    [property: Description("Whether predictions feed the water level: off, loading, in_use, or no_station_list (on, but there's no list to predict from).")] string PredictionState,
    [property: Description("Predicted tide stations in view at a drawable scale (1:500,000 and larger), before thinning; 0 while the overlay is off.")] int StationsInView,
    [property: Description("Stations drawn: after 40 px thinning and leaving out stations S-104 covers at the view time.")] int StationsDrawn,
    [property: Description("The cached station list.")] TideStationListDto StationList,
    [property: Description("The 'Current stations · predicted' Layer stack row (#1454, D16): on or off. Off by default, kept after a restart. Display only: currents aren't used in route checks.")] bool CurrentStations = false,
    [property: Description("Current stations in view at 1:500,000 and larger, before thinning; 0 while that overlay is off.")] int CurrentStationsInView = 0,
    [property: Description("Current stations drawn: after 40 px thinning and leaving out stations S-111 covers at the view time.")] int CurrentStationsDrawn = 0);

/// <summary>Reads and sets the tide prediction display state, as the Layer stack row and the setting do.</summary>
internal sealed class TideDisplayTools
{
    /// <summary>The read tool's name.</summary>
    public const string GetName = "get_tide_display";

    /// <summary>The set tool's name.</summary>
    public const string SetName = "set_tide_display";

    private readonly TideStationsOverlay _overlay;
    private readonly Services.ViewerSettingsService _settings;
    private readonly TidePredictionSourceService _predictions;
    private readonly TideStationListService _list;
    private readonly IRouteEditInvoker _invoker;

    public TideDisplayTools(
        TideStationsOverlay overlay,
        Services.ViewerSettingsService settings,
        TidePredictionSourceService predictions,
        TideStationListService list,
        IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(invoker);
        _overlay = overlay;
        _settings = settings;
        _predictions = predictions;
        _list = list;
        _invoker = invoker;
    }

    /// <summary>Returns the state.</summary>
    public Task<ToolResult<TideDisplayDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<TideDisplayDto>.Ok(Dto()));
    }

    /// <summary>Sets the given switches (a null one is left as it is), and starts a download when asked.</summary>
    public Task<ToolResult<TideDisplayDto>> SetAsync(
        bool? tideStations = null,
        bool? usePredictions = null,
        bool? refreshStationList = null,
        bool? currentStations = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() =>
        {
            if (tideStations is { } stations)
            {
                _overlay.SetVisible(stations);
            }

            if (currentStations is { } currents)
            {
                _overlay.SetCurrentsVisible(currents);
            }

            if (usePredictions is { } use)
            {
                _settings.UseTidePredictions = use;
            }

            if (refreshStationList is true)
            {
                _ = _list.DownloadAsync();
            }

            return ToolResult<TideDisplayDto>.Ok(Dto());
        });
    }

    internal TideDisplayDto Dto()
    {
        var catalogue = _list.Catalogue;
        var progress = _list.DownloadProgress;
        return new TideDisplayDto(
            _overlay.IsVisible,
            _settings.UseTidePredictions,
            _predictions.State switch
            {
                TidePredictionSourceState.Loading => "loading",
                TidePredictionSourceState.InUse => "in_use",
                TidePredictionSourceState.NoStationList => "no_station_list",
                _ => "off",
            },
            _overlay.Source.StationsInView.Count,
            _overlay.Source.CurrentFeatures.Count,
            new TideStationListDto(
                catalogue is not null,
                catalogue?.Provider,
                catalogue?.Retrieved,
                catalogue?.Stations.Count ?? 0,
                catalogue?.CurrentStations.Count ?? 0,
                _list.IsStale,
                _list.Activity.ToString().ToLowerInvariant(),
                progress?.CompletedStations,
                progress?.TotalStations,
                _list.LastError),
            _overlay.AreCurrentsVisible,
            _overlay.Currents.StationsInView.Count,
            _overlay.Currents.CurrentFeatures.Count);
    }
}

/// <summary>Wraps <see cref="TideDisplayTools"/> as <see cref="McpServerTool"/>s.</summary>
internal static class TideDisplayMcpAdapters
{
    public static McpServerTool CreateGet(TideDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.GetAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = TideDisplayTools.GetName,
            Description =
                "Returns the tide prediction display state: whether the 'Tide stations · predicted' and 'Current stations · predicted' overlays are on, whether " +
                "'Use tide predictions where there's no S-104' is on and in use, how many stations are in view and drawn, " +
                "and the cached NOAA station list (date, counts, stale, download progress). Change it with set_tide_display.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool CreateSet(TideDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The 'Tide stations · predicted' overlay on or off; omit to leave it.")] bool? tideStations = null,
            [Description("'Use tide predictions where there's no S-104' on or off; omit to leave it.")] bool? usePredictions = null,
            [Description("True to download NOAA's station list again (about 10 minutes, in the background); watch stationList.activity.")] bool? refreshStationList = null,
            [Description("The 'Current stations · predicted' overlay on or off; omit to leave it.")] bool? currentStations = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.SetAsync(tideStations, usePredictions, refreshStationList, currentStations, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = TideDisplayTools.SetName,
            Description =
                "Sets the tide prediction display state, as the Layer stack row and the Mariner Settings switch do; an omitted " +
                "value is left as it is, and both are kept after a restart. Turning either on with no station list cached " +
                "starts NOAA's download. Returns what get_tide_display returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
