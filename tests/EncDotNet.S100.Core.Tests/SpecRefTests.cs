using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class SpecRefTests
{
    [Theory]
    [InlineData("S-101", 1, 2, 0)]
    [InlineData("s-101", 1, 2, 0)]
    [InlineData("S101", 1, 2, 0)]
    [InlineData("s101", 1, 2, 0)]
    [InlineData("  S-101  ", 1, 2, 0)]
    public void Constructor_normalises_name_to_canonical_form(string raw, int major, int minor, int clarification)
    {
        var spec = new SpecRef(raw, new SpecVersion(major, minor, clarification));
        Assert.Equal("S-101", spec.Name);
        Assert.Equal(new SpecVersion(major, minor, clarification), spec.Edition);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("S-")]
    [InlineData("S-1")]
    [InlineData("S-1234")]
    public void Constructor_rejects_unrecognised_name(string raw)
    {
        Assert.Throws<FormatException>(() => new SpecRef(raw, new SpecVersion(1, 0, 0)));
    }

    [Theory]
    [InlineData("S-101/1.2.0",   "S-101", 1, 2, 0)]
    [InlineData("S-102@3.0.0",   "S-102", 3, 0, 0)]
    [InlineData("s101/1.2",      "S-101", 1, 2, 0)]
    [InlineData("INT.IHO.S-101.1.2.0", "S-101", 1, 2, 0)]
    [InlineData("INT.IHO.S101.2.0.0",  "S-101", 2, 0, 0)]
    public void Parse_accepts_recognised_forms(string input, string expectedName, int major, int minor, int clar)
    {
        var spec = SpecRef.Parse(input);
        Assert.Equal(expectedName, spec.Name);
        Assert.Equal(new SpecVersion(major, minor, clar), spec.Edition);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("S-101")]
    [InlineData("/1.2.0")]
    [InlineData("S-101/")]
    [InlineData("garbage/1.2.0")]
    [InlineData("S-101/not-a-version")]
    public void TryParse_rejects_invalid_input(string input)
    {
        Assert.False(SpecRef.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_rejects_null()
    {
        Assert.False(SpecRef.TryParse(null, out _));
    }

    [Fact]
    public void ToString_round_trips_through_Parse()
    {
        var spec = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        Assert.Equal("S-101/1.2.0", spec.ToString());
        Assert.Equal(spec, SpecRef.Parse(spec.ToString()));
    }

    [Fact]
    public void Equality_normalises_Name_so_case_and_dash_dont_matter()
    {
        var a = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var b = new SpecRef("s101",  new SpecVersion(1, 2, 0));
        var c = new SpecRef("S101",  new SpecVersion(1, 2, 0));

        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.GetHashCode(), c.GetHashCode());
    }

    [Fact]
    public void Equality_distinguishes_by_edition()
    {
        var a = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var b = new SpecRef("S-101", new SpecVersion(2, 0, 0));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_distinguishes_by_name()
    {
        var a = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var b = new SpecRef("S-102", new SpecVersion(1, 2, 0));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SpecRef_works_as_dictionary_key()
    {
        var dict = new Dictionary<SpecRef, string>
        {
            [new SpecRef("S-101", new SpecVersion(1, 2, 0))] = "old",
            [new SpecRef("S-101", new SpecVersion(2, 0, 0))] = "new",
            [new SpecRef("S-102", new SpecVersion(3, 0, 0))] = "bath",
        };

        Assert.Equal("old",  dict[new SpecRef("S-101", new SpecVersion(1, 2, 0))]);
        Assert.Equal("new",  dict[new SpecRef("s101",  new SpecVersion(2, 0, 0))]);
        Assert.Equal("bath", dict[new SpecRef("S-102", new SpecVersion(3, 0, 0))]);
    }
}
