using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
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
    private LibraryItemState? _state;
    private readonly TimeProvider _time;
    private readonly Func<TimeFormat>? _timeFormat;
    private IReadOnlyList<LibraryItemViewModel> _members = [];

    public LibraryItemViewModel(
        CollectionItem item,
        LibrarySource source,
        Func<CollectionItem, LibraryLoadState>? loadState = null,
        ILibraryDownloader? downloader = null,
        string? collectionName = null,
        Func<LibraryItemViewModel, Task>? download = null,
        TimeProvider? time = null,
        Func<TimeFormat>? timeFormat = null)
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
        _timeFormat = timeFormat;
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
    public CollectionItem EffectiveItem => State.EffectiveItem;

    /// <summary>
    /// The item's state as the Library core resolves it (availability, the
    /// copy that opens, the time it covers); recreated by <see cref="RefreshAvailability"/>.
    /// </summary>
    internal LibraryItemState State => _state ??= new LibraryItemState(Item, Source, _downloader, _loadState, _time);

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
        Func<CollectionItem, LibraryLoadState>? loadState, ILibraryDownloader? downloader, TimeProvider? time,
        Func<TimeFormat>? timeFormat = null)
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
        header = new LibraryItemViewModel(item, source, loadState, downloader, time: time, timeFormat: timeFormat)
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
        : State.LocalRun;

    /// <summary>The run shown: the downloaded copy's, else the catalogue's latest.</summary>
    private DateTimeOffset? ShownRun => LocalRun ?? S100ForecastFeedIndexer.RunOf(Item);

    /// <summary>The end of the shown run's valid window.</summary>
    private DateTimeOffset? ShownValidTo => ForecastRuns.ShownWindow(Item, LocalRun)?.ValidTo;

    /// <summary>
    /// The time the item's data covers (#711): a forecast run's valid window
    /// (the downloaded copy's run, else the catalogue's), or a dataset's own
    /// time coverage; <see langword="null"/> for none.
    /// </summary>
    internal (DateTime Start, DateTime End)? ValidWindow =>
        IsForecast
            ? ShownRun is { } run && ShownValidTo is { } end ? (run.UtcDateTime, end.UtcDateTime) : null
            : LibraryItemState.TimeCoverage(Item);

    /// <summary>The run of the downloaded copy, if any (UTC).</summary>
    internal DateTime? LocalRunTime => LocalRun?.UtcDateTime;

    /// <summary>The catalogue's latest run and its valid window, for a forecast (UTC).</summary>
    internal (DateTime Run, DateTime End)? CatalogueRun => State.CatalogueRun;

    /// <summary>True when the item's downloaded copy is loaded on the map now.</summary>
    internal bool IsLoadedNow => State.IsLoadedNow;

    /// <summary>True when a forecast run row shows its valid window as a bar.</summary>
    public bool HasForecastWindow => IsForecastRunRow && ShownValidTo is not null;

    /// <summary>How much of the shown run's window has passed (0–1).</summary>
    public double WindowElapsed => ShownRun is { } start && ShownValidTo is { } end && end > start
        ? Math.Clamp((_time.GetUtcNow() - start) / (end - start), 0, 1)
        : 0;

    /// <summary>How much of the shown run's window is left (0–1).</summary>
    public double WindowRemaining => 1 - WindowElapsed;

    /// <summary>"39 h left", or "Ended 9 h ago".</summary>
    public string? TimeLeftText => ShownValidTo is { } end ? ForecastRunText.TimeLeft(end, _time.GetUtcNow()) : null;

    /// <summary>True when the shown run's window has ended.</summary>
    public bool IsWindowEnded => ShownValidTo is { } end && end <= _time.GetUtcNow();

    /// <summary>A run time as the user reads it: their Local/UTC setting (#730); UTC without one.</summary>
    private string FormatRun(DateTimeOffset time) =>
        ForecastRunText.FormatRun(time, _timeFormat?.Invoke() ?? TimeFormat.Utc, _time.LocalTimeZone);

    /// <summary>Re-reads the run times after the user's Local/UTC setting changed (#730).</summary>
    internal void RefreshTimeFormat()
    {
        if (!IsForecast)
            return;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Details));
    }

    /// <summary>Re-evaluates what depends on the clock (time left, Expired); called every minute for forecasts.</summary>
    public void RefreshClock()
    {
        if (!IsForecast)
            return;
        if (_availability is not null)
        {
            _availability = null;
            _state = null;
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
                parts.Add(string.Format(c, Strings.Library_Forecast_RunFormat, FormatRun(run)));
            if (ShownValidTo is { } end)
                parts.Add(string.Format(c, Strings.Library_Forecast_ToFormat, FormatRun(end)));
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
            ? _members.Select(m => _loadState?.Invoke(m.EffectiveItem))
            : [_loadState?.Invoke(EffectiveItem)];
        tags.AddRange(LibraryItemText.ForecastRunTags(states, Availability).Select(Tag));
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
        : State.Availability;

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
        _state = null;
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
            if (IsModelHeader)
            {
                var tags = LibraryItemText.DownloadTags(DownloadStatus).Select(Tag).ToList();
                AddForecastTags(tags);
                return tags;
            }

            if (IsGroupHeader)
            {
                return
                [
                    .. LibraryItemText.DownloadTags(DownloadStatus).Select(Tag),
                    new LibraryItemTag(Strings.Library_Tag_Unpacked, LibraryTagKind.Neutral),
                ];
            }

            return [.. LibraryItemText.Tags(TextInput).Select(Tag)];
        }
    }

    /// <summary>A core tag as the row shows it; a failed download's tag retries it when clicked.</summary>
    private LibraryItemTag Tag(LibraryTag tag) =>
        new(tag.Text, tag.Kind, tag.Kind == LibraryTagKind.Failed && _download is not null ? RetryCommand : null);

    /// <summary>The row as the Library core describes it (#792): tags and details in shared words.</summary>
    private LibraryItemTextInput TextInput => new(Item, Source, Availability, EffectiveItem)
    {
        Download = DownloadStatus,
        LoadState = _loadState,
        CollectionName = _collectionName,
        LocalRun = LocalRun,
        TimeFormat = ForecastRunText.ToLibrary(_timeFormat?.Invoke() ?? TimeFormat.Utc),
        TimeZone = _time.LocalTimeZone,
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
    public bool NotForNavigation => State.NotForNavigation;

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
            if (IsGroupHeader && !IsModelHeader)
            {
                // An unpacked package: where it came from and what it held.
                var source = new List<LibraryDetailField>();
                if (!string.IsNullOrWhiteSpace(_collectionName))
                    source.Add(new LibraryDetailField(Strings.Library_Field_Collection, _collectionName));
                source.Add(new LibraryDetailField(LibraryTextFormat.PropertyLabel("package"), Item.Name));
                source.Add(new LibraryDetailField(Strings.Library_Field_Datasets, GroupCount.ToString("N0", CultureInfo.CurrentCulture)));
                return [new LibraryDetailGroup(Strings.Library_Group_Source, source)];
            }

            return LibraryItemText.Details(TextInput);
        }
    }

    /// <summary>Formats a byte count for display (e.g. <c>1.6 MB</c>).</summary>
    public static string FormatBytes(long bytes) => LibraryTextFormat.Bytes(bytes);

    /// <summary>
    /// True when the item matches a free-text filter (name, title, spec, or a
    /// property value such as a state code), case-insensitively.
    /// </summary>
    public bool Matches(string filter) => State.Matches(filter);
}
