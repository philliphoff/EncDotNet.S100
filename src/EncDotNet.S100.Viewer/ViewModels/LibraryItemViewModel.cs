using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One dataset row in the Library panel: a <see cref="CollectionItem"/> plus
/// display formatting. Availability is resolved lazily (on first display)
/// because it touches the file system and lists can hold thousands of rows.
/// </summary>
internal sealed class LibraryItemViewModel : ViewModelBase
{
    private readonly Func<CollectionItem, LibraryLoadState>? _loadState;
    private readonly ILibraryDownloader? _downloader;
    private readonly string? _collectionName;
    private readonly Func<LibraryItemViewModel, Task>? _download;
    private LibraryDownloadItemStatus? _lastDownloadStatus;
    private LibraryAvailability? _availability;
    private CollectionItem? _effective;
    private readonly TimeProvider _time;
    private IReadOnlyList<LibraryItemViewModel> _members = [];

    public LibraryItemViewModel(
        CollectionItem item,
        LibrarySource source,
        Func<CollectionItem, LibraryLoadState>? loadState = null,
        ILibraryDownloader? downloader = null,
        string? collectionName = null,
        Func<LibraryItemViewModel, Task>? download = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        Item = item;
        Source = source;
        _loadState = loadState;
        _downloader = downloader;
        _collectionName = collectionName;
        _download = download;
        _time = time ?? TimeProvider.System;
        RetryCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(
            () => _download?.Invoke(this) ?? Task.CompletedTask);
        CancelDownloadCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => _downloader?.Cancel(Item));
    }

    /// <summary>Downloads the item again after a failure (the "Failed · retry" tag).</summary>
    public System.Windows.Input.ICommand RetryCommand { get; }

    /// <summary>Cancels the item's download (the row's Cancel link).</summary>
    public System.Windows.Input.ICommand CancelDownloadCommand { get; }

    /// <summary>Where the item stands in a download, if anywhere.</summary>
    private LibraryDownloadItemStatus? DownloadStatus => _downloader?.StatusOf(Item);

    /// <summary>True while the item is downloading (the row shows a progress bar and Cancel).</summary>
    public bool IsDownloading => DownloadStatus?.State == LibraryDownloadItemState.Running;

    /// <summary>The fraction downloaded (0–1).</summary>
    public double DownloadProgress => DownloadStatus?.Fraction ?? 0;

    /// <summary>"1,8 / 2,9 MB" while downloading.</summary>
    public string? DownloadProgressText => DownloadStatus is { State: LibraryDownloadItemState.Running } status
        ? status.TotalBytes is { } total
            ? string.Format(CultureInfo.CurrentCulture, Strings.Library_DownloadProgressFormat, FormatBytes(status.BytesReceived), FormatBytes(total))
            : FormatBytes(status.BytesReceived)
        : null;

    /// <summary>
    /// Re-reads the item's download status and raises what changed; cheap
    /// enough to call on every progress tick for the listed rows.
    /// </summary>
    public void RefreshDownload()
    {
        var status = DownloadStatus;
        if (Equals(status, _lastDownloadStatus))
            return;
        var stateChanged = status?.State != _lastDownloadStatus?.State;
        _lastDownloadStatus = status;
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(DownloadProgress));
        OnPropertyChanged(nameof(DownloadProgressText));
        if (stateChanged)
        {
            OnPropertyChanged(nameof(Tags));
            OnPropertyChanged(nameof(Details));
        }
    }

    /// <summary>
    /// The item as it can be opened now: an online item that has been
    /// downloaded, with its downloaded (local) location; otherwise
    /// <see cref="Item"/>.
    /// </summary>
    public CollectionItem EffectiveItem => _effective ??= _downloader?.Localize(Item) ?? Item;

    /// <summary>The indexed item.</summary>
    public CollectionItem Item { get; }

    /// <summary>The source the item was indexed from.</summary>
    public LibrarySource Source { get; }

    /// <summary>
    /// The dataset name (e.g. <c>US5AK1AM</c>). A community-list package
    /// (or its group, once unpacked) is named by its description instead.
    /// </summary>
    public string Name => IsModelHeader ? Item.Name
        : IsPackageEntry || IsGroupHeader
        ? Item.Title is { } title ? PackageTitles.Clean(title) : Item.Name
        : Item.Name;

    /// <summary>True when <see cref="Name"/> is a code, shown in monospace (not a package's description).</summary>
    public bool IsNameMono => IsModelHeader || (!IsPackageEntry && !IsGroupHeader);

    /// <summary>The descriptive title, when the source supplies one (for a package: what it holds).</summary>
    /// <remarks>A dataset under an unpacked package drops a title that only repeats the package's.</remarks>
    public string? Subtitle => IsPackageEntry ? Strings.Library_PackageHint
        : IsModelHeader ? Item.Title
        : IsForecastTile ? null
        : IsGroupHeader ? null
        : _isGroupChild && Item.Title is { } title && Item.Properties.TryGetValue("packageTitle", out var package) && title == package ? null
        : Item.Title;

    /// <summary>True when there is a <see cref="Subtitle"/> to show.</summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>
    /// True for a community-list entry not yet downloaded: a package that may
    /// hold several datasets, listed as one row.
    /// </summary>
    public bool IsPackageEntry =>
        Item.Location is RemoteItemLocation { Package: { } package, Layout: null } && Item.Key == package;

    /// <summary>True for the header row of an unpacked package's datasets.</summary>
    public bool IsGroupHeader { get; private init; }

    private bool _isGroupChild;
    private DateTimeOffset? _publishedAt;
    private int _groupCount;

    /// <summary>True for a dataset listed under an unpacked package's header (indented).</summary>
    public bool IsGroupChild
    {
        get => _isGroupChild;
        set
        {
            if (SetProperty(ref _isGroupChild, value))
            {
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    /// <summary>For a group header: how many datasets the package unpacked into.</summary>
    public int GroupCount
    {
        get => _groupCount;
        set
        {
            if (SetProperty(ref _groupCount, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>For a group header: the (source, package) it groups.</summary>
    internal (Guid Source, string Package) GroupKey { get; private init; }

    private bool _isExpanded;

    /// <summary>For a group header: whether its datasets are listed.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>For a group header: shows or hides its datasets.</summary>
    public System.Windows.Input.ICommand? ToggleCommand { get; private init; }

    /// <summary>
    /// Creates the header row for an unpacked package: named by the package's
    /// description, tagged "Unpacked", with its dataset count.
    /// </summary>
    internal static LibraryItemViewModel ForPackageGroup(
        LibrarySource source, string package, string? title, int count, bool isExpanded, Action<LibraryItemViewModel> toggle,
        DateTimeOffset? publishedAt = null)
    {
        var item = new CollectionItem
        {
            Key = "package:" + package,
            ProductSpec = "S-57",
            Name = package,
            Title = title ?? package,
            Location = NoItemLocation.Instance,
            Properties = new Dictionary<string, string> { ["package"] = package },
        };
        LibraryItemViewModel? header = null;
        header = new LibraryItemViewModel(item, source)
        {
            IsGroupHeader = true,
            GroupKey = (source.Id, package),
            ToggleCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => toggle(header!)),
        };
        header._groupCount = count;
        header._isExpanded = isExpanded;
        header._publishedAt = publishedAt;
        return header;
    }

    /// <summary>
    /// Creates the row of a forecast model (#685): named by its code, its
    /// water body below, its run and time left, expandable to its tiles (which
    /// carry no tags of their own — the run is updated as a whole).
    /// </summary>
    internal static LibraryItemViewModel ForModelGroup(
        LibrarySource source, IReadOnlyList<LibraryItemViewModel> members, bool isExpanded, Action<LibraryItemViewModel> toggle,
        Func<CollectionItem, LibraryLoadState>? loadState, ILibraryDownloader? downloader, TimeProvider? time)
    {
        var first = members[0].Item;
        var model = ForecastRuns.ModelOf(first) ?? first.Name;
        var item = first with
        {
            Key = "model:" + model,
            Name = model,
            Location = NoItemLocation.Instance,
            Coverage = null,
            Bounds = GeoBounds.UnionAll(members.Select(m => m.Item.Bounds).OfType<GeoBounds>()),
        };
        LibraryItemViewModel? header = null;
        header = new LibraryItemViewModel(item, source, loadState, downloader, time: time)
        {
            IsGroupHeader = true,
            IsModelHeader = true,
            GroupKey = (source.Id, "model:" + model),
            ToggleCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => toggle(header!)),
        };
        header._members = members;
        header._groupCount = members.Count;
        header._isExpanded = isExpanded;
        return header;
    }

    /// <summary>For a model row, its tiles (in the latest run listed).</summary>
    internal IReadOnlyList<LibraryItemViewModel> Members
    {
        get => _members;
        set
        {
            _members = value;
            _groupCount = value.Count;
            _availability = null;
            RaiseForecast();
        }
    }

    /// <summary>True for a forecast model's row (tiles download shape), which groups its tiles.</summary>
    public bool IsModelHeader { get; private init; }

    /// <summary>True for a dataset of one forecast run (#685).</summary>
    public bool IsForecast => ForecastRuns.IsForecast(Item);

    /// <summary>
    /// True for a row that stands for a whole forecast run: a model's row, or a
    /// model's one file when runs download as one file per model.
    /// </summary>
    public bool IsForecastRunRow => IsModelHeader
        || (IsForecast && Source.Definition is S100ForecastFeedSource { Shape: ForecastShape.Regional });

    /// <summary>True for one tile of a forecast run (listed under its model's row).</summary>
    private bool IsForecastTile => IsForecast && !IsForecastRunRow;

    /// <summary>The run of this row's downloaded copy (a model's: of its first downloaded tile), if any.</summary>
    private DateTimeOffset? LocalRun => IsModelHeader
        ? _members.Select(m => m.LocalRun).FirstOrDefault(r => r is not null)
        : _downloader?.LocalPublishedAtOf(Item);

    /// <summary>The run shown: the downloaded copy's, else the catalogue's latest.</summary>
    private DateTimeOffset? ShownRun => LocalRun ?? S100ForecastFeedIndexer.RunOf(Item);

    /// <summary>The end of the shown run's valid window.</summary>
    private DateTimeOffset? ShownValidTo => ShownRun is { } run && ForecastRuns.Horizon(Item) is { } horizon ? run + horizon : null;

    /// <summary>True when the downloaded copy's run has ended (whatever is online).</summary>
    private bool IsRunEnded => LocalRun is not null && ShownValidTo is { } end && end <= _time.GetUtcNow();

    /// <summary>
    /// The time the item's data covers (#711): a forecast run's valid window
    /// (the downloaded copy's run, else the catalogue's), or a dataset's own
    /// time coverage; <see langword="null"/> for none.
    /// </summary>
    internal (DateTime Start, DateTime End)? ValidWindow =>
        IsForecast
            ? ShownRun is { } run && ShownValidTo is { } end ? (run.UtcDateTime, end.UtcDateTime) : null
            : TimeCoverage(Item);

    /// <summary>The run of the downloaded copy, if any (UTC).</summary>
    internal DateTime? LocalRunTime => LocalRun?.UtcDateTime;

    /// <summary>The catalogue's latest run and its valid window, for a forecast (UTC).</summary>
    internal (DateTime Run, DateTime End)? CatalogueRun =>
        S100ForecastFeedIndexer.RunOf(Item) is { } run && S100ForecastFeedIndexer.ValidToOf(Item) is { } end
            ? (run.UtcDateTime, end.UtcDateTime)
            : null;

    /// <summary>True when the item's downloaded copy is loaded on the map now.</summary>
    internal bool IsLoadedNow => _loadState?.Invoke(EffectiveItem) == LibraryLoadState.Loaded;

    /// <summary>A dataset's own time coverage, as a local index records it (<c>timeStart</c>/<c>timeEnd</c>).</summary>
    internal static (DateTime Start, DateTime End)? TimeCoverage(CollectionItem item) =>
        item.Properties.TryGetValue("timeStart", out var start) && item.Properties.TryGetValue("timeEnd", out var end)
        && DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s)
        && DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var e)
        && e >= s
            ? (s.UtcDateTime, e.UtcDateTime)
            : null;

    /// <summary>True when a forecast run row shows its valid window as a bar.</summary>
    public bool HasForecastWindow => IsForecastRunRow && ShownValidTo is not null;

    /// <summary>How much of the shown run's window has passed (0–1).</summary>
    public double WindowElapsed => ShownRun is { } start && ShownValidTo is { } end && end > start
        ? Math.Clamp((_time.GetUtcNow() - start) / (end - start), 0, 1)
        : 0;

    /// <summary>How much of the shown run's window is left (0–1).</summary>
    public double WindowRemaining => 1 - WindowElapsed;

    /// <summary>"39 h left", or "Ended 9 h ago".</summary>
    public string? TimeLeftText => ShownValidTo is { } end ? ForecastRuns.TimeLeft(end, _time.GetUtcNow()) : null;

    /// <summary>True when the shown run's window has ended.</summary>
    public bool IsWindowEnded => ShownValidTo is { } end && end <= _time.GetUtcNow();

    /// <summary>Re-evaluates what depends on the clock (time left, Expired); called every minute for forecasts.</summary>
    public void RefreshClock()
    {
        if (!IsForecast)
            return;
        if (_availability is not null)
        {
            _availability = null;
            _effective = null;
            OnPropertyChanged(nameof(Availability));
            OnPropertyChanged(nameof(PrimaryAvailability));
            OnPropertyChanged(nameof(Tags));
        }

        RaiseForecast();
    }

    private void RaiseForecast()
    {
        OnPropertyChanged(nameof(HasForecastWindow));
        OnPropertyChanged(nameof(WindowElapsed));
        OnPropertyChanged(nameof(WindowRemaining));
        OnPropertyChanged(nameof(TimeLeftText));
        OnPropertyChanged(nameof(IsWindowEnded));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(PrimaryAvailability));
    }

    /// <summary>"S-111 · run 30.09.2026 12:00Z · to 02.10.2026 12:00Z · 23 tiles · 12 MB" (a tile: its size).</summary>
    private string ForecastSummary()
    {
        var c = CultureInfo.CurrentCulture;
        var parts = new List<string>(5) { Item.ProductSpec };
        if (IsForecastRunRow)
        {
            if (ShownRun is { } run)
                parts.Add(string.Format(c, Strings.Library_Forecast_RunFormat, ForecastRuns.FormatRun(run)));
            if (ShownValidTo is { } end)
                parts.Add(string.Format(c, Strings.Library_Forecast_ToFormat, ForecastRuns.FormatRun(end)));
            if (IsModelHeader)
                parts.Add(string.Format(c, Strings.Library_Forecast_TilesFormat, _members.Count));
        }

        var bytes = IsModelHeader
            ? _members.Sum(m => (m.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0)
            : (Item.Location as RemoteItemLocation)?.SizeBytes ?? 0;
        if (bytes > 0)
            parts.Add(FormatBytes(bytes));
        return string.Join(" · ", parts);
    }

    /// <summary>The tags of a forecast run row: Loaded / On pan, then New run or Expired.</summary>
    private void AddForecastTags(List<LibraryItemTag> tags)
    {
        var states = IsModelHeader
            ? _members.Select(m => _loadState?.Invoke(m.EffectiveItem)).ToArray()
            : [_loadState?.Invoke(EffectiveItem)];
        if (states.Contains(LibraryLoadState.Loaded))
            tags.Add(new LibraryItemTag(Strings.Library_Availability_Loaded, LibraryItemTagKind.Loaded));
        else if (states.Contains(LibraryLoadState.Deferred))
            tags.Add(new LibraryItemTag(Strings.Library_Availability_Deferred, LibraryItemTagKind.OnPan));

        switch (Availability)
        {
            case LibraryAvailability.Outdated:
                tags.Add(new LibraryItemTag(Strings.Library_Tag_NewRun, LibraryItemTagKind.Update));
                break;
            case LibraryAvailability.Expired:
                tags.Add(new LibraryItemTag(Strings.Library_Tag_Expired, LibraryItemTagKind.Expired));
                break;
        }
    }

    /// <summary>A model row's state: the most pressing of its tiles'.</summary>
    private static LibraryAvailability Aggregate(IEnumerable<LibraryItemViewModel> members)
    {
        LibraryAvailability[] order =
        [
            LibraryAvailability.Outdated, LibraryAvailability.Expired, LibraryAvailability.Loaded, LibraryAvailability.Deferred,
            LibraryAvailability.Local, LibraryAvailability.Missing, LibraryAvailability.Online,
        ];
        var states = members.Select(m => m.Availability).ToHashSet();
        return order.FirstOrDefault(states.Contains, LibraryAvailability.Listed);
    }

    /// <summary>A compact one-line summary: spec, band, edition/update, issue date, and download size.</summary>
    public string Summary
    {
        get
        {
            if (IsForecast)
                return ForecastSummary();
            if (IsGroupHeader)
            {
                // "116 datasets · published 23.10.2025 15:17" — the package number is already in its name.
                return _publishedAt is { } published
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_PackageGroupFormat, GroupCount,
                        published.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                    : string.Format(CultureInfo.CurrentCulture, Strings.Library_PackageGroupNoDateFormat, GroupCount);
            }
            if (IsPackageEntry)
            {
                var published = Item.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return published is null
                    ? Item.Name
                    : string.Format(CultureInfo.CurrentCulture, Strings.Library_PackagePublishedFormat, Item.Name, published);
            }

            if (QuietUpdates)
                return RemoteCatalogueSummary();

            var parts = new List<string>(4) { Item.ProductSpec };
            if (Item.UsageBand is { } band)
                parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Library_BandFormat, band));
            if (Item.Edition is { } edition)
            {
                parts.Add(Item.Update is { } update
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_EditionUpdateFormat, edition, update)
                    : string.Format(CultureInfo.CurrentCulture, Strings.Library_EditionFormat, edition));
            }
            if (Item.IssueDate is { } issued)
                parts.Add(issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (Item.Location is RemoteItemLocation { SizeBytes: { } size })
                parts.Add(FormatBytes(size));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// True for a dataset from a remote S-100 catalogue (#685): a newer edition
    /// is shown by an amber swatch and "Ed 2 → Ed 3 online" rather than a tag,
    /// since nearly every tile is reissued each quarter; the source node and
    /// bulk bar carry the count and the action.
    /// </summary>
    public bool QuietUpdates => Source.Definition is S100CatalogueFeedSource or S100ForecastFeedSource && !IsGroupHeader;

    /// <summary>
    /// "S-102 · Port 4 m · Ed 3 · 2026-08-14 · 3,1 MB", or, with a newer
    /// edition online, "S-102 · Port 4 m · Ed 2 → Ed 3 online · 2,8 MB".
    /// </summary>
    private string RemoteCatalogueSummary()
    {
        var c = CultureInfo.CurrentCulture;
        var parts = new List<string>(5) { Item.ProductSpec };
        if (Library.NavigationPurposes.Of(Item) is { } purpose)
            parts.Add(purpose);
        if (Availability == LibraryAvailability.Outdated && Item.Edition is { } online)
        {
            parts.Add(_downloader?.LocalEditionOf(Item) is { } local
                ? string.Format(c, Strings.Library_EditionNewerOnlineFormat, local, online)
                : string.Format(c, Strings.Library_EditionOnlineFormat, online));
        }
        else
        {
            if (Item.Edition is { } edition)
                parts.Add(string.Format(c, Strings.Library_EditionFormat, edition));
            if (Item.IssueDate is { } issued)
                parts.Add(issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (Item.Location is RemoteItemLocation { SizeBytes: { } size })
            parts.Add(FormatBytes(size));
        return string.Join(" · ", parts);
    }

    /// <summary>Where the data can be had now (resolved on first access).</summary>
    public LibraryAvailability Availability => _availability ??=
        IsModelHeader ? Aggregate(_members)
        : IsGroupHeader ? LibraryAvailability.Local
        : IsForecast && _downloader?.IsOutdated(Item) == true ? LibraryAvailability.Outdated
        : IsForecast && IsRunEnded ? LibraryAvailability.Expired
        : (_loadState?.Invoke(EffectiveItem)) switch
        {
            LibraryLoadState.Loaded => LibraryAvailability.Loaded,
            LibraryLoadState.Deferred => LibraryAvailability.Deferred,
            _ when _downloader?.IsOutdated(Item) == true => LibraryAvailability.Outdated,
            _ => LibraryAvailabilityResolver.Resolve(EffectiveItem),
        };

    /// <summary>True when the item can be opened from disk (local, not already loaded).</summary>
    public bool CanLoad => !IsGroupHeader
        && Availability is LibraryAvailability.Local or LibraryAvailability.Deferred or LibraryAvailability.Outdated or LibraryAvailability.Expired;

    /// <summary>True when the item can be downloaded (online, or a newer edition is available).</summary>
    public bool CanDownload =>
        _downloader?.CanDownload(Item) == true
        && Availability is LibraryAvailability.Online or LibraryAvailability.Outdated;

    /// <summary>Re-resolves <see cref="Availability"/> after the item was opened or closed.</summary>
    public void RefreshAvailability()
    {
        if (_availability is null)
            return;  // never shown; resolved lazily on first display
        _availability = null;
        _effective = null;
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(PrimaryAvailability));
        OnPropertyChanged(nameof(PrimaryStateText));
        OnPropertyChanged(nameof(Tags));
        if (QuietUpdates)
            OnPropertyChanged(nameof(Summary));
        if (IsForecast)
            RaiseForecast();
        OnPropertyChanged(nameof(CanLoadAfterDownload));
        OnPropertyChanged(nameof(LoadTooltip));
        RefreshDownload();
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(Details));
    }

    /// <summary>
    /// Where the data is — exactly one state, drawn as the row swatch exactly
    /// like the map outline.
    /// </summary>
    public LibraryPrimaryAvailability PrimaryAvailability => (QuietUpdates || IsModelHeader) && Availability == LibraryAvailability.Outdated
        ? LibraryPrimaryAvailability.Update
        : LibraryOutlineStyles.Primary(Availability);

    /// <summary>
    /// What is happening to the dataset — zero or more sentence-case tags
    /// after its name (update available, loaded, on pan, …).
    /// </summary>
    public IReadOnlyList<LibraryItemTag> Tags
    {
        get
        {
            var tags = new List<LibraryItemTag>(2);
            switch (DownloadStatus?.State)
            {
                case LibraryDownloadItemState.Queued:
                    tags.Add(new LibraryItemTag(Strings.Library_Tag_Queued, LibraryItemTagKind.Queued));
                    break;
                case LibraryDownloadItemState.Failed:
                    tags.Add(new LibraryItemTag(Strings.Library_Tag_FailedRetry, LibraryItemTagKind.Failed, _download is null ? null : RetryCommand));
                    break;
            }

            if (IsForecastTile)
                return tags;  // a run's tiles are updated together; the model's row carries its state
            if (IsForecastRunRow)
            {
                AddForecastTags(tags);
                return tags;
            }

            if (IsPackageEntry)
                tags.Add(new LibraryItemTag(Strings.Library_Tag_Package, LibraryItemTagKind.Neutral));
            if (IsGroupHeader)
            {
                tags.Add(new LibraryItemTag(Strings.Library_Tag_Unpacked, LibraryItemTagKind.Neutral));
                return tags;
            }

            switch (Availability)
            {
                case LibraryAvailability.Outdated when !QuietUpdates:
                    tags.Add(new LibraryItemTag(UpdateText(), LibraryItemTagKind.Update));
                    break;
                case LibraryAvailability.Loaded:
                    tags.Add(new LibraryItemTag(Strings.Library_Availability_Loaded, LibraryItemTagKind.Loaded));
                    break;
                case LibraryAvailability.Deferred:
                    tags.Add(new LibraryItemTag(Strings.Library_Availability_Deferred, LibraryItemTagKind.OnPan));
                    break;
            }

            return tags;
        }
    }

    /// <summary>"Ed 46 available" (or with the update), naming what the source now offers.</summary>
    private string UpdateText() => (Item.Edition, Item.Update) switch
    {
        ({ } edition, { } update and > 0) => string.Format(CultureInfo.CurrentCulture, Strings.Library_Tag_EditionUpdateAvailableFormat, edition, update),
        ({ } edition, _) => string.Format(CultureInfo.CurrentCulture, Strings.Library_Tag_EditionAvailableFormat, edition),
        _ => Strings.Library_Availability_Outdated,
    };

    /// <summary>The primary state in words for the details header, e.g. "Online · 1,7 MB".</summary>
    public string PrimaryStateText
    {
        get
        {
            var words = PrimaryAvailability switch
            {
                LibraryPrimaryAvailability.Local => Strings.Library_Availability_Local,
                LibraryPrimaryAvailability.Online => Strings.Library_Availability_Online,
                LibraryPrimaryAvailability.Missing => Strings.Library_Availability_Missing,
                LibraryPrimaryAvailability.Update => IsForecast ? Strings.Library_Tag_NewRun : Strings.Library_Availability_Outdated,
                LibraryPrimaryAvailability.Expired => Strings.Library_Tag_Expired,
                _ => Strings.Library_Availability_Listed,
            };
            var size = PrimaryAvailability switch
            {
                LibraryPrimaryAvailability.Online => (Item.Location as RemoteItemLocation)?.SizeBytes,
                LibraryPrimaryAvailability.Local or LibraryPrimaryAvailability.Update or LibraryPrimaryAvailability.Expired => LocalSize(),
                _ => null,
            };
            return size is { } bytes
                ? string.Format(CultureInfo.CurrentCulture, Strings.Library_StateSizeFormat, words, FormatBytes(bytes))
                : words;
        }
    }

    /// <summary>The size of the local copy's files (base and updates), or <see langword="null"/> when unknown.</summary>
    private long? LocalSize()
    {
        if (EffectiveItem.Location is not LocalItemLocation { IsZip: false } local)
            return null;
        try
        {
            return new[] { local.RelativePath }.Concat(local.UpdateRelativePaths)
                .Select(p => new FileInfo(Path.Combine(local.RootPath, p.Replace('/', Path.DirectorySeparatorChar))))
                .Where(f => f.Exists)
                .Sum(f => f.Length) is > 0 and var total ? total : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The Download button's tooltip, naming the size when known: "Download (1,7 MB)".</summary>
    public string DownloadTooltip => Item.Location is RemoteItemLocation { SizeBytes: { } size }
        ? string.Format(CultureInfo.CurrentCulture, Strings.Tooltip_DownloadSizeFormat, FormatBytes(size))
        : Strings.Button_Download;

    /// <summary>The Load button's tooltip: "Load after download" for an online dataset, else "Load".</summary>
    public string LoadTooltip => CanLoadAfterDownload ? Strings.Button_LoadAfterDownload : Strings.Button_Load;

    /// <summary>True when the item is online and can be downloaded and then loaded in one step.</summary>
    public bool CanLoadAfterDownload => !CanLoad && CanDownload;

    /// <summary>The availability in words (details pane).</summary>
    public string AvailabilityText => Availability switch
    {
        LibraryAvailability.Local => Strings.Library_Availability_Local,
        LibraryAvailability.Online => Strings.Library_Availability_Online,
        LibraryAvailability.Missing => Strings.Library_Availability_Missing,
        LibraryAvailability.Deferred => Strings.Library_Availability_Deferred,
        LibraryAvailability.Loaded => Strings.Library_Availability_Loaded,
        LibraryAvailability.Outdated => IsForecast ? Strings.Library_Tag_NewRun : Strings.Library_Availability_Outdated,
        LibraryAvailability.Expired => Strings.Library_Tag_Expired,
        _ => Strings.Library_Availability_Listed,
    };

    /// <summary>True when the item is cancelled or withdrawn.</summary>
    public bool IsCancelled => Item.Status == CollectionItemStatus.Cancelled;

    /// <summary>True when the source flags the item not for navigation.</summary>
    public bool NotForNavigation =>
        Item.Properties.TryGetValue("notForNavigation", out var value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the item has geographic bounds.</summary>
    public bool HasBounds => Item.Bounds is not null;

    /// <summary>
    /// The details pane's fields in groups: Product (what it is), Coverage
    /// (where), Source (where it comes from, then the source's own properties).
    /// </summary>
    public IReadOnlyList<LibraryDetailGroup> Details
    {
        get
        {
            var c = CultureInfo.CurrentCulture;
            var product = new List<LibraryDetailField>();
            var coverage = new List<LibraryDetailField>();
            var source = new List<LibraryDetailField>();
            static void Add(List<LibraryDetailField> fields, string label, string? value, bool mono = false, string? copy = null)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    fields.Add(new LibraryDetailField(label, value, mono, copy));
            }

            if (IsGroupHeader && !IsModelHeader)
            {
                // An unpacked package: where it came from and what it held.
                Add(source, Strings.Library_Field_Collection, _collectionName);
                Add(source, PropertyLabel("package"), Item.Name);
                Add(source, Strings.Library_Field_Datasets, GroupCount.ToString("N0", c));
                return [new LibraryDetailGroup(Strings.Library_Group_Source, source)];
            }

            var forecast = new List<LibraryDetailField>();
            if (IsForecast)
            {
                if (ShownRun is { } shown)
                    Add(forecast, Strings.Library_Field_Run, ForecastRuns.FormatRun(shown), mono: true);
                if (ShownRun is { } from && ShownValidTo is { } to)
                    Add(forecast, Strings.Library_Field_Valid, $"{ForecastRuns.FormatRun(from)} → {ForecastRuns.FormatRun(to)}", mono: true);
                Add(forecast, Strings.Library_Field_NewerRun, Availability == LibraryAvailability.Outdated
                        && S100ForecastFeedIndexer.RunOf(Item) is { } newer
                    ? string.Format(c, Strings.Library_NewerRunOnlineFormat, ForecastRuns.FormatRun(newer))
                    : Source.Index is { } index
                        ? string.Format(c, Strings.Library_NewerRunNoneFormat, index.IndexedAt.ToLocalTime().ToString("g", c))
                        : null);
            }

            Add(product, Strings.Library_Field_Spec, ProductText());
            Add(product, Strings.Library_Field_Edition, (Item.Edition, Item.Update) switch
            {
                ({ } e, { } u) => string.Format(c, Strings.Library_EditionUpdateLongFormat, e, u),
                ({ } e, null) => string.Format(c, Strings.Library_EditionLongFormat, e),
                _ => null,
            }, mono: true);
            Add(product, Strings.Library_Field_Issued, Item.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mono: true);
            Add(product, Strings.Library_Field_UpdateApplied, Item.UpdateApplicationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mono: true);
            Add(product, Strings.Library_Field_Status, Item.Status == CollectionItemStatus.Unknown ? null : Item.Status.ToString());
            Add(product, Strings.Library_Field_Band, Item.UsageBand?.ToString(c));
            Add(product, Strings.Library_Field_CompilationScale, Item.CompilationScale is { } cscl ? "1:" + cscl.ToString("N0", c) : null, mono: true);
            Add(product, Strings.Library_Field_DisplayScales, FormatScales(Item.MinimumDisplayScale, Item.MaximumDisplayScale, c), mono: true);

            if (Item.Bounds is { } b)
            {
                Add(coverage, Strings.Library_Field_NorthEast, LatLonFormatter.Format(b.North, b.East), mono: true);
                Add(coverage, Strings.Library_Field_SouthWest, LatLonFormatter.Format(b.South, b.West), mono: true);
            }

            Add(source, Strings.Library_Field_Collection, _collectionName);
            if (Item.Properties.TryGetValue(LocalManifestIndexer.GroupProperty, out var groupId))
            {
                var groupName = Item.Properties.GetValueOrDefault(LocalManifestIndexer.GroupNameProperty) ?? groupId;
                Add(source, Strings.Library_Detail_Group, string.Equals(groupName, groupId, StringComparison.Ordinal)
                    ? groupId
                    : string.Format(c, Strings.Library_GroupValueFormat, groupName, groupId));
            }
            if (Item.Location is RemoteItemLocation && EffectiveItem.Location is LocalItemLocation downloaded)
            {
                var path = LibraryAvailabilityResolver.ResolvePath(downloaded);
                Add(source, Strings.Library_Field_Location, path, mono: true, copy: path);
            }

            switch (Item.Location)
            {
                case LocalItemLocation local:
                    var localPath = LibraryAvailabilityResolver.ResolvePath(local) + (local.IsZip ? " → " + local.RelativePath : string.Empty);
                    Add(source, Strings.Library_Field_Location, localPath, mono: true, copy: localPath);
                    if (local.UpdateRelativePaths.Count > 0)
                        Add(source, Strings.Library_Field_Updates, local.UpdateRelativePaths.Count.ToString(c));
                    break;
                case RemoteItemLocation remote:
                    Add(source, Strings.Library_Field_Download, ShortUrl(remote.Uri), mono: true, copy: remote.Uri.AbsoluteUri);
                    Add(source, Strings.Library_Field_Size, remote.SizeBytes is { } size ? FormatBytes(size) : null);
                    if (DownloadStatus is { State: LibraryDownloadItemState.Failed, Error: { } error })
                        Add(source, Strings.Library_Field_LastDownload, string.Format(c, Strings.Library_LastDownloadFailedFormat, error));
                    break;
            }

            foreach (var (key, value) in Item.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (key is not ("notForNavigation" or LocalManifestIndexer.GroupProperty or LocalManifestIndexer.GroupNameProperty))
                    Add(source, PropertyLabel(key), value);
            }

            return new[]
            {
                new LibraryDetailGroup(Strings.Library_Group_Forecast, forecast),
                new LibraryDetailGroup(Strings.Library_Group_Product, product),
                new LibraryDetailGroup(Strings.Library_Group_Coverage, coverage),
                new LibraryDetailGroup(Strings.Library_Group_Source, source),
            }.Where(g => g.Fields.Count > 0).ToArray();
        }
    }

    /// <summary>"S-57 · ENC cell", "S-101 · Electronic Navigational Chart (2.0.0)".</summary>
    private string ProductText()
    {
        var name = Item.ProductSpec == "S-57" ? Strings.Library_Product_S57 : Strings.SpecDisplayName(Item.ProductSpec);
        var text = name is null ? Item.ProductSpec : $"{Item.ProductSpec} · {name}";
        return Item.ProductSpecVersion is { } version ? $"{text} ({version})" : text;
    }

    /// <summary>"ienccloud.us · U37IL257.zip": the host and file of a download URL.</summary>
    internal static string ShortUrl(Uri uri)
    {
        var file = Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrEmpty(file) ? uri.Host : $"{uri.Host} · {Uri.UnescapeDataString(file)}";
    }

    /// <summary>A readable label for a property key: curated, else the camelCase key split into words.</summary>
    internal static string PropertyLabel(string key)
    {
        if (Strings.LibraryPropertyLabel(key) is { } label)
            return label;

        var words = new System.Text.StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];
            if (i == 0)
                words.Append(char.ToUpperInvariant(ch));
            else if (char.IsUpper(ch) && !char.IsUpper(key[i - 1]))
                words.Append(' ').Append(char.ToLowerInvariant(ch));
            else
                words.Append(ch);
        }

        return words.ToString();
    }

    /// <summary>Formats a byte count for display (e.g. <c>1.6 MB</c>).</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes} B")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }

    private static string? FormatScales(int? minimum, int? maximum, CultureInfo c) =>
        (minimum, maximum) switch
        {
            (null, null) => null,
            ({ } min, { } max) => $"1:{min.ToString("N0", c)} – 1:{max.ToString("N0", c)}",
            ({ } min, null) => $"≤ 1:{min.ToString("N0", c)}",
            (null, { } max) => $"≥ 1:{max.ToString("N0", c)}",
        };

    /// <summary>
    /// True when the item matches a free-text filter (name, title, spec, or a
    /// property value such as a state code), case-insensitively.
    /// </summary>
    public bool Matches(string filter) =>
        Item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (Item.Title?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || Item.ProductSpec.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Item.Properties.Values.Any(v => v.Contains(filter, StringComparison.OrdinalIgnoreCase));
}
