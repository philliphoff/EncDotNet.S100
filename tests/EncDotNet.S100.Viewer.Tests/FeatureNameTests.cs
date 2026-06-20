using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class FeatureNameTests
{
    private static PickAttribute Leaf(string code, string value, string? name = null) =>
        new() { Code = code, Name = name, RawValue = value, DisplayValue = null, Children = [] };

    [Fact]
    public void Derive_SimpleName_ReturnsValue()
    {
        var name = FeatureName.Derive([Leaf("name", "Number 10")]);
        Assert.Equal("Number 10", name);
    }

    [Fact]
    public void Derive_ComplexFeatureName_ReadsChildName()
    {
        var complex = new PickAttribute
        {
            Code = "featureName",
            Name = "Feature Name",
            RawValue = "",
            DisplayValue = null,
            Children = [Leaf("name", "Number 10"), Leaf("language", "eng")],
        };

        Assert.Equal("Number 10", FeatureName.Derive([complex]));
    }

    [Fact]
    public void Derive_PrefersFeatureNameOverSimpleName()
    {
        var attrs = new[]
        {
            Leaf("name", "Fallback"),
            new PickAttribute
            {
                Code = "featureName",
                RawValue = "",
                Children = [Leaf("name", "Preferred")],
            },
        };

        Assert.Equal("Preferred", FeatureName.Derive(attrs));
    }

    [Fact]
    public void Derive_NoNameAttribute_ReturnsNull()
    {
        Assert.Null(FeatureName.Derive([Leaf("DRVAL1", "10.0")]));
    }

    [Fact]
    public void Derive_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(FeatureName.Derive([]));
        Assert.Null(FeatureName.Derive(null));
    }

    [Fact]
    public void Derive_BlankValue_IsIgnored()
    {
        Assert.Null(FeatureName.Derive([Leaf("name", "   ")]));
    }
}
