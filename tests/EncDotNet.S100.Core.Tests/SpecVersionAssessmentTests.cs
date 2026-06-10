using EncDotNet.S100.Core;
using Xunit;

namespace EncDotNet.S100.Core.Tests;

public class SpecVersionAssessmentTests
{
    private static SpecRef Spec(int major, int minor, int clarification = 0)
        => new("S-111", new SpecVersion(major, minor, clarification));

    private static SpecRef SpecNoEdition()
        => new("S-111", default);

    [Fact]
    public void Create_ReturnsNull_WhenNoSupportedEditions()
    {
        Assert.Null(SpecVersionAssessment.Create(Spec(1, 0), []));
    }

    [Fact]
    public void Create_Exact_WhenDeclaredMatchesImplemented()
    {
        var a = SpecVersionAssessment.Create(Spec(2, 0, 0), [new SpecVersion(2, 0, 0)]);

        Assert.NotNull(a);
        Assert.Equal(SpecMatchKind.Exact, a!.Kind);
        Assert.False(a.IsDivergent);
        Assert.False(a.IsWarning);
        Assert.Null(a.BuildMessage());
    }

    [Fact]
    public void Create_Warns_WhenBuildImplementsOlderMinorSameMajor()
    {
        // Declared 1.2 but build implements only 1.0 → CatalogueOlder → warn.
        var a = SpecVersionAssessment.Create(Spec(1, 2, 0), [new SpecVersion(1, 0, 0)]);

        Assert.NotNull(a);
        Assert.Equal(SpecMatchKind.CatalogueOlder, a!.Kind);
        Assert.True(a.IsWarning);
        Assert.Contains("rendering may be incomplete or incorrect", a.BuildMessage());
    }

    [Fact]
    public void Create_Warns_OnMajorDivergence()
    {
        // Declared draft 0.8 with build implementing 2.0.0 → no same-major → warn.
        var a = SpecVersionAssessment.Create(Spec(0, 8, 0), [new SpecVersion(2, 0, 0)]);

        Assert.NotNull(a);
        Assert.Equal(SpecMatchKind.MajorDivergence, a!.Kind);
        Assert.True(a.IsWarning);
        var message = a.BuildMessage();
        Assert.Contains("S-111 0.8.0", message);
        Assert.Contains("S-111 2.0.0", message);
    }

    [Fact]
    public void Create_DoesNotWarn_WhenBuildImplementsNewerBackwardCompatibleEdition()
    {
        // Declared 1.0 but build implements 1.2 on same major → info only.
        var a = SpecVersionAssessment.Create(Spec(1, 0, 0), [new SpecVersion(1, 2, 0)]);

        Assert.NotNull(a);
        Assert.Equal(SpecMatchKind.CatalogueNewerCompatible, a!.Kind);
        Assert.True(a.IsDivergent);
        Assert.False(a.IsWarning);
        Assert.Contains("newer, backward-compatible edition", a.BuildMessage());
    }

    [Fact]
    public void Create_PrefersSameMajorMember_InMultiEditionBuild()
    {
        // S-102-style multi-edition build {2.1, 3.0}; declaring 2.1 must not
        // flag against 3.0.
        var a = SpecVersionAssessment.Create(
            new SpecRef("S-102", new SpecVersion(2, 1, 0)),
            [new SpecVersion(2, 1, 0), new SpecVersion(3, 0, 0)]);

        Assert.NotNull(a);
        Assert.Equal(new SpecVersion(2, 1, 0), a!.Implemented);
        Assert.Equal(SpecMatchKind.Exact, a.Kind);
        Assert.False(a.IsWarning);
    }

    [Fact]
    public void Create_Unknown_WhenDeclaredEditionMissing()
    {
        var a = SpecVersionAssessment.Create(SpecNoEdition(), [new SpecVersion(2, 0, 0)]);

        Assert.NotNull(a);
        Assert.Equal(SpecMatchKind.Unknown, a!.Kind);
        Assert.False(a.IsDivergent);
        Assert.False(a.IsWarning);
        Assert.Null(a.BuildMessage());
        // The implemented edition is still surfaced for display.
        Assert.Equal(new SpecVersion(2, 0, 0), a.Implemented);
    }

    [Fact]
    public void Create_PreservesCatalogueForDisplay()
    {
        var cat = new CatalogueRef("S-111", new SpecVersion(1, 5, 0));
        var a = SpecVersionAssessment.Create(Spec(2, 0, 0), [new SpecVersion(2, 0, 0)], cat);

        Assert.NotNull(a);
        Assert.Equal(cat, a!.Catalogue);
    }
}
