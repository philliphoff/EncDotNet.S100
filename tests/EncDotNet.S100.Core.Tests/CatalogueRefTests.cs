using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class CatalogueRefTests
{
    [Fact]
    public void Constructor_normalises_name_and_preserves_version()
    {
        var c = new CatalogueRef("s101", new SpecVersion(2, 0, 0));
        Assert.Equal("S-101", c.Name);
        Assert.Equal(new SpecVersion(2, 0, 0), c.Version);
    }

    [Theory]
    [InlineData("S-101@2.0.0", "S-101", 2, 0, 0)]
    [InlineData("S-101/2.0.0", "S-101", 2, 0, 0)]
    [InlineData("s101@1.2.1",  "S-101", 1, 2, 1)]
    public void Parse_accepts_recognised_forms(string input, string name, int major, int minor, int clar)
    {
        var c = CatalogueRef.Parse(input);
        Assert.Equal(name, c.Name);
        Assert.Equal(new SpecVersion(major, minor, clar), c.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-101")]
    [InlineData("@2.0.0")]
    [InlineData("S-101@")]
    [InlineData("garbage@2.0.0")]
    public void TryParse_rejects_invalid_input(string input)
    {
        Assert.False(CatalogueRef.TryParse(input, out _));
    }

    [Fact]
    public void ToString_uses_at_separator_to_distinguish_from_SpecRef()
    {
        var c = new CatalogueRef("S-101", new SpecVersion(2, 0, 0));
        Assert.Equal("S-101@2.0.0", c.ToString());
    }

    [Fact]
    public void CatalogueRef_works_as_dictionary_key()
    {
        var dict = new Dictionary<CatalogueRef, int>
        {
            [new CatalogueRef("S-101", new SpecVersion(2, 0, 0))] = 1,
            [new CatalogueRef("S-101", new SpecVersion(1, 0, 0))] = 2,
            [new CatalogueRef("S-411", new SpecVersion(1, 2, 1))] = 3,
        };

        Assert.Equal(1, dict[new CatalogueRef("s101", new SpecVersion(2, 0, 0))]);
        Assert.Equal(2, dict[new CatalogueRef("S-101", new SpecVersion(1, 0, 0))]);
        Assert.Equal(3, dict[new CatalogueRef("S-411", new SpecVersion(1, 2, 1))]);
    }
}
