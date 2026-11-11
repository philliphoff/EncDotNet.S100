using System.Globalization;
using EncDotNet.S100.Collections.Library;
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
    private S100CatalogueScope? S100Scope => Scope as S100CatalogueScope;

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

    /// <summary>Raises what follows a remote S-100 catalogue's regions, and starts sizing the region shown.</summary>
    private void RaiseRegionsChanged()
    {
        if (!IsS100Catalogue)
            return;

        OnPropertyChanged(nameof(IsRegionPicker));
        OnPropertyChanged(nameof(Resolutions));
        OnPropertyChanged(nameof(HasResolutions));
        OnPropertyChanged(nameof(SelectedResolution));
        OnPropertyChanged(nameof(RegionsHeader));
        OnPropertyChanged(nameof(AreasHeader));
        _ = S100Scope?.SizeGroupAsync(_selectedFacetGroup?.Source);
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
