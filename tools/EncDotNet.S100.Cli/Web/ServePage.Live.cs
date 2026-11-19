using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Web.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// The page's server state and live changes (issue #1583, handoff D28–D31):
/// the pill, the toasts and row tags a change brings, the outlines and tiles
/// it refreshes, and the clean-shutdown signal. The lost-connection states are
/// the browser's (js/serve-live.js), as the circuit is down while they last.
/// </summary>
public partial class ServePage
{
    private ServerState _serverState;
    // Read as the page first renders (prerendered too), then kept as changes arrive.
    private bool _live;
    private string? _revision;
    private int _datasetsVersion;
    private int _notServedCount;
    private DatasetsUpdate? _reloading;
    private readonly Dictionary<string, DateTime> _newUntil = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _rowTags = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<LiveToast> _toasts = [];
    private int _toastKeys;
    private IDisposable? _updates;
    private IDisposable? _stopping;
    private bool _disposed;

    /// <summary>The rail's items, the Datasets icon counting the datasets not served (D31).</summary>
    private IReadOnlyList<RailItem> RailItems =>
        NotServedCount == 0 ? railItems : [.. railItems.Select(i => i.Panel == ServePanel.Datasets ? i with { Badge = NotServedCount } : i)];

    private int NotServedCount => _live ? _notServedCount : Data.NotServed.Count;

    private string? Revision => _live ? _revision : Data.Revision;

    private string? StateDetail => ServeLive.StateDetail(Revision, Data.RefreshInterval);

    private string? DatasetsFooter => ServeLive.Footer(Data.HasDatasets, Revision, Data.RefreshInterval);

    /// <summary>The Datasets panel's footer (H2): how often files are checked, and the revision.</summary>
    private RenderFragment? DatasetsFooterContent => DatasetsFooter is { } text
        ? builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "panel-foot-text");
            builder.OpenComponent<Icon>(2);
            builder.AddComponentParameter(3, nameof(Icon.Name), "refresh");
            builder.AddComponentParameter(4, nameof(Icon.Size), 14);
            builder.CloseComponent();
            builder.AddContent(5, text);
            builder.CloseElement();
        }
    : null;

    /// <summary>Reads the server's state and starts hearing of changes and of the server stopping. Once shown.</summary>
    private async Task StartLiveAsync()
    {
        _revision = Data.Revision;
        _notServedCount = Data.NotServed.Count;
        _serverState = ServeLive.State(Data.IsUpdating);
        _live = true;
        _updates = Data.OnDatasetsUpdated(update => _ = InvokeAsync(() => ApplyUpdateAsync(update)));
        _stopping = Data.OnStopping(() => InvokeAsync(TellStoppingAsync));
        await JS.InvokeVoidAsync("s100Live.start");
    }

    private void StopLive()
    {
        _disposed = true;
        _updates?.Dispose();
        _stopping?.Dispose();
    }

    // The server is stopping cleanly: the browser shows Server stopped once the link drops (D28).
    private async Task TellStoppingAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("s100Live.stopping");
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            // The tab is gone already.
        }
    }

    /// <summary>A change to what's served (D29): the pill, tags and toasts, and the map when the revision changed.</summary>
    private async Task ApplyUpdateAsync(DatasetsUpdate update)
    {
        if (_disposed)
            return;
        _serverState = ServeLive.State(update.InProgress);
        _reloading = update.InProgress ? update : null;
        if (!update.InProgress)
        {
            var now = DateTime.UtcNow;
            var added = update.Changes.Where(c => c is { Kind: DatasetChangeKind.Added, Id: not null }).Select(c => c.Id!).ToList();
            foreach (var id in added)
                _newUntil[id] = now + ServeLive.NewFor;
            if (update.Changes.Count > 0 || update.Revision != _revision)
                await ServedChangedAsync();
            foreach (var toast in ServeLive.Toasts(update, HasPlace))
                ShowToast(toast);
            foreach (var id in added.Where(HasPlace))
                await MapAsync("s100Map.flashDataset", id);
            if (added.Count > 0)
                _ = ExpireNewAsync();
        }

        _rowTags = ServeLive.Tags(_reloading, _newUntil, DateTime.UtcNow);
        StateHasChanged();
    }

    private bool HasPlace(string id) => servedDatasets.FirstOrDefault(d => d.Id == id) is { } dataset && ServeDatasetInfo.HasPlace(dataset);

    /// <summary>What's served changed: read it again, redraw the outlines and refetch the tiles.</summary>
    private async Task ServedChangedAsync()
    {
        _revision = Data.Revision;
        servedDatasets = Data.Datasets;
        _notServedCount = Data.NotServed.Count;
        tileSet = Data.TileSet;
        summary = ServeSummary.Describe(Data);
        _datasetsVersion++;
        if (selectedDataset is not null && servedDatasets.All(d => d.Id != selectedDataset))
            await SelectDatasetAsync(null);
        await MapAsync("s100Live.setTitle", ServeSummary.Title(Data));
        await MapAsync("s100Map.setOutlines", ServeOutlines.FeatureCollection(servedDatasets).ToJsonString());
        await MapAsync("s100Map.setChart", TileJsonUrl());
        await SetDatasetOutlinesAsync();
    }

    private void ShowToast(LiveToast toast)
    {
        var shown = toast with { Key = ++_toastKeys };
        _toasts.Add(shown);
        // At most four at once: the oldest go first.
        while (_toasts.Count > 4)
            _toasts.RemoveAt(0);
        _ = ExpireToastAsync(shown, toast.Tone == "error" ? ServeLive.ToastFor * 2 : ServeLive.ToastFor);
    }

    private async Task ExpireToastAsync(LiveToast toast, TimeSpan after)
    {
        await Task.Delay(after);
        await InvokeAsync(() =>
        {
            if (!_disposed && _toasts.Remove(toast))
                StateHasChanged();
        });
    }

    private async Task ExpireNewAsync()
    {
        await Task.Delay(ServeLive.NewFor + TimeSpan.FromSeconds(0.5));
        await InvokeAsync(() =>
        {
            if (_disposed)
                return;
            _rowTags = ServeLive.Tags(_reloading, _newUntil, DateTime.UtcNow);
            StateHasChanged();
        });
    }

    private void DismissToast(LiveToast toast) => _toasts.Remove(toast);

    private async Task ToastActionAsync(LiveToast toast)
    {
        _toasts.Remove(toast);
        if (toast.DatasetId is { } id)
            await ZoomToDatasetAsync(id);
    }
}
