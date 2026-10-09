using System.Globalization;
using EncDotNet.S100.Collections.RemoteCatalogues;

namespace EncDotNet.S100.Collections.Library;

/// <summary>One resolution choice of a remote S-100 catalogue: a navigation purpose, or all of them.</summary>
/// <param name="Value">The <c>navigationPurpose</c>, or null for all.</param>
/// <param name="Label">"Port 4 m", or "Port and Transit" for all.</param>
public sealed record LibraryResolution(string? Value, string Label);

/// <summary>
/// A scope of a remote S-100 exchange catalogue (issue #685; NOAA's S-102 on
/// AWS): regions, each with its areas, and a resolution that applies to every
/// area. Sizes come from listing the bucket one region at a time
/// (<see cref="SizeGroupAsync"/>); <see cref="LibraryCatalogueScope.Changed"/> is raised when they arrive.
/// </summary>
public sealed class S100CatalogueScope : LibraryCatalogueScope
{
    private readonly Func<Uri, CancellationToken, Task<RemoteS100Catalogue>> _load;
    private readonly Func<RemoteS100Catalogue, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<Uri, S3Object>?>>? _listFolders;
    private readonly Dictionary<Uri, S3Object> _sizes = [];
    private readonly HashSet<string> _sizedRegions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sizingRegions = new(StringComparer.Ordinal);
    private RemoteS100Catalogue? _catalogue;
    private bool _sizesUnavailable;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];
    private IReadOnlyList<LibraryResolution> _resolutions = [];
    private LibraryResolution? _selectedResolution;

    /// <summary>Creates a scope of the catalogue at <paramref name="catalogUri"/>.</summary>
    /// <param name="catalogUri">The catalogue's URL.</param>
    /// <param name="load">Reads the catalogue.</param>
    /// <param name="listFolders">Lists folders for their file sizes, or null when sizes cannot be had.</param>
    public S100CatalogueScope(
        Uri catalogUri,
        Func<Uri, CancellationToken, Task<RemoteS100Catalogue>> load,
        Func<RemoteS100Catalogue, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<Uri, S3Object>?>>? listFolders = null)
        : base(catalogUri)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
        _listFolders = listFolders;
    }

    /// <inheritdoc />
    public override bool IsLoaded => _catalogue is not null;

    /// <summary>The regions, each a group of its areas (its <see cref="LibraryChoiceGroup.Key"/> is the region's folder).</summary>
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The resolution choices: all, then each navigation purpose, finest first.</summary>
    public IReadOnlyList<LibraryResolution> Resolutions => _resolutions;

    /// <summary>True when the catalogue has more than one navigation purpose to choose between.</summary>
    public bool HasResolutions => _resolutions.Count > 2;

    /// <summary>The chosen resolution; it applies to every area.</summary>
    public LibraryResolution? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (value is null || value == _selectedResolution)
                return;
            _selectedResolution = value;
            RefreshAreaDetails();
        }
    }

    /// <summary>The filter for the current region, area and resolution choices.</summary>
    public S100CatalogueFilter CurrentFilter
    {
        get
        {
            // A region whose areas are all ticked is kept as the region, so areas
            // the publisher adds to it later are included too.
            var folders = new List<string>();
            if (!IncludeAll)
            {
                foreach (var region in _groups)
                {
                    var areas = region.Options;
                    if (areas.Count > 1 && areas.All(a => a.IsSelected) && region.Key is { Length: > 0 } key)
                        folders.Add(key);
                    else
                        folders.AddRange(areas.Where(a => a.IsSelected).Select(a => a.Value));
                }
            }

            return new S100CatalogueFilter
            {
                Folders = folders,
                NavigationPurposes = _selectedResolution?.Value is { } purpose ? [purpose] : [],
            };
        }
    }

    /// <inheritdoc />
    public override bool IsEverything => CurrentFilter.IsUnscoped;

    /// <summary>The datasets are listed, not downloaded: the summary says so.</summary>
    public override string ScopeSummary => IncludeAll || SelectedCount > 0
        ? LibrarySourceText.NothingDownloads(SelectionSummary)
        : base.ScopeSummary;

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        var catalogue = await _load(CatalogUri, cancellationToken).ConfigureAwait(false);
        _sizes.Clear();
        _sizedRegions.Clear();
        _sizingRegions.Clear();
        _sizesUnavailable = false;

        _groups = [.. S100CatalogueFacets.Regions(catalogue.Items).Select(r => new LibraryChoiceGroup(
            r.Name,
            [.. r.Areas.Select(a => new LibraryChoice(a.Value, RemoteS100Catalogue.FolderName(a.Value), string.Empty))])
        {
            Key = r.Folder,
        })];

        var purposes = S100CatalogueFacets.NavigationPurposes(catalogue.Items);
        _resolutions =
        [
            new(null, purposes.Count is > 0 and <= 3
                ? JoinAnd([.. purposes.Select(p => NavigationPurposes.Name(p.Value))])
                : LibraryText.Get("Wizard_ResolutionAll")),
            .. purposes.Select(p => new LibraryResolution(p.Value, NavigationPurposes.Label(p.Value, p.GridResolution))),
        ];
        _selectedResolution = _resolutions[0];
        CatalogueDate = catalogue.IssuedAt is { } issued ? DateOnly.FromDateTime(issued.UtcDateTime) : null;
        _catalogue = catalogue;
        RefreshAreaDetails();
    });

    /// <summary>Lists a region's files for their sizes, once; its areas show "sizing…" until then.</summary>
    /// <param name="region">The region (a group of <see cref="Groups"/>).</param>
    public async Task SizeGroupAsync(LibraryChoiceGroup? region)
    {
        if (region?.Key is not { } folder || _catalogue is not { } catalogue || _listFolders is null
            || _sizesUnavailable || _sizedRegions.Contains(folder) || !_sizingRegions.Add(folder))
        {
            return;
        }

        try
        {
            var listed = await _listFolders(catalogue, [folder], CancellationToken.None).ConfigureAwait(true);
            if (listed is null)
            {
                _sizesUnavailable = true;
            }
            else
            {
                foreach (var (uri, entry) in listed)
                    _sizes[uri] = entry;
                _sizedRegions.Add(folder);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // Sizes stay unknown; the region is listed again when next asked for.
        }
        finally
        {
            _sizingRegions.Remove(folder);
        }

        if (ReferenceEquals(catalogue, _catalogue))
        {
            RefreshAreaDetails();
            OnChanged();
        }
    }

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_catalogue is null)
                return string.Empty;
            var areas = SelectedCount;
            var selected = Items(IncludeAll ? null : CurrentFilter).ToArray();
            var tiles = TilesDetail(selected.Length, SizeOf(selected), sizing: true);
            return IncludeAll || areas == 0
                ? tiles
                : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_AreasTilesFormat"), areas, tiles);
        }
    }

    /// <summary>"5 209 tiles · 17,3 GB online" for the whole catalogue at the chosen resolution.</summary>
    public override string EverythingSummary
    {
        get
        {
            var items = Items().ToArray();
            return SizeOf(items) is { } bytes
                ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_TilesOnlineFormat"), items.Length, LibraryTextFormat.Bytes(bytes))
                : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_TilesFormat"), items.Length);
        }
    }

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry =>
        _catalogue?.Items is [var item] ? new LibraryChoice(item.Key, item.Title ?? item.Name, string.Empty) : null;

    /// <summary>The ticked areas and the resolution ("Boston, Penobscot Bay · Port 4 m"), or null for everything.</summary>
    public override string? DescribeSelection()
    {
        var filter = CurrentFilter;
        var where = IncludeAll ? null : LibrarySourceText.List([.. Choices.Where(o => o.IsSelected).Select(o => o.Label)]);
        var resolution = filter.NavigationPurposes.Count > 0 ? _selectedResolution?.Label : null;
        return (where, resolution) switch
        {
            (null, null) => null,
            (null, { } r) => r,
            ({ } w, null) => w,
            ({ } w, { } r) => $"{w} · {r}",
        };
    }

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name) =>
        new S100CatalogueFeedSource(id, DescribeSelection() ?? name, CatalogUri, CurrentFilter);

    /// <summary>The items passing the resolution choice (and, when <paramref name="folders"/> is given, in those folders).</summary>
    private IEnumerable<CollectionItem> Items(S100CatalogueFilter? folders = null)
    {
        var purpose = _selectedResolution?.Value;
        return (_catalogue?.Items ?? []).Where(i =>
            (purpose is null || string.Equals(
                i.Properties.GetValueOrDefault(RemoteS100Catalogue.NavigationPurposeProperty), purpose, StringComparison.OrdinalIgnoreCase))
            && (folders is null || folders.MatchesFolder(RemoteS100Catalogue.FolderOf(i))));
    }

    /// <summary>The known size of <paramref name="items"/>, or null while any is unknown.</summary>
    private long? SizeOf(IEnumerable<CollectionItem> items)
    {
        long total = 0;
        foreach (var item in items)
        {
            if (item.Location is not RemoteItemLocation remote || !_sizes.TryGetValue(remote.Uri, out var entry))
                return null;
            total += entry.SizeBytes;
        }

        return total;
    }

    /// <summary>"164 tiles · 0,55 GB", "… · sizing…", or just the count when sizes cannot be listed.</summary>
    private string TilesDetail(int count, long? bytes, bool sizing) =>
        bytes is { } b ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_TilesSizeFormat"), count, LibraryTextFormat.Bytes(b))
        : sizing && !_sizesUnavailable ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_TilesSizingFormat"), count)
        : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_TilesFormat"), count);

    private void RefreshAreaDetails()
    {
        if (_catalogue is null)
            return;

        var byFolder = Items().GroupBy(RemoteS100Catalogue.FolderOf, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var option in Choices)
        {
            var items = byFolder.GetValueOrDefault(option.Value) ?? [];
            option.Detail = TilesDetail(items.Length, SizeOf(items), sizing: true);
        }
    }

    private static string JoinAnd(IReadOnlyList<string> parts) => parts.Count switch
    {
        1 => parts[0],
        _ => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_JoinAndFormat"), string.Join(", ", parts.Take(parts.Count - 1)), parts[^1]),
    };
}
