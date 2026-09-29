using System.Globalization;
using EncDotNet.S100.Collections;
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

    public LibraryItemViewModel(
        CollectionItem item,
        LibrarySource source,
        Func<CollectionItem, LibraryLoadState>? loadState = null,
        ILibraryDownloader? downloader = null,
        string? collectionName = null,
        Func<LibraryItemViewModel, Task>? download = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        Item = item;
        Source = source;
        _loadState = loadState;
        _downloader = downloader;
        _collectionName = collectionName;
        _download = download;
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
            OnPropertyChanged(nameof(Tags));
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

    /// <summary>The dataset name (e.g. <c>US5AK1AM</c>).</summary>
    public string Name => Item.Name;

    /// <summary>The descriptive title, when the source supplies one.</summary>
    public string? Subtitle => Item.Title;

    /// <summary>True when there is a <see cref="Subtitle"/> to show.</summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Item.Title);

    /// <summary>A compact one-line summary: spec, band, edition/update, issue date, and download size.</summary>
    public string Summary
    {
        get
        {
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

    /// <summary>Where the data can be had now (resolved on first access).</summary>
    public LibraryAvailability Availability => _availability ??= (_loadState?.Invoke(EffectiveItem)) switch
    {
        LibraryLoadState.Loaded => LibraryAvailability.Loaded,
        LibraryLoadState.Deferred => LibraryAvailability.Deferred,
        _ when _downloader?.IsOutdated(Item) == true => LibraryAvailability.Outdated,
        _ => LibraryAvailabilityResolver.Resolve(EffectiveItem),
    };

    /// <summary>True when the item can be opened from disk (local, not already loaded).</summary>
    public bool CanLoad => Availability is LibraryAvailability.Local or LibraryAvailability.Deferred or LibraryAvailability.Outdated;

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
        OnPropertyChanged(nameof(CanLoadAfterDownload));
        RefreshDownload();
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(Details));
    }

    /// <summary>
    /// Where the data is — exactly one state, drawn as the row swatch exactly
    /// like the map outline.
    /// </summary>
    public LibraryPrimaryAvailability PrimaryAvailability => LibraryOutlineStyles.Primary(Availability);

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

            switch (Availability)
            {
                case LibraryAvailability.Outdated:
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
                _ => Strings.Library_Availability_Listed,
            };
            return PrimaryAvailability == LibraryPrimaryAvailability.Online && Item.Location is RemoteItemLocation { SizeBytes: { } size }
                ? string.Format(CultureInfo.CurrentCulture, Strings.Library_StateSizeFormat, words, FormatBytes(size))
                : words;
        }
    }

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
        LibraryAvailability.Outdated => Strings.Library_Availability_Outdated,
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
                    break;
            }

            foreach (var (key, value) in Item.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (key != "notForNavigation")
                    Add(source, PropertyLabel(key), value);
            }

            return new[]
            {
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
