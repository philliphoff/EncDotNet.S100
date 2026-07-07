using System.Linq;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for the curated S-101 subsection grouping of ECDIS
/// viewing-group checkboxes (<see cref="EcdisSpecViewModel.Sections"/>)
/// and the section metadata exposed by
/// <see cref="EcdisLabelOverrideProvider"/>.
/// </summary>
public class EcdisViewingGroupSectionTests
{
    private static PortrayalCatalogue CatalogueWith(params string[] viewingGroupIds) => new()
    {
        ProductId = "S-101",
        Version = "1.0",
        ViewingGroups = viewingGroupIds
            .Select(id => new ViewingGroup { Id = id, Description = new Description { Name = id } })
            .ToList(),
    };

    [Fact]
    public void Provider_ExposesS101Sections_InDeclaredOrder()
    {
        var provider = new EcdisLabelOverrideProvider();

        var sections = provider.GetSections("S-101");

        Assert.NotEmpty(sections);
        var ids = sections.Select(s => s.Id).ToList();
        // 'land' is declared before 'depths', which is declared before 'selectors'.
        Assert.True(ids.IndexOf("land") < ids.IndexOf("depths"));
        Assert.True(ids.IndexOf("depths") < ids.IndexOf("selectors"));
    }

    [Fact]
    public void Provider_ResolvesSectionIdForGroup()
    {
        var provider = new EcdisLabelOverrideProvider();

        Assert.True(provider.TryGetSectionId("S-101", 90000, out var shallow));
        Assert.Equal("selectors", shallow);

        Assert.True(provider.TryGetSectionId("S-101", 13010, out var safety));
        Assert.Equal("depths", safety);
    }

    [Fact]
    public void Provider_ReturnsNoSections_ForSpecWithoutCuratedSections()
    {
        var provider = new EcdisLabelOverrideProvider();
        Assert.Empty(provider.GetSections("S-124"));
    }

    [Fact]
    public void SpecViewModel_GroupsIntoOrderedSections()
    {
        var catalogue = CatalogueWith("33010", "13010", "12010");

        var vm = new EcdisSpecViewModel(
            new EcdisDisplayState(), "S-101", catalogue, new EcdisLabelOverrideProvider());

        var titles = vm.Sections.Select(s => s.Title).ToList();
        Assert.Equal(new[] { "Land & coastline", "Depths, contours & soundings" }, titles);

        // Within the depths section, groups are sorted by display label:
        // "Safety contour" (13010) precedes "Soundings" (33010).
        var depths = vm.Sections.Single(s => s.Title == "Depths, contours & soundings");
        Assert.Equal(new[] { "Safety contour", "Soundings" },
            depths.ViewingGroups.Select(g => g.DisplayLabel).ToArray());
    }

    [Fact]
    public void SpecViewModel_PutsUnmappedGroupsInOther()
    {
        var catalogue = CatalogueWith("13010", "99999");

        var vm = new EcdisSpecViewModel(
            new EcdisDisplayState(), "S-101", catalogue, new EcdisLabelOverrideProvider());

        var other = vm.Sections.Last();
        Assert.Equal("Other", other.Title);
        Assert.Contains(other.ViewingGroups, g => g.Id == 99999);
    }

    [Fact]
    public void SpecViewModel_FallsBackToSingleUntitledSection_WhenSpecHasNoSections()
    {
        var catalogue = CatalogueWith("31010", "31020");

        var vm = new EcdisSpecViewModel(
            new EcdisDisplayState(), "S-124", catalogue, new EcdisLabelOverrideProvider());

        var section = Assert.Single(vm.Sections);
        Assert.Null(section.Title);
        Assert.False(section.HasTitle);
        Assert.Equal(2, section.ViewingGroups.Count);
    }

    [Fact]
    public void SpecViewModel_HasViewingGroups_TrueWhenGroupsPresent()
    {
        var catalogue = CatalogueWith("31010", "31020");

        var vm = new EcdisSpecViewModel(
            new EcdisDisplayState(), "S-124", catalogue, new EcdisLabelOverrideProvider());

        Assert.True(vm.HasViewingGroups);
    }

    [Fact]
    public void SpecViewModel_HasViewingGroups_FalseWhenCatalogueDeclaresNone()
    {
        // S-411's display is driven by the sea-ice display-mode selector, not
        // viewing-group filters; its catalogue declares no viewing groups, so
        // the per-spec panel entry is hidden.
        var catalogue = CatalogueWith();

        var vm = new EcdisSpecViewModel(
            new EcdisDisplayState(), "S-411", catalogue, new EcdisLabelOverrideProvider());

        Assert.False(vm.HasViewingGroups);
    }
}
