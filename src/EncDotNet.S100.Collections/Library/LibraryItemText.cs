using System.Globalization;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Collections.Library;

/// <summary>What a Library item tag says about the item (and how it is drawn).</summary>
public enum LibraryTagKind
{
    /// <summary>A newer edition or run is online.</summary>
    Update,

    /// <summary>The item is loaded on the map.</summary>
    Loaded,

    /// <summary>The item loads as the map pans to it.</summary>
    OnPan,

    /// <summary>The item is queued for download.</summary>
    Queued,

    /// <summary>The item's download failed.</summary>
    Failed,

    /// <summary>A neutral fact, e.g. a community-list package.</summary>
    Neutral,

    /// <summary>A forecast run whose window has ended.</summary>
    Expired,

    /// <summary>A weather run more than 12 hours old (#1075): "Stale", with "Load latest run" offered.</summary>
    Stale,

    /// <summary>
    /// NATO Additional Military Layers data, "AML · {code}": outlined ink with a
    /// layers glyph, so it never reads as a state colour or as
    /// <see cref="Neutral"/>.
    /// </summary>
    Military,
}

/// <summary>A short sentence-case tag after an item's name, e.g. "Ed 46 available" or "Loaded".</summary>
/// <param name="Text">The tag's text.</param>
/// <param name="Kind">What it says.</param>
/// <param name="ToolTip">More about it on hover, or <see langword="null"/>.</param>
public sealed record LibraryTag(string Text, LibraryTagKind Kind, string? ToolTip = null);

/// <summary>A group of an item's details (Forecast, Product, Coverage, Source).</summary>
/// <param name="Title">The group's title.</param>
/// <param name="Fields">Its fields, in order.</param>
public sealed record LibraryDetailGroup(string Title, IReadOnlyList<LibraryDetailField> Fields)
{
    /// <summary>The title in capitals, as a section header shows it.</summary>
    public string Header => Title.ToUpper(CultureInfo.CurrentCulture);
}

/// <summary>A labelled value in an item's details.</summary>
/// <param name="Label">The field's label.</param>
/// <param name="Value">The value as shown.</param>
/// <param name="IsMono">True when the value is a code, date or position, shown in monospace.</param>
/// <param name="CopyValue">What "Copy" copies (e.g. a full path or URL), or null when the value cannot be copied.</param>
public sealed record LibraryDetailField(string Label, string Value, bool IsMono = false, string? CopyValue = null)
{
    /// <summary>True when the field has a <see cref="CopyValue"/>.</summary>
    public bool IsCopyable => CopyValue is not null;
}

/// <summary>
/// One Library item as its row describes it: the item, where it stands, and
/// what the host knows about its download and load. Group rows (an unpacked
/// package, a forecast model's tiles) are the host's own and not described here.
/// </summary>
/// <param name="Item">The indexed item.</param>
/// <param name="Source">The source that lists it.</param>
/// <param name="Availability">Where the data is (from <see cref="LibraryItemState"/>).</param>
/// <param name="EffectiveItem">The item as it opens now: its downloaded copy, if any.</param>
public sealed record LibraryItemTextInput(
    CollectionItem Item,
    LibrarySource Source,
    LibraryAvailability Availability,
    CollectionItem EffectiveItem)
{
    /// <summary>Where the item stands in a download, if anywhere.</summary>
    public LibraryDownloadItemStatus? Download { get; init; }

    /// <summary>Whether the host has a copy loaded or deferred; null when it loads nothing.</summary>
    public Func<CollectionItem, LibraryLoadState>? LoadState { get; init; }

    /// <summary>The name of the collection the source belongs to, for the details.</summary>
    public string? CollectionName { get; init; }

    /// <summary>The run of the downloaded copy, for a forecast.</summary>
    public DateTimeOffset? LocalRun { get; init; }

    /// <summary>Whether run times are shown in local time or UTC (#730).</summary>
    public LibraryTimeFormat TimeFormat { get; init; } = LibraryTimeFormat.Utc;

    /// <summary>The user's time zone, for <see cref="LibraryTimeFormat.Local"/>.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;

    /// <summary>The time now, which decides whether a weather run is stale (#1075); the system clock when null.</summary>
    public DateTimeOffset? Now { get; init; }
}

/// <summary>
/// The tags and details of a Library item, in the words the viewer's Library
/// panel uses, for every host (#792).
/// </summary>
public static class LibraryItemText
{
    /// <summary>
    /// True for a remote S-100 catalogue's or forecast feed's dataset: a newer
    /// edition is shown in its summary rather than as a tag, since nearly every
    /// tile is reissued each quarter.
    /// </summary>
    /// <param name="source">The source that lists the item.</param>
    public static bool QuietUpdates(LibrarySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Definition is S100CatalogueFeedSource or S100ForecastFeedSource;
    }

    /// <summary>
    /// True for a package not yet listed by its datasets: a community-list
    /// entry, or a SECOM exchange-set object (#916), that may hold several.
    /// The datasets of a downloaded package are keyed <c>package/name</c>.
    /// </summary>
    /// <param name="item">The item.</param>
    public static bool IsPackageEntry(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Location is RemoteItemLocation { Package: { } package, Layout: null }
            && !item.Key.StartsWith(package + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// True for an item that stands for a whole forecast run: a model's one file
    /// when runs download as one file per model.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="source">The source that lists it.</param>
    public static bool IsForecastRun(CollectionItem item, LibrarySource source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        return ForecastRuns.IsForecast(item)
            && source.Definition is S100ForecastFeedSource { Shape: ForecastShape.Regional } or WeatherFeedSource;
    }

    /// <summary>"Queued" or "Failed · retry" for an item in a download, else nothing.</summary>
    /// <param name="download">Where the item stands in a download.</param>
    public static IEnumerable<LibraryTag> DownloadTags(LibraryDownloadItemStatus? download)
    {
        switch (download?.State)
        {
            case LibraryDownloadItemState.Queued:
                yield return new LibraryTag(LibraryText.Get("Library_Tag_Queued"), LibraryTagKind.Queued);
                break;
            case LibraryDownloadItemState.Failed:
                yield return new LibraryTag(LibraryText.Get("Library_Tag_FailedRetry"), LibraryTagKind.Failed);
                break;
        }
    }

    /// <summary>
    /// The tags of a forecast run: Loaded or On pan when any of its files is,
    /// then New run or Expired.
    /// </summary>
    /// <param name="loadStates">The load state of each of the run's files.</param>
    /// <param name="availability">The run's availability.</param>
    public static IEnumerable<LibraryTag> ForecastRunTags(IEnumerable<LibraryLoadState?> loadStates, LibraryAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(loadStates);
        var states = loadStates.ToArray();
        if (states.Contains(LibraryLoadState.Loaded))
            yield return new LibraryTag(LibraryText.Get("Library_Availability_Loaded"), LibraryTagKind.Loaded);
        else if (states.Contains(LibraryLoadState.Deferred))
            yield return new LibraryTag(LibraryText.Get("Library_Availability_Deferred"), LibraryTagKind.OnPan);

        switch (availability)
        {
            case LibraryAvailability.Outdated:
                yield return new LibraryTag(LibraryText.Get("Library_Tag_NewRun"), LibraryTagKind.Update);
                break;
            case LibraryAvailability.Expired:
                yield return new LibraryTag(LibraryText.Get("Library_Tag_Expired"), LibraryTagKind.Expired);
                break;
        }
    }

    /// <summary>
    /// What is happening to the item: zero or more sentence-case tags (a
    /// download, an update available, loaded, on pan, a package).
    /// </summary>
    /// <param name="input">The item.</param>
    public static IReadOnlyList<LibraryTag> Tags(LibraryItemTextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var tags = new List<LibraryTag>(2);
        tags.AddRange(DownloadTags(input.Download));

        var item = input.Item;
        if (ForecastRuns.IsForecast(item))
        {
            // A run's tiles are updated together; the model's row carries its state.
            if (IsForecastRun(item, input.Source))
                tags.AddRange(ForecastRunTags([input.LoadState?.Invoke(input.EffectiveItem)], input.Availability));
            // A weather run: pinned (kept on purpose), or stale past 12 hours (#1075).
            if (Weather.WeatherRuns.IsPinned(item))
                tags.Add(new LibraryTag(LibraryText.Get("Library_Tag_Pinned"), LibraryTagKind.Neutral));
            else if (Weather.WeatherRuns.IsStale(item, input.LocalRun, input.Now ?? DateTimeOffset.UtcNow))
                tags.Add(new LibraryTag(LibraryText.Get("Library_Tag_Stale"), LibraryTagKind.Stale));
            return tags;
        }

        if (IsPackageEntry(item))
            tags.Add(new LibraryTag(LibraryText.Get("Library_Tag_Package"), LibraryTagKind.Neutral));

        if (AmlLibraryProperties.Read(item.Properties, item.Name) is { } aml)
            tags.Add(new LibraryTag(aml.TagText, LibraryTagKind.Military, aml.TagToolTip));

        switch (input.Availability)
        {
            case LibraryAvailability.Outdated when !QuietUpdates(input.Source):
                tags.Add(new LibraryTag(UpdateText(item), LibraryTagKind.Update));
                break;
            case LibraryAvailability.Loaded:
                tags.Add(new LibraryTag(LibraryText.Get("Library_Availability_Loaded"), LibraryTagKind.Loaded));
                break;
            case LibraryAvailability.Deferred:
                tags.Add(new LibraryTag(LibraryText.Get("Library_Availability_Deferred"), LibraryTagKind.OnPan));
                break;
        }

        return tags;
    }

    /// <summary>"Ed 46 available" (or with the update), naming what the source now offers.</summary>
    private static string UpdateText(CollectionItem item) => (item.Edition, item.Update) switch
    {
        ({ } edition, { } update and > 0) => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_Tag_EditionUpdateAvailableFormat"), edition, update),
        ({ } edition, _) => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_Tag_EditionAvailableFormat"), edition),
        _ => LibraryText.Get("Library_Availability_Outdated"),
    };

    /// <summary>
    /// The item's details in groups: Forecast (its run), Product (what it is),
    /// Coverage (where), Source (where it comes from, then the source's own
    /// properties). Empty groups are left out.
    /// </summary>
    /// <param name="input">The item.</param>
    public static IReadOnlyList<LibraryDetailGroup> Details(LibraryItemTextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = CultureInfo.CurrentCulture;
        var item = input.Item;
        var forecast = new List<LibraryDetailField>();
        var product = new List<LibraryDetailField>();
        var coverage = new List<LibraryDetailField>();
        var source = new List<LibraryDetailField>();
        static void Add(List<LibraryDetailField> fields, string label, string? value, bool mono = false, string? copy = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields.Add(new LibraryDetailField(label, value, mono, copy));
        }

        string Run(DateTimeOffset time) => LibraryTextFormat.Run(time, input.TimeFormat, input.TimeZone);

        if (ForecastRuns.IsForecast(item))
        {
            var shownRun = input.LocalRun ?? S100ForecastFeedIndexer.RunOf(item);
            var shownValidTo = ForecastRuns.ShownWindow(item, input.LocalRun)?.ValidTo;
            if (shownRun is { } shown)
                Add(forecast, LibraryText.Get("Library_Field_Run"), Run(shown), mono: true);
            if (shownRun is { } from && shownValidTo is { } to)
                Add(forecast, LibraryText.Get("Library_Field_Valid"), $"{Run(from)} → {Run(to)}", mono: true);
            Add(forecast, LibraryText.Get("Library_Field_NewerRun"), input.Availability == LibraryAvailability.Outdated
                    && S100ForecastFeedIndexer.RunOf(item) is { } newer
                ? string.Format(c, LibraryText.Get("Library_NewerRunOnlineFormat"), Run(newer))
                : input.Source.Index is { } index
                    ? string.Format(c, LibraryText.Get("Library_NewerRunNoneFormat"), index.IndexedAt.ToLocalTime().ToString("g", c))
                    : null);
        }

        Add(product, LibraryText.Get("Library_Field_Spec"), LibraryTextFormat.Product(item));
        Add(product, LibraryText.Get("Library_Field_Edition"), (item.Edition, item.Update) switch
        {
            ({ } e, { } u) => string.Format(c, LibraryText.Get("Library_EditionUpdateLongFormat"), e, u),
            ({ } e, null) => string.Format(c, LibraryText.Get("Library_EditionLongFormat"), e),
            _ => null,
        }, mono: true);
        Add(product, LibraryText.Get("Library_Field_Issued"), item.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mono: true);
        Add(product, LibraryText.Get("Library_Field_UpdateApplied"), item.UpdateApplicationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mono: true);
        Add(product, LibraryText.Get("Library_Field_Status"), item.Status == CollectionItemStatus.Unknown ? null : item.Status.ToString());
        Add(product, LibraryText.Get("Library_Field_Band"), item.UsageBand?.ToString(c));
        Add(product, LibraryText.Get("Library_Field_CompilationScale"), item.CompilationScale is { } cscl ? "1:" + cscl.ToString("N0", c) : null, mono: true);
        Add(product, LibraryText.Get("Library_Field_DisplayScales"), LibraryTextFormat.Scales(item.MinimumDisplayScale, item.MaximumDisplayScale), mono: true);

        if (item.Bounds is { } b)
        {
            Add(coverage, LibraryText.Get("Library_Field_NorthEast"), LibraryTextFormat.LatLon(b.North, b.East), mono: true);
            Add(coverage, LibraryText.Get("Library_Field_SouthWest"), LibraryTextFormat.LatLon(b.South, b.West), mono: true);
        }

        Add(source, LibraryText.Get("Library_Field_Collection"), input.CollectionName);
        if (item.Properties.TryGetValue(LocalManifestIndexer.GroupProperty, out var groupId))
        {
            var groupName = item.Properties.GetValueOrDefault(LocalManifestIndexer.GroupNameProperty) ?? groupId;
            Add(source, LibraryText.Get("Library_Detail_Group"), string.Equals(groupName, groupId, StringComparison.Ordinal)
                ? groupId
                : string.Format(c, LibraryText.Get("Library_GroupValueFormat"), groupName, groupId));
        }
        if (item.Location is RemoteItemLocation && input.EffectiveItem.Location is LocalItemLocation downloaded)
        {
            var path = LibraryAvailabilityResolver.ResolvePath(downloaded);
            Add(source, LibraryText.Get("Library_Field_Location"), path, mono: true, copy: path);
        }

        switch (item.Location)
        {
            case LocalItemLocation local:
                var localPath = LibraryAvailabilityResolver.ResolvePath(local) + (local.IsZip ? " → " + local.RelativePath : string.Empty);
                Add(source, LibraryText.Get("Library_Field_Location"), localPath, mono: true, copy: localPath);
                if (local.UpdateRelativePaths.Count > 0)
                    Add(source, LibraryText.Get("Library_Field_Updates"), local.UpdateRelativePaths.Count.ToString(c));
                break;
            case RemoteItemLocation remote:
                Add(source, LibraryText.Get("Library_Field_Download"), LibraryTextFormat.ShortUrl(remote.Uri), mono: true, copy: remote.Uri.AbsoluteUri);
                Add(source, LibraryText.Get("Library_Field_Size"), remote.SizeBytes is { } size ? LibraryTextFormat.Bytes(size) : null);
                if (input.Download is { State: LibraryDownloadItemState.Failed, Error: { } error })
                    Add(source, LibraryText.Get("Library_Field_LastDownload"), string.Format(c, LibraryText.Get("Library_LastDownloadFailedFormat"), error));
                break;
        }

        foreach (var (key, value) in item.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // AML facts have groups of their own (#1370).
            if (AmlLibraryProperties.IsAmlKey(key))
                continue;
            if (key is not ("notForNavigation" or LocalManifestIndexer.GroupProperty or LocalManifestIndexer.GroupNameProperty
                or Weather.WeatherRuns.StaleAfterProperty or Weather.WeatherRuns.ServiceProperty
                or Weather.WeatherRuns.DownloadFolderProperty or Weather.WeatherRuns.PinnedProperty))
                Add(source, LibraryTextFormat.PropertyLabel(key), value);
        }

        return AmlDetails(item).Concat(new[]
        {
            new LibraryDetailGroup(LibraryText.Get("Library_Group_Forecast"), forecast),
            new LibraryDetailGroup(LibraryText.Get("Library_Group_Product"), product),
            new LibraryDetailGroup(LibraryText.Get("Library_Group_Coverage"), coverage),
            new LibraryDetailGroup(LibraryText.Get("Library_Group_Source"), source),
        }).Where(g => g.Fields.Count > 0).ToArray();
    }

    /// <summary>
    /// The AML groups of a NATO Additional Military Layers item (#1370,
    /// decision 6): <i>Additional Military Layers</i> (product, version,
    /// protective marking, IDO status, owner authority, caveat, scale band)
    /// and, for an AML 1.0/2.1 name, <i>From the cell name</i>. Where the name
    /// and the header disagree, the header wins and the row says so. Empty for
    /// any other item.
    /// </summary>
    /// <param name="item">The item.</param>
    public static IReadOnlyList<LibraryDetailGroup> AmlDetails(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (AmlLibraryProperties.Read(item.Properties, item.Name) is not { } aml)
            return [];

        var c = CultureInfo.CurrentCulture;
        var fields = new List<LibraryDetailField>();
        void Add(List<LibraryDetailField> list, string label, string? value, bool mono = false)
        {
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(new LibraryDetailField(label, value, mono));
        }

        Add(fields, LibraryText.Get("Aml_Field_Product"), aml.ProductDisplay);
        Add(fields, LibraryText.Get("Aml_Field_Version"), aml.Version is { } version
            ? string.Format(c, LibraryText.Get("Aml_VersionFormat"), version)
            : null);
        Add(fields, LibraryText.Get("Aml_ProtectiveMarking"), aml.ProtectiveMarking is { } marking
            ? aml.MarkingSource == AmlMarkingSource.CellName
                ? string.Format(c, LibraryText.Get("Aml_MarkingFromCellNameFormat"), marking)
                : marking
            : null);
        Add(fields, LibraryText.Get("Aml_Field_IdoStatus"), aml.IdoStatus);
        Add(fields, LibraryText.Get("Aml_Field_OwnerAuthority"), aml.OwnerAuthority);
        Add(fields, LibraryText.Get("Aml_Field_Caveat"), aml.Caveat);
        Add(fields, LibraryText.Get("Aml_Field_ScaleBand"), aml.ScaleBand is { } band
            ? string.Format(c, LibraryText.Get("Aml_ScaleBandFormat"), band)
            : null);
        var groups = new List<LibraryDetailGroup> { new(LibraryText.Get("Aml_Heading"), fields) };

        // The 3.0 trial names carry little beyond the marking letter, and their
        // product token is already the tag's: decode only 1.0/2.1 names.
        if (aml.FromName is { IsAml30Pattern: false } name)
        {
            var stem = Path.GetFileNameWithoutExtension(item.Name.Trim());
            var fromName = new List<LibraryDetailField>();
            Add(fromName, LibraryText.Get("Aml_Field_Producer"), name.Producer, mono: true);
            Add(fromName, LibraryText.Get("Aml_Field_Product"), name.Product is { } product
                ? Compared($"{stem[2]} · {product}", product, aml.ProductCode)
                : stem[2].ToString());
            Add(fromName, LibraryText.Get("Aml_Field_ScaleBand"), Compared(
                name.ScaleBand.ToString(c),
                name.ScaleBand.ToString(c),
                aml.ScaleBand?.ToString(c)));
            var letter = name.ClassificationLetter.ToString();
            Add(fromName, LibraryText.Get("Aml_ProtectiveMarking"), name.Marking is not { } named
                ? letter
                : aml.MarkingSource == AmlMarkingSource.CellName
                    ? string.Format(c, LibraryText.Get("Aml_FromName_UsedFormat"), $"{letter} · {named}")
                    : Compared($"{letter} · {named}", named, aml.ProtectiveMarking));
            groups.Add(new LibraryDetailGroup(LibraryText.Get("Aml_FromCellName"), fromName));
        }

        return groups;

        // What a part of the name says, and whether the header agrees.
        string Compared(string shown, string fromName, string? fromHeader) =>
            fromHeader is null
                ? shown
                : string.Equals(fromName, fromHeader, StringComparison.OrdinalIgnoreCase)
                    ? string.Format(c, LibraryText.Get("Aml_FromName_MatchesFormat"), shown)
                    : string.Format(c, LibraryText.Get("Aml_FromName_HeaderWinsFormat"), shown, fromHeader);
    }
}
