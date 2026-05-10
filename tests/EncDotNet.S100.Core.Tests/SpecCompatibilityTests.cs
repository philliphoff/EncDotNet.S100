using EncDotNet.S100.Core;
using Xunit;

namespace EncDotNet.S100.Core.Tests;

public class SpecCompatibilityTests
{
    private static SpecRef Spec(int major, int minor, int clarification = 0)
        => new("S-101", new SpecVersion(major, minor, clarification));

    private static CatalogueRef Cat(int major, int minor, int clarification = 0)
        => new("S-101", new SpecVersion(major, minor, clarification));

    // ── IsMatch / Exact ────────────────────────────────────────────────

    [Fact]
    public void Exact_RequiresIdenticalNameAndEdition()
    {
        Assert.True(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(1, 2, 0), SpecMatchPolicy.Exact));
        Assert.False(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(1, 2, 1), SpecMatchPolicy.Exact));
        Assert.False(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(1, 3, 0), SpecMatchPolicy.Exact));
    }

    [Fact]
    public void Exact_DifferentNamesNeverMatch()
    {
        var s101 = new SpecRef("S-101", new SpecVersion(1, 0, 0));
        var s102 = new CatalogueRef("S-102", new SpecVersion(1, 0, 0));
        Assert.False(SpecCompatibility.IsMatch(s101, s102, SpecMatchPolicy.Exact));
    }

    // ── IsMatch / SameMajor ────────────────────────────────────────────

    [Fact]
    public void SameMajor_AllowsHigherMinorOnSameMajor()
    {
        Assert.True(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(1, 3, 0), SpecMatchPolicy.SameMajor));
        Assert.True(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(1, 2, 5), SpecMatchPolicy.SameMajor));
    }

    [Fact]
    public void SameMajor_RejectsLowerMinor()
    {
        Assert.False(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(1, 1, 0), SpecMatchPolicy.SameMajor));
    }

    [Fact]
    public void SameMajor_RejectsDifferentMajor()
    {
        Assert.False(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(2, 2, 0), SpecMatchPolicy.SameMajor));
        Assert.False(SpecCompatibility.IsMatch(Spec(2, 0, 0), Cat(1, 9, 0), SpecMatchPolicy.SameMajor));
    }

    // ── IsMatch / AnyVersion ───────────────────────────────────────────

    [Fact]
    public void AnyVersion_MatchesAnythingWithSameName()
    {
        Assert.True(SpecCompatibility.IsMatch(Spec(1, 2, 0), Cat(99, 99, 99), SpecMatchPolicy.AnyVersion));
        Assert.True(SpecCompatibility.IsMatch(Spec(1, 0, 0), Cat(0, 0, 0), SpecMatchPolicy.AnyVersion));
    }

    [Fact]
    public void AnyVersion_StillRejectsDifferentName()
    {
        var s101 = new SpecRef("S-101", new SpecVersion(1, 0, 0));
        var s102 = new CatalogueRef("S-102", new SpecVersion(1, 0, 0));
        Assert.False(SpecCompatibility.IsMatch(s101, s102, SpecMatchPolicy.AnyVersion));
    }

    // ── Classify ───────────────────────────────────────────────────────

    [Fact]
    public void Classify_EqualVersions_IsExact()
    {
        Assert.Equal(SpecMatchKind.Exact,
            SpecCompatibility.Classify(new SpecVersion(1, 2, 0), new SpecVersion(1, 2, 0)));
    }

    [Fact]
    public void Classify_CatalogueOnNewerMinor_IsCompatible()
    {
        Assert.Equal(SpecMatchKind.CatalogueNewerCompatible,
            SpecCompatibility.Classify(new SpecVersion(1, 2, 0), new SpecVersion(1, 3, 0)));
    }

    [Fact]
    public void Classify_CatalogueOnLowerMinor_IsOlder()
    {
        Assert.Equal(SpecMatchKind.CatalogueOlder,
            SpecCompatibility.Classify(new SpecVersion(1, 3, 0), new SpecVersion(1, 2, 0)));
    }

    [Fact]
    public void Classify_DifferentMajor_IsDivergence()
    {
        Assert.Equal(SpecMatchKind.MajorDivergence,
            SpecCompatibility.Classify(new SpecVersion(1, 0, 0), new SpecVersion(2, 0, 0)));
        Assert.Equal(SpecMatchKind.MajorDivergence,
            SpecCompatibility.Classify(new SpecVersion(2, 0, 0), new SpecVersion(1, 9, 0)));
    }

    [Fact]
    public void Classify_DefaultVersion_IsUnknown()
    {
        Assert.Equal(SpecMatchKind.Unknown,
            SpecCompatibility.Classify(default, new SpecVersion(1, 0, 0)));
        Assert.Equal(SpecMatchKind.Unknown,
            SpecCompatibility.Classify(new SpecVersion(1, 0, 0), default));
    }

    [Fact]
    public void Classify_ClarificationDelta_IsCompatible()
    {
        // Clarification-level differences are interchangeable per S-100
        // Part 2 §6 — same-major, same-minor falls to the compatible bucket.
        Assert.Equal(SpecMatchKind.CatalogueNewerCompatible,
            SpecCompatibility.Classify(new SpecVersion(1, 2, 0), new SpecVersion(1, 2, 1)));
    }
}
