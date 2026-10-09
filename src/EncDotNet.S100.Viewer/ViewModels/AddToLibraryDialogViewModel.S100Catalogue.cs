using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

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

    private S100CatalogueScope? S100Scope => _scope as S100CatalogueScope;

    /// <summary>True when adding a remote S-100 catalogue.</summary>
    public bool IsS100Catalogue => _kind == LibrarySourceKind.S100Catalogue;

    /// <summary>True when the facets are regions (left) and their areas (right) rather than tabs.</summary>
    public bool IsRegionPicker => IsS100Catalogue && _facetGroups.Count > 1;

    /// <summary>The resolution choices: all, then each navigation purpose, finest first.</summary>
    public IReadOnlyList<LibraryResolution> Resolutions => S100Scope?.Resolutions ?? [];

    /// <summary>True when the catalogue has more than one navigation purpose to choose between.</summary>
    public bool HasResolutions => S100Scope?.HasResolutions == true;

    /// <summary>The chosen resolution; it applies to every area.</summary>
    public LibraryResolution? SelectedResolution
    {
        get => S100Scope?.SelectedResolution;
        set
        {
            if (value is null || S100Scope is not { } scope || value == scope.SelectedResolution)
                return;

            scope.SelectedResolution = value;
            OnPropertyChanged();
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
    public S100CatalogueFilter CurrentS100CatalogueFilter => S100Scope?.CurrentFilter ?? new S100CatalogueFilter();

    /// <summary>Shows a remote S-100 catalogue's regions once its scope has read it, and starts sizing the first.</summary>
    private void ShowS100Regions(S100CatalogueScope scope)
    {
        foreach (var option in AllOptions)
            option.PropertyChanged -= OnFacetChanged;
        _facetGroups = scope.Groups.Select(r =>
        {
            var group = new FacetGroupViewModel(r.Title, [.. r.Options]) { Key = r.Key, Source = r };
            foreach (var option in group.Options)
                option.PropertyChanged += OnFacetChanged;
            return group;
        }).ToArray();
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
        _ = scope.SizeGroupAsync(_selectedFacetGroup?.Source);
    }

    /// <summary>Called when another region is opened.</summary>
    private void OnRegionShown()
    {
        if (!IsS100Catalogue)
            return;

        OnPropertyChanged(nameof(AreasHeader));
        _ = S100Scope?.SizeGroupAsync(_selectedFacetGroup?.Source);
    }
}
