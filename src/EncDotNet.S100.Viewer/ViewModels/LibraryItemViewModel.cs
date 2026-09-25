using System.Globalization;
using Avalonia.Media;
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
    private LibraryAvailability? _availability;
    private CollectionItem? _effective;

    public LibraryItemViewModel(
        CollectionItem item,
        LibrarySource source,
        Func<CollectionItem, LibraryLoadState>? loadState = null,
        ILibraryDownloader? downloader = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        Item = item;
        Source = source;
        _loadState = loadState;
        _downloader = downloader;
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

    /// <summary>A compact one-line summary: spec, band, edition/update, issue date.</summary>
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
        OnPropertyChanged(nameof(AvailabilityBrush));
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(Details));
    }

    /// <summary>The availability badge text.</summary>
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

    /// <summary>The availability badge fill.</summary>
    public IBrush AvailabilityBrush => new SolidColorBrush(Availability switch
    {
        LibraryAvailability.Local => Color.Parse("#4d9a6a"),
        LibraryAvailability.Online => Color.Parse("#4f7fbf"),
        LibraryAvailability.Missing => Color.Parse("#c0504d"),
        LibraryAvailability.Deferred => Color.Parse("#8a6fb8"),
        LibraryAvailability.Loaded => Color.Parse("#2e6b45"),
        LibraryAvailability.Outdated => Color.Parse("#c07a2c"),
        _ => Color.Parse("#8a8f98"),
    });

    /// <summary>True when the item is cancelled or withdrawn.</summary>
    public bool IsCancelled => Item.Status == CollectionItemStatus.Cancelled;

    /// <summary>True when the source flags the item not for navigation.</summary>
    public bool NotForNavigation =>
        Item.Properties.TryGetValue("notForNavigation", out var value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the item has geographic bounds.</summary>
    public bool HasBounds => Item.Bounds is not null;

    /// <summary>The label/value rows of the details pane, in display order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Details
    {
        get
        {
            var rows = new List<KeyValuePair<string, string>>();
            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    rows.Add(new(label, value));
            }

            var c = CultureInfo.CurrentCulture;
            Add(Strings.Library_Field_Title, Item.Title);
            Add(Strings.Library_Field_Spec, Item.ProductSpecVersion is { } v ? $"{Item.ProductSpec} ({v})" : Item.ProductSpec);
            Add(Strings.Library_Field_Edition, Item.Edition?.ToString(c));
            Add(Strings.Library_Field_Update, Item.Update?.ToString(c));
            Add(Strings.Library_Field_Issued, Item.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(Strings.Library_Field_UpdateApplied, Item.UpdateApplicationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(Strings.Library_Field_Status, Item.Status == CollectionItemStatus.Unknown ? null : Item.Status.ToString());
            Add(Strings.Library_Field_Band, Item.UsageBand?.ToString(c));
            Add(Strings.Library_Field_CompilationScale, Item.CompilationScale is { } cscl ? "1:" + cscl.ToString("N0", c) : null);
            Add(Strings.Library_Field_DisplayScales, FormatScales(Item.MinimumDisplayScale, Item.MaximumDisplayScale, c));
            if (Item.Bounds is { } b)
            {
                Add(Strings.Library_Field_NorthEast, LatLonFormatter.Format(b.North, b.East));
                Add(Strings.Library_Field_SouthWest, LatLonFormatter.Format(b.South, b.West));
            }

            if (Item.Location is RemoteItemLocation && EffectiveItem.Location is LocalItemLocation downloaded)
                Add(Strings.Library_Field_Location, LibraryAvailabilityResolver.ResolvePath(downloaded));

            switch (Item.Location)
            {
                case LocalItemLocation local:
                    Add(Strings.Library_Field_Location, LibraryAvailabilityResolver.ResolvePath(local)
                        + (local.IsZip ? " → " + local.RelativePath : string.Empty));
                    if (local.UpdateRelativePaths.Count > 0)
                        Add(Strings.Library_Field_Updates, local.UpdateRelativePaths.Count.ToString(c));
                    break;
                case RemoteItemLocation remote:
                    Add(Strings.Library_Field_Download, remote.Uri.AbsoluteUri);
                    Add(Strings.Library_Field_Size, remote.SizeBytes is { } size ? FormatBytes(size) : null);
                    break;
            }

            foreach (var (key, value) in Item.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                Add(key, value);

            return rows;
        }
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
