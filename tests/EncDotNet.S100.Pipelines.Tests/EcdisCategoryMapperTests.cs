using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Cross-spec ECDIS category mapping (S-100 Part 9 §11.7).
/// </summary>
public class EcdisCategoryMapperTests
{
    private static IReadOnlySet<string> Modes(params string[] ids) =>
        new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void All_AlwaysMapsToNull()
    {
        Assert.Null(EcdisCategoryMapper.Map("S-101", EcdisDisplayCategory.All,
            Modes("DisplayBase", "StandardDisplay", "OtherInformation")));
        Assert.Null(EcdisCategoryMapper.Map("S-122", EcdisDisplayCategory.All, Modes("StandardDisplay")));
    }

    [Theory]
    [InlineData(EcdisDisplayCategory.DisplayBase, "DisplayBase")]
    [InlineData(EcdisDisplayCategory.Standard, "StandardDisplay")]
    [InlineData(EcdisDisplayCategory.OtherInformation, "OtherInformation")]
    public void S101_AllCategoriesResolveToCanonicalNames(EcdisDisplayCategory cat, string expected)
    {
        var modes = Modes("DisplayBase", "StandardDisplay", "OtherInformation");
        Assert.Equal(expected, EcdisCategoryMapper.Map("S-101", cat, modes));
    }

    [Fact]
    public void SpecWithOnlyStandard_DisplayBaseAndOther_FallBackToNull()
    {
        var modes = Modes("StandardDisplay");

        Assert.Null(EcdisCategoryMapper.Map("S-122", EcdisDisplayCategory.DisplayBase, modes));
        Assert.Null(EcdisCategoryMapper.Map("S-122", EcdisDisplayCategory.OtherInformation, modes));
        Assert.Equal("StandardDisplay",
            EcdisCategoryMapper.Map("S-122", EcdisDisplayCategory.Standard, modes));
    }

    [Fact]
    public void SpecWithNoModes_AllCategoriesFallBackToNull()
    {
        var none = Modes();

        Assert.Null(EcdisCategoryMapper.Map("S-411", EcdisDisplayCategory.DisplayBase, none));
        Assert.Null(EcdisCategoryMapper.Map("S-411", EcdisDisplayCategory.Standard, none));
        Assert.Null(EcdisCategoryMapper.Map("S-411", EcdisDisplayCategory.OtherInformation, none));
        Assert.Null(EcdisCategoryMapper.Map("S-411", EcdisDisplayCategory.All, none));
    }
}

public class EcdisDisplayExtensionsTests
{
    [Fact]
    public async Task ApplyTo_ChangesActiveModeAndUserOverridesOnS101Catalogue()
    {
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var catalogue = new EncDotNet.S100.Datasets.S101.S101PortrayalCatalogue(provider);

        var settings = new EcdisDisplaySettings
        {
            Category = EcdisDisplayCategory.DisplayBase,
            HiddenViewingGroups = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["S-101"] = new HashSet<int> { 11010 },
            },
        };

        settings.ApplyTo(catalogue);

        Assert.Equal("DisplayBase", catalogue.DisplayModes.ActiveDisplayModeId);
        Assert.NotNull(catalogue.ViewingGroups.ActiveModeMembership);
        // The user override wins over the mode membership.
        Assert.False(catalogue.ViewingGroups.IsVisible(11010));
    }

    [Fact]
    public async Task ApplyTo_AllCategory_ClearsModeFilter()
    {
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var catalogue = new EncDotNet.S100.Datasets.S101.S101PortrayalCatalogue(provider);

        // Pre-set to DisplayBase to prove ApplyTo can return us to "All".
        catalogue.DisplayModes.SetActive("DisplayBase");
        Assert.NotNull(catalogue.ViewingGroups.ActiveModeMembership);

        new EcdisDisplaySettings { Category = EcdisDisplayCategory.All }.ApplyTo(catalogue);

        Assert.Null(catalogue.DisplayModes.ActiveDisplayModeId);
        Assert.Null(catalogue.ViewingGroups.ActiveModeMembership);
    }
}
