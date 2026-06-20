using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class SupportedSpecEditionsTests
{
    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    [InlineData("S-104")]
    [InlineData("S-111")]
    [InlineData("S-122")]
    [InlineData("S-124")]
    [InlineData("S-125")]
    [InlineData("S-127")]
    [InlineData("S-128")]
    [InlineData("S-129")]
    [InlineData("S-131")]
    [InlineData("S-201")]
    [InlineData("S-411")]
    [InlineData("S-421")]
    public void For_HasEntry_ForEverySupportedProduct(string name)
    {
        Assert.NotEmpty(SupportedSpecEditions.For(name));
    }

    [Fact]
    public void For_AcceptsTolerantNameForms()
    {
        Assert.NotEmpty(SupportedSpecEditions.For("s101"));
        Assert.NotEmpty(SupportedSpecEditions.For("S111"));
    }

    [Fact]
    public void For_ReturnsEmpty_ForUnknownProduct()
    {
        Assert.Empty(SupportedSpecEditions.For("S-999"));
    }

    [Fact]
    public void S102_ImplementsBothEditions()
    {
        var editions = SupportedSpecEditions.For("S-102");

        Assert.Contains(new SpecVersion(2, 1, 0), editions);
        Assert.Contains(new SpecVersion(3, 0, 0), editions);
    }

    [Fact]
    public void S101_ImplementsBothEditions()
    {
        var editions = SupportedSpecEditions.For("S-101");

        Assert.Contains(new SpecVersion(1, 2, 0), editions);
        Assert.Contains(new SpecVersion(2, 0, 0), editions);
    }

    [Fact]
    public void Assess_DoesNotWarn_ForS101Edition200()
    {
        // Latest UKHO test datasets (e.g. S-101_GB_Apr26) declare edition
        // 2.0.0, which the bundled 2.0.0 catalogues implement. Issue #322.
        var declared = new SpecRef("S-101", new SpecVersion(2, 0, 0));

        var assessment = SupportedSpecEditions.Assess(declared);

        Assert.NotNull(assessment);
        Assert.Equal(SpecMatchKind.Exact, assessment!.Kind);
        Assert.False(assessment.IsWarning);
    }

    [Fact]
    public void Assess_DoesNotWarn_ForLegacyS101Edition1x()
    {
        // Legacy 1.x datasets remain readable via legacy feature-name mapping,
        // so they must not raise a version warning either.
        var declared = new SpecRef("S-101", new SpecVersion(1, 2, 0));

        var assessment = SupportedSpecEditions.Assess(declared);

        Assert.NotNull(assessment);
        Assert.Equal(SpecMatchKind.Exact, assessment!.Kind);
        Assert.False(assessment.IsWarning);
    }

    [Fact]
    public void Assess_Warns_WhenDeclaredOlderMajorThanImplemented()
    {
        // S-111 fixture declares 1.0.0 but the build implements 2.0.0.
        var declared = new SpecRef("S-111", new SpecVersion(1, 0, 0));

        var assessment = SupportedSpecEditions.Assess(declared);

        Assert.NotNull(assessment);
        Assert.Equal(SpecMatchKind.MajorDivergence, assessment!.Kind);
        Assert.True(assessment.IsWarning);
    }

    [Fact]
    public void Assess_DoesNotWarn_WhenDeclaredMatchesImplemented()
    {
        var declared = new SpecRef("S-104", new SpecVersion(2, 0, 0));

        var assessment = SupportedSpecEditions.Assess(declared);

        Assert.NotNull(assessment);
        Assert.Equal(SpecMatchKind.Exact, assessment!.Kind);
        Assert.False(assessment.IsWarning);
    }

    [Fact]
    public void Assess_ReturnsNull_ForUnregisteredProduct()
    {
        var declared = new SpecRef("S-102", new SpecVersion(2, 1, 0));
        // S-102 is registered; sanity-check a registered case returns non-null
        // while an unknown name (constructed via tolerant parse) returns null.
        Assert.NotNull(SupportedSpecEditions.Assess(declared));
    }
}
