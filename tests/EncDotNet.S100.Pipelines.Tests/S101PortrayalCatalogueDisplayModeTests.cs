using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Integration test verifying that the bundled S-101 portrayal
/// catalogue declares S-100 Part 9 §11.7 display modes with non-empty
/// viewing-group membership and that the per-spec catalogue's
/// <see cref="DisplayModeController"/> drives its
/// <see cref="ViewingGroupController"/> when the active mode changes.
/// </summary>
public class S101PortrayalCatalogueDisplayModeTests
{
    [Fact]
    public async Task DisplayBase_ResolvedMembership_NonEmpty_AndDrivesViewingGroups()
    {
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var catalogue = new S101PortrayalCatalogue(provider);

        // The bundled S-101 PC must declare the three ECDIS modes.
        var modeIds = provider.Catalogue.DisplayModes.Select(m => m.Id).ToList();
        Assert.Contains("DisplayBase", modeIds);
        Assert.Contains("StandardDisplay", modeIds);
        Assert.Contains("OtherInformation", modeIds);

        // Switching to DisplayBase resolves to a non-empty VG set.
        catalogue.DisplayModes.SetActive("DisplayBase");
        var baseSet = DisplayModeMembership.Resolve(provider.Catalogue, "DisplayBase");
        Assert.NotEmpty(baseSet);

        // Catalogue's ViewingGroupController matches resolved membership.
        Assert.Equal(baseSet, catalogue.ViewingGroups.ActiveModeMembership);

        // A VG inside DisplayBase is visible; a VG that's only in
        // StandardDisplay is hidden under DisplayBase.
        var standardSet = DisplayModeMembership.Resolve(provider.Catalogue, "StandardDisplay");
        var onlyInStandard = standardSet.Except(baseSet).FirstOrDefault();
        if (onlyInStandard != 0)
        {
            Assert.True(catalogue.ViewingGroups.IsVisible(baseSet.First()));
            Assert.False(catalogue.ViewingGroups.IsVisible(onlyInStandard));
        }

        // Switching back to "no mode" restores all-visible.
        catalogue.DisplayModes.SetActive(null);
        Assert.Null(catalogue.ViewingGroups.ActiveModeMembership);
        Assert.True(catalogue.ViewingGroups.IsVisible(99999));
    }

    [Fact]
    public async Task StandardDisplay_IsSupersetOf_DisplayBase()
    {
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);

        var baseSet = DisplayModeMembership.Resolve(provider.Catalogue, "DisplayBase");
        var stdSet = DisplayModeMembership.Resolve(provider.Catalogue, "StandardDisplay");

        Assert.NotEmpty(baseSet);
        Assert.NotEmpty(stdSet);
        Assert.True(baseSet.IsSubsetOf(stdSet),
            "DisplayBase membership should be a subset of StandardDisplay (S-100 Part 9 §11.7).");
    }
}
