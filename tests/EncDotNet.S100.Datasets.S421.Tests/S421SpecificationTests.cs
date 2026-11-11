using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>
/// Tests verifying that the S-421 Feature Catalogue and Portrayal Catalogue
/// are bundled in the <c>EncDotNet.S100.Specifications</c> assembly.
/// </summary>
public class S421SpecificationTests
{
    [Fact]
    public void S421_IsListedAsAvailableSpec()
    {
        Assert.Contains("S-421", Specification.AvailableSpecs);
    }

    [Fact]
    public void S421_FeatureCatalogue_IsBundled()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-421");
        Assert.NotNull(stream);
    }

    [Fact]
    public void S421_PortrayalCatalogue_IsBundled()
    {
        Assert.True(Specification.HasPortrayalCatalogue("S-421"));
    }
}
