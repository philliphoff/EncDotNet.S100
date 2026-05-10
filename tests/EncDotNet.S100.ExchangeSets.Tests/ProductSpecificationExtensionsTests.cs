using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class ProductSpecificationExtensionsTests
{
    [Fact]
    public void TryToSpecRef_uses_Name_and_Version_when_both_present()
    {
        var ps = new ProductSpecification
        {
            Name = "S-101",
            Version = "1.2.0",
        };

        Assert.True(ps.TryToSpecRef(out var spec));
        Assert.Equal(new SpecRef("S-101", new SpecVersion(1, 2, 0)), spec);
    }

    [Fact]
    public void TryToSpecRef_normalises_loosely_typed_Name()
    {
        var ps = new ProductSpecification
        {
            Name = "s101",
            Version = "1.2.0",
        };

        Assert.True(ps.TryToSpecRef(out var spec));
        Assert.Equal("S-101", spec.Name);
    }

    [Fact]
    public void TryToSpecRef_falls_back_to_ProductIdentifier()
    {
        var ps = new ProductSpecification
        {
            ProductIdentifier = "INT.IHO.S-101.1.2.0",
        };

        Assert.True(ps.TryToSpecRef(out var spec));
        Assert.Equal(new SpecRef("S-101", new SpecVersion(1, 2, 0)), spec);
    }

    [Fact]
    public void TryToSpecRef_prefers_Name_Version_over_ProductIdentifier()
    {
        // When both shapes are present and disagree, Name+Version wins
        // (the more specific declaration).
        var ps = new ProductSpecification
        {
            Name = "S-101",
            Version = "2.0.0",
            ProductIdentifier = "INT.IHO.S-101.1.0.0",
        };

        Assert.True(ps.TryToSpecRef(out var spec));
        Assert.Equal(new SpecVersion(2, 0, 0), spec.Edition);
    }

    [Fact]
    public void TryToSpecRef_returns_false_when_no_data()
    {
        var ps = new ProductSpecification();
        Assert.False(ps.TryToSpecRef(out _));
    }

    [Fact]
    public void TryToSpecRef_returns_false_for_unparseable_inputs()
    {
        var ps = new ProductSpecification
        {
            Name = "garbage",
            Version = "not-a-version",
            ProductIdentifier = "INT.IHO.garbage",
        };

        Assert.False(ps.TryToSpecRef(out _));
    }

    [Fact]
    public void ToSpecRef_throws_on_unresolvable_input()
    {
        var ps = new ProductSpecification();
        Assert.Throws<FormatException>(() => ps.ToSpecRef());
    }
}
