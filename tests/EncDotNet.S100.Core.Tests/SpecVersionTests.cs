using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class SpecVersionTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("2.0.0", 2, 0, 0)]
    [InlineData("1.2.1", 1, 2, 1)]
    [InlineData("3.0.0", 3, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("5", 5, 0, 0)]
    [InlineData("  1.2.0  ", 1, 2, 0)]
    public void Parse_returns_expected_components(string input, int major, int minor, int clarification)
    {
        var v = SpecVersion.Parse(input);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(clarification, v.Clarification);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2.3.4")]
    [InlineData("1.-2.0")]
    [InlineData("1.a.0")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0-rc1")]
    public void TryParse_rejects_invalid_input(string input)
    {
        Assert.False(SpecVersion.TryParse(input, out _));
        Assert.Throws<FormatException>(() => SpecVersion.Parse(input));
    }

    [Fact]
    public void TryParse_rejects_null()
    {
        Assert.False(SpecVersion.TryParse(null, out _));
    }

    [Fact]
    public void Constructor_rejects_negative_components()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecVersion(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecVersion(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecVersion(0, 0, -1));
    }

    [Fact]
    public void ToString_round_trips_through_parse()
    {
        var v = new SpecVersion(1, 2, 3);
        Assert.Equal("1.2.3", v.ToString());
        Assert.Equal(v, SpecVersion.Parse(v.ToString()));
    }

    [Fact]
    public void Equality_compares_all_three_components()
    {
        Assert.Equal(new SpecVersion(1, 2, 0), new SpecVersion(1, 2, 0));
        Assert.NotEqual(new SpecVersion(1, 2, 0), new SpecVersion(1, 2, 1));
        Assert.NotEqual(new SpecVersion(1, 2, 0), new SpecVersion(1, 3, 0));
        Assert.NotEqual(new SpecVersion(1, 2, 0), new SpecVersion(2, 2, 0));
    }

    [Theory]
    [InlineData(1, 2, 0, 1, 1, 5, true)]
    [InlineData(1, 2, 0, 1, 2, 0, true)]
    [InlineData(1, 2, 1, 1, 2, 0, true)]
    [InlineData(1, 1, 0, 1, 2, 0, false)]
    [InlineData(2, 0, 0, 1, 9, 9, false)]
    [InlineData(1, 0, 0, 2, 0, 0, false)]
    public void IsBackwardCompatibleWith_follows_S100_part2_rules(
        int newerMajor, int newerMinor, int newerClar,
        int olderMajor, int olderMinor, int olderClar,
        bool expected)
    {
        var newer = new SpecVersion(newerMajor, newerMinor, newerClar);
        var older = new SpecVersion(olderMajor, olderMinor, olderClar);
        Assert.Equal(expected, newer.IsBackwardCompatibleWith(older));
    }

    [Fact]
    public void Comparison_orders_lexicographically()
    {
        Assert.True(new SpecVersion(1, 2, 0) < new SpecVersion(1, 2, 1));
        Assert.True(new SpecVersion(1, 2, 0) < new SpecVersion(1, 3, 0));
        Assert.True(new SpecVersion(1, 2, 0) < new SpecVersion(2, 0, 0));
        Assert.True(new SpecVersion(2, 0, 0) > new SpecVersion(1, 99, 99));
        Assert.True(new SpecVersion(1, 2, 0) >= new SpecVersion(1, 2, 0));
        Assert.True(new SpecVersion(1, 2, 0) <= new SpecVersion(1, 2, 0));
    }
}
