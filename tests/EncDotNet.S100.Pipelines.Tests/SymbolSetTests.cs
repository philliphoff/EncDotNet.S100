using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="SymbolSet"/>, the embedded SVG symbols of a portrayal
/// defined in code (#1066). They read the S-104 station symbols.
/// </summary>
public sealed class SymbolSetTests
{
    private const string Prefix = "EncDotNet.S100.Datasets.S104.Symbols";

    private static readonly System.Reflection.Assembly S104 = typeof(S104CoverageStyle).Assembly;

    [Fact]
    public void Symbols_are_named_by_resource_name_without_prefix_or_extension()
    {
        var set = new SymbolSet(S104, Prefix);

        Assert.Contains("S104GAUGE_RISING", set.Names);
        Assert.True(set.Contains("S104GAUGE_RISING"));
        Assert.All(set.Names, name => Assert.DoesNotContain(".", name, StringComparison.Ordinal));
    }

    [Fact]
    public void The_provider_returns_the_resource_text_and_null_for_an_unknown_name()
    {
        var set = new SymbolSet(S104, Prefix);

        using var stream = S104.GetManifestResourceStream(Prefix + ".S104GAUGE_RISING.svg")!;
        using var reader = new StreamReader(stream);
        var expected = reader.ReadToEnd();

        Assert.Equal(expected, set.Provider("S104GAUGE_RISING"));
        Assert.Same(set.Provider("S104GAUGE_RISING"), set.GetSymbol("S104GAUGE_RISING"));
        Assert.Null(set.Provider("S104GAUGE_MISSING"));
        Assert.Null(set.Provider("s104gauge_rising"));
    }

    [Fact]
    public void The_provider_is_one_delegate()
    {
        var set = new SymbolSet(S104, Prefix);

        Assert.Same(set.Provider, set.Provider);
    }

    [Fact]
    public void The_prefix_may_end_with_a_dot()
    {
        Assert.Equal(
            new SymbolSet(S104, Prefix).Names.Order(StringComparer.Ordinal),
            new SymbolSet(S104, Prefix + ".").Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_prefix_with_no_svg_resources_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SymbolSet(S104, "EncDotNet.S100.Datasets.S104.Nothing"));
        Assert.Throws<ArgumentException>(() => new SymbolSet(typeof(SymbolSetTests).Assembly, Prefix));
    }
}
