using System.Collections.Immutable;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="IceEggCodeBuilder"/>, covering the WMO / SIGRID-3
/// egg-code variations (single ice type through the special cases reported
/// outside the oval) that the S-411 pick report must render.
/// </summary>
public class IceEggCodeBuilderTests
{
    private static string[] Texts(ImmutableArray<IceEggValue> values) =>
        values.Select(v => v.Text).ToArray();

    [Fact]
    public void Build_MockupSeaice0007_MatchesEggAndAnnotations()
    {
        // iceact 70, iceapc [30,30,10,4,4], icesod [91,87,85,95,99], iceflz [5,4,4,4,5]
        var egg = IceEggCodeBuilder.Build(
            "70", "[30, 30, 10, 4, 4]", "[91, 87, 85, 95, 99]", "[5, 4, 4, 4, 5]");

        Assert.NotNull(egg);
        Assert.True(egg!.HasOval);
        Assert.Equal("70", egg.TotalConcentration!.Text);
        Assert.Equal(new[] { "30", "30", "10" }, Texts(egg.PartialConcentrations));
        Assert.Equal(new[] { "91", "87", "85" }, Texts(egg.StagesOfDevelopment));
        Assert.Equal(new[] { "5", "4", "4" }, Texts(egg.FormsOfIce));

        // Fourth thinner class reported outside the oval to the right: Sd 95,
        // Se 99; Cd 4, Ce 4.
        Assert.Equal(new[] { "95", "99" }, Texts(egg.TrailingStagesOfDevelopment));
        Assert.Equal(new[] { "4", "4" }, Texts(egg.TrailingPartialConcentrations));
        Assert.Equal(new[] { "4", "5" }, Texts(egg.TrailingFormsOfIce));
        Assert.Empty(egg.Annotations);
        Assert.False(egg.ConcentrationRowFolded);
    }

    [Fact]
    public void Build_ReturnsNull_WhenEverythingEmpty()
    {
        Assert.Null(IceEggCodeBuilder.Build(null, null, null, null));
        Assert.Null(IceEggCodeBuilder.Build("", "  ", "[]", ""));
    }

    [Fact]
    public void Build_ReturnsNull_WhenOnlySnowDepthPresent()
    {
        Assert.Null(IceEggCodeBuilder.Build(null, null, null, null, snowDepthCm: 12.5));
    }

    [Fact]
    public void Build_WithCoreValueAndSnowDepth_AddsSnowAnnotation()
    {
        var egg = IceEggCodeBuilder.Build("70", null, null, null, snowDepthCm: 12.5);

        Assert.NotNull(egg);
        Assert.Contains(egg!.Annotations, a => a.Role == IceEggValueRole.SnowDepth);
    }

    [Fact]
    public void Build_TwoIceTypes_CarriesTwoColumns()
    {
        // Variation A: Ct 4, two partials 2 2, stages 5 4, forms 3 1.
        var egg = IceEggCodeBuilder.Build("4", "[2, 2]", "[5, 4]", "[3, 1]");

        Assert.NotNull(egg);
        Assert.Equal("4", egg!.TotalConcentration!.Text);
        Assert.Equal(new[] { "2", "2" }, Texts(egg.PartialConcentrations));
        Assert.Equal(new[] { "5", "4" }, Texts(egg.StagesOfDevelopment));
        Assert.Equal(new[] { "3", "1" }, Texts(egg.FormsOfIce));
        Assert.Empty(egg.Annotations);
        Assert.False(egg.ConcentrationRowFolded);
    }

    [Fact]
    public void Build_SingleIceType_FoldsConcentrationRow()
    {
        // Variation B: one type — Ca is redundant, so the partial row folds away.
        var egg = IceEggCodeBuilder.Build("6", "[7]", "[7]", "[5]");

        Assert.NotNull(egg);
        Assert.True(egg!.ConcentrationRowFolded);
        Assert.Empty(egg.PartialConcentrations);
        Assert.Equal(new[] { "7" }, Texts(egg.StagesOfDevelopment));
        Assert.Equal(new[] { "5" }, Texts(egg.FormsOfIce));
    }

    [Fact]
    public void Build_MaxThreeTypes_FlanksFourthClassOutside()
    {
        // Variation C: 4th thinner class exists; only 3 ride in the oval, the
        // 4th (Sd/Cd) flanks the row outside on the right.
        var egg = IceEggCodeBuilder.Build("7", "[1, 1, 3, 2]", "[7, 5, 4, 1]", "[3, 3, 2]");

        Assert.NotNull(egg);
        Assert.Equal(new[] { "1", "1", "3" }, Texts(egg!.PartialConcentrations));
        Assert.Equal(new[] { "7", "5", "4" }, Texts(egg.StagesOfDevelopment));
        Assert.Equal(new[] { "3", "3", "2" }, Texts(egg.FormsOfIce));
        Assert.Equal(new[] { "1" }, Texts(egg.TrailingStagesOfDevelopment));
        Assert.Equal(new[] { "2" }, Texts(egg.TrailingPartialConcentrations));
        Assert.Empty(egg.TrailingFormsOfIce);
        Assert.Empty(egg.Annotations);
    }

    [Theory]
    [InlineData("9+")]
    [InlineData("4-6")]
    [InlineData("X")]
    public void Build_UndeterminedOrRangeTotal_PassesThroughVerbatim(string total)
    {
        // Variation D/E: Ct can be a single value, a range, "9+", or "X".
        var egg = IceEggCodeBuilder.Build(total, "[2, 7, 1]", "[1, 7, 5]", "[3, 5, 2]");

        Assert.NotNull(egg);
        Assert.Equal(total, egg!.TotalConcentration!.Text);
    }

    [Fact]
    public void Build_UndeterminedForm_KeepsXToken()
    {
        // Variation E: "X" marks a form that can't be specified.
        var egg = IceEggCodeBuilder.Build("8", "[1, 5, 2]", "[5, 4, 1]", "[4, 4, X]");

        Assert.NotNull(egg);
        Assert.Equal(new[] { "4", "4", "X" }, Texts(egg!.FormsOfIce));
    }

    [Fact]
    public void Build_OpenWater_OmitsOval()
    {
        // Variation H: open water / no ice — oval omitted, only Ct (0).
        var egg = IceEggCodeBuilder.Build("0", null, null, null);

        Assert.NotNull(egg);
        Assert.False(egg!.HasOval);
        Assert.Equal("0", egg.TotalConcentration!.Text);
        Assert.Empty(egg.PartialConcentrations);
        Assert.Empty(egg.StagesOfDevelopment);
        Assert.Empty(egg.FormsOfIce);
    }

    [Fact]
    public void Build_TotalConcentrationSourceCode_UsesCallerSuppliedCode()
    {
        var egg = IceEggCodeBuilder.Build(
            "70",
            null,
            null,
            null,
            totalConcentrationSourceCode: "totalConcentration");

        Assert.NotNull(egg);
        Assert.Equal("totalConcentration", egg!.TotalConcentration!.SourceCode);
    }

    [Fact]
    public void Build_SpaceSeparatedList_ParsesTokens()
    {
        var egg = IceEggCodeBuilder.Build("9", "1 5 3", "7 5 4", "4 5 4");

        Assert.NotNull(egg);
        Assert.Equal(new[] { "1", "5", "3" }, Texts(egg!.PartialConcentrations));
    }

    [Fact]
    public void Build_QuotedListTokens_StripsSurroundingQuotes()
    {
        // Python-list-style producers quote non-numeric SIGRID-3 tokens; the
        // quotes are serialisation artefacts and must not reach the diagram.
        var egg = IceEggCodeBuilder.Build("9", "['9+', 'X']", "[91, 95]", "['4-6', \"7\"]");

        Assert.NotNull(egg);
        Assert.Equal(new[] { "9+", "X" }, Texts(egg!.PartialConcentrations));
        Assert.Equal(new[] { "4-6", "7" }, Texts(egg.FormsOfIce));
    }

    [Fact]
    public void Build_QuotedTotalConcentration_StripsSurroundingQuotes()
    {
        var singleQuoted = IceEggCodeBuilder.Build("'9+'", null, null, null);
        var doubleQuoted = IceEggCodeBuilder.Build("\"9+\"", null, null, null);

        Assert.NotNull(singleQuoted);
        Assert.Equal("9+", singleQuoted!.TotalConcentration!.Text);
        Assert.NotNull(doubleQuoted);
        Assert.Equal("9+", doubleQuoted!.TotalConcentration!.Text);
        Assert.Null(IceEggCodeBuilder.Build("''", null, null, null));
    }

    [Fact]
    public void Build_AssignsWmoPositionalSymbols()
    {
        var egg = IceEggCodeBuilder.Build("90", "[30, 30, 20, 10]", "[87, 85, 84, 99]", "[7, 6, 5]");

        Assert.NotNull(egg);
        Assert.Equal("Ct", egg!.TotalConcentration!.Symbol);
        Assert.Equal(new[] { "Ca", "Cb", "Cc" }, egg.PartialConcentrations.Select(v => v.Symbol));
        Assert.Equal(new[] { "Sa", "Sb", "Sc" }, egg.StagesOfDevelopment.Select(v => v.Symbol));
        Assert.Equal(new[] { "Fa", "Fb", "Fc" }, egg.FormsOfIce.Select(v => v.Symbol));
        Assert.Equal(new[] { "Sd" }, egg.TrailingStagesOfDevelopment.Select(v => v.Symbol));
        Assert.Equal(new[] { "Cd" }, egg.TrailingPartialConcentrations.Select(v => v.Symbol));
    }

    [Fact]
    public void Build_FifthClass_FlanksWithEForAllRows()
    {
        // Five ice types: 4th and 5th (d, e) flank each row on the right.
        var egg = IceEggCodeBuilder.Build(
            "90", "[40, 30, 20, 7, 3]", "[87, 85, 84, 91, 95]", "[7, 6, 5, 4, 2]");

        Assert.NotNull(egg);
        Assert.Equal(new[] { "Cd", "Ce" }, egg!.TrailingPartialConcentrations.Select(v => v.Symbol));
        Assert.Equal(new[] { "7", "3" }, Texts(egg.TrailingPartialConcentrations));
        Assert.Equal(new[] { "Sd", "Se" }, egg.TrailingStagesOfDevelopment.Select(v => v.Symbol));
        Assert.Equal(new[] { "91", "95" }, Texts(egg.TrailingStagesOfDevelopment));
        Assert.Equal(new[] { "Fd", "Fe" }, egg.TrailingFormsOfIce.Select(v => v.Symbol));
        Assert.Equal(new[] { "4", "2" }, Texts(egg.TrailingFormsOfIce));
    }
}
