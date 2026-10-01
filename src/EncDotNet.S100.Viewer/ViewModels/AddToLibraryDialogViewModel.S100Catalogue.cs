using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>One choice of the resolution segmented control: a navigation purpose, or all of them.</summary>
/// <param name="Value">The <c>navigationPurpose</c>, or <see langword="null"/> for all.</param>
/// <param name="Label">"Port 4 m", or "Port and Transit" for all.</param>
internal sealed record ResolutionOptionViewModel(string? Value, string Label);

/// <summary>
/// The remote S-100 catalogue part of the "Add to Library" dialog (issue
/// #685; NOAA's S-102 on AWS): regions on the left, each region's areas on the
/// right with tile counts and sizes, and a resolution filter that applies to
/// every area. Sizes come from listing the bucket, one region at a time, when
/// the region is first opened.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    private readonly Func<Uri, CancellationToken, Task<RemoteS100Catalogue>>? _loadS100Catalogue;
    private readonly Func<RemoteS100Catalogue, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<Uri, S3Object>?>>? _listS100Folders;
    private readonly Dictionary<Uri, S3Object> _s100Sizes = [];
    private readonly HashSet<string> _sizedRegions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sizingRegions = new(StringComparer.Ordinal);
    private RemoteS100Catalogue? _s100Catalogue;
    private bool _s100SizesUnavailable;
    private IReadOnlyList<ResolutionOptionViewModel> _resolutions = [];
    private ResolutionOptionViewModel? _selectedResolution;

    /// <summary>True when adding a remote S-100 catalogue.</summary>
    public bool IsS100Catalogue => _kind == AddToLibraryKind.S100Catalogue;

    /// <summary>True when the facets are regions (left) and their areas (right) rather than tabs.</summary>
    public bool IsRegionPicker => IsS100Catalogue && _facetGroups.Count > 1;

    /// <summary>The resolution choices: all, then each navigation purpose, finest first.</summary>
    public IReadOnlyList<ResolutionOptionViewModel> Resolutions => _resolutions;

    /// <summary>True when the catalogue has more than one navigation purpose to choose between.</summary>
    public bool HasResolutions => _resolutions.Count > 2;

    /// <summary>The chosen resolution; it applies to every area.</summary>
    public ResolutionOptionViewModel? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (value is null || !SetProperty(ref _selectedResolution, value))
                return;

            RefreshAreaDetails();
            UpdateSelection();
            OnScopeChanged();
        }
    }

    /// <summary>"REGIONS · 14", over the region list.</summary>
    public string RegionsHeader => string.Format(
        CultureInfo.CurrentCulture, Strings.Wizard_RegionsHeaderFormat, _facetGroups.Count).ToUpper(CultureInfo.CurrentCulture);

    /// <summary>"NORTHEAST · 9 AREAS", over the area list.</summary>
    public string AreasHeader => _selectedFacetGroup is { } region
        ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_AreasHeaderFormat, region.Title, region.AllOptions.Count())
            .ToUpper(CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>The filter for the current region, area and resolution choices.</summary>
    public S100CatalogueFilter CurrentS100CatalogueFilter
    {
        get
        {
            // A region whose areas are all ticked is kept as the region, so areas
            // the publisher adds to it later are included too.
            var folders = new List<string>();
            if (!_includeAll)
            {
                foreach (var region in _facetGroups)
                {
                    var areas = region.AllOptions.ToArray();
                    if (areas.Length > 1 && areas.All(a => a.IsSelected) && region.Key is { Length: > 0 } key)
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

    private async Task LoadS100CatalogueAsync(CancellationToken cancellationToken)
    {
        if (_loadS100Catalogue is null)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _s100Catalogue = await _loadS100Catalogue(CatalogUri, cancellationToken).ConfigureAwait(true);
            SetCatalogueDate(_s100Catalogue.IssuedAt is { } issued ? DateOnly.FromDateTime(issued.UtcDateTime) : null);

            var regions = S100CatalogueFacets.Regions(_s100Catalogue.Items);
            foreach (var option in AllOptions)
                option.PropertyChanged -= OnFacetChanged;
            _facetGroups = regions.Select(r =>
            {
                var group = new FacetGroupViewModel(r.Name, [.. r.Areas.Select(a => new FacetOptionViewModel(
                    a.Value, RemoteS100Catalogue.FolderName(a.Value), string.Empty))])
                { Key = r.Folder };
                foreach (var option in group.Options)
                    option.PropertyChanged += OnFacetChanged;
                return group;
            }).ToArray();

            var purposes = S100CatalogueFacets.NavigationPurposes(_s100Catalogue.Items);
            _resolutions =
            [
                new(null, purposes.Count is > 0 and <= 3
                    ? JoinAnd(purposes.Select(p => PurposeName(p.Value)).ToArray())
                    : Strings.Wizard_ResolutionAll),
                .. purposes.Select(p => new ResolutionOptionViewModel(p.Value, PurposeLabel(p))),
            ];
            _selectedResolution = _resolutions[0];
            _selectedFacetGroup = _facetGroups.FirstOrDefault();

            OnPropertyChanged(nameof(FacetGroups));
            OnPropertyChanged(nameof(HasFacetTabs));
            OnPropertyChanged(nameof(ShowsGroupTitle));
            OnPropertyChanged(nameof(IsRegionPicker));
            OnPropertyChanged(nameof(Resolutions));
            OnPropertyChanged(nameof(HasResolutions));
            OnPropertyChanged(nameof(SelectedResolution));
            OnPropertyChanged(nameof(SelectedFacetGroup));
            OnPropertyChanged(nameof(RegionsHeader));
            OnPropertyChanged(nameof(AreasHeader));
            RefreshAreaDetails();
            UpdateSelection();
            _ = SizeRegionAsync(_selectedFacetGroup);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ResetS100Catalogue()
    {
        _s100Catalogue = null;
        _s100Sizes.Clear();
        _sizedRegions.Clear();
        _sizingRegions.Clear();
        _s100SizesUnavailable = false;
        _resolutions = [];
        _selectedResolution = null;
    }

    /// <summary>Lists a region's files for their sizes, once; areas show "sizing…" until then.</summary>
    private async Task SizeRegionAsync(FacetGroupViewModel? region)
    {
        if (region?.Key is not { } folder || _s100Catalogue is not { } catalogue || _listS100Folders is null
            || _s100SizesUnavailable || _sizedRegions.Contains(folder) || !_sizingRegions.Add(folder))
        {
            return;
        }

        try
        {
            var listed = await _listS100Folders(catalogue, [folder], CancellationToken.None).ConfigureAwait(true);
            if (listed is null)
            {
                _s100SizesUnavailable = true;
            }
            else
            {
                foreach (var (uri, entry) in listed)
                    _s100Sizes[uri] = entry;
                _sizedRegions.Add(folder);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // Sizes stay unknown; the region is listed again when next opened.
        }
        finally
        {
            _sizingRegions.Remove(folder);
        }

        if (ReferenceEquals(catalogue, _s100Catalogue))
        {
            RefreshAreaDetails();
            UpdateSelection();
            OnScopeChanged();
        }
    }

    /// <summary>Called when another region is opened.</summary>
    private void OnRegionShown()
    {
        if (!IsS100Catalogue)
            return;

        OnPropertyChanged(nameof(AreasHeader));
        _ = SizeRegionAsync(_selectedFacetGroup);
    }

    /// <summary>The items passing the resolution choice (and, when <paramref name="folders"/> is given, in those folders).</summary>
    private IEnumerable<CollectionItem> S100Items(S100CatalogueFilter? folders = null)
    {
        var purpose = _selectedResolution?.Value;
        return (_s100Catalogue?.Items ?? []).Where(i =>
            (purpose is null || string.Equals(
                i.Properties.GetValueOrDefault(RemoteS100Catalogue.NavigationPurposeProperty), purpose, StringComparison.OrdinalIgnoreCase))
            && (folders is null || folders.MatchesFolder(RemoteS100Catalogue.FolderOf(i))));
    }

    /// <summary>The known size of <paramref name="items"/>, or <see langword="null"/> while any is unknown.</summary>
    private long? SizeOf(IEnumerable<CollectionItem> items)
    {
        long total = 0;
        foreach (var item in items)
        {
            if (item.Location is not RemoteItemLocation remote || !_s100Sizes.TryGetValue(remote.Uri, out var entry))
                return null;
            total += entry.SizeBytes;
        }

        return total;
    }

    /// <summary>"164 tiles · 0,55 GB", "… · sizing…", or just the count when sizes cannot be listed.</summary>
    private string TilesDetail(int count, long? bytes, bool sizing) =>
        bytes is { } b ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_TilesSizeFormat, count, LibraryItemViewModel.FormatBytes(b))
        : sizing && !_s100SizesUnavailable ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_TilesSizingFormat, count)
        : string.Format(CultureInfo.CurrentCulture, Strings.Wizard_TilesFormat, count);

    private void RefreshAreaDetails()
    {
        if (_s100Catalogue is null)
            return;

        var byFolder = S100Items().GroupBy(RemoteS100Catalogue.FolderOf, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var option in AllOptions)
        {
            var items = byFolder.GetValueOrDefault(option.Value) ?? [];
            option.Detail = TilesDetail(items.Length, SizeOf(items), sizing: true);
        }
    }

    private void UpdateS100CatalogueSelection()
    {
        if (_s100Catalogue is null)
            return;

        var filter = CurrentS100CatalogueFilter;
        var areas = AllOptions.Count(o => o.IsSelected);
        var selected = S100Items(_includeAll ? null : filter).ToArray();
        var size = SizeOf(selected);
        var tiles = TilesDetail(selected.Length, size, sizing: true);
        SelectionSummary = _includeAll || areas == 0
            ? tiles
            : string.Format(CultureInfo.CurrentCulture, Strings.Wizard_AreasTilesFormat, areas, tiles);

        FollowSelectionInName(filter.IsUnscoped, () => DescribeS100Selection(filter)!);
    }

    /// <summary>"5 209 tiles · 17,3 GB online" for the whole catalogue at the chosen resolution.</summary>
    private string S100EverythingSummary
    {
        get
        {
            var items = S100Items().ToArray();
            return SizeOf(items) is { } bytes
                ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_TilesOnlineFormat, items.Length, LibraryItemViewModel.FormatBytes(bytes))
                : string.Format(CultureInfo.CurrentCulture, Strings.Wizard_TilesFormat, items.Length);
        }
    }

    /// <summary>The ticked areas and the resolution ("Boston, Penobscot Bay · Port 4 m"), or <see langword="null"/> for everything.</summary>
    private string? DescribeS100Selection(S100CatalogueFilter filter)
    {
        var areas = _includeAll ? [] : AllOptions.Where(o => o.IsSelected).Select(o => o.Label).ToArray();
        var where = areas.Length switch
        {
            0 => null,
            <= 3 => string.Join(", ", areas),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", areas.Take(2)), areas.Length - 2),
        };
        var resolution = filter.NavigationPurposes.Count > 0 ? _selectedResolution?.Label : null;
        return (where, resolution) switch
        {
            (null, null) => null,
            (null, { } r) => r,
            ({ } w, null) => w,
            ({ } w, { } r) => $"{w} · {r}",
        };
    }

    /// <summary>"Port 4 m": the purpose and its usual grid resolution.</summary>
    private static string PurposeLabel(S100CataloguePurpose purpose) => purpose.GridResolution is { } metres
        ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_PurposeResolutionFormat, PurposeName(purpose.Value), metres)
        : PurposeName(purpose.Value);

    /// <summary>The catalogue's purpose code as a name ("port" → "Port").</summary>
    private static string PurposeName(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..];

    private static string JoinAnd(IReadOnlyList<string> parts) => parts.Count switch
    {
        1 => parts[0],
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Wizard_JoinAndFormat, string.Join(", ", parts.Take(parts.Count - 1)), parts[^1]),
    };
}
