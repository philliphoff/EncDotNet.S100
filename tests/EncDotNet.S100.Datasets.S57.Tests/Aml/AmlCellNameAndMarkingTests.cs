namespace EncDotNet.S100.Datasets.S57.Tests.Aml;

/// <summary>AML cell-name decoding and the "highest wins" marking rule (#1236, decision 11).</summary>
public class AmlCellNameAndMarkingTests
{
    [Theory]
    [InlineData("UKC6U114.000", "UK", "CLB", 6, 'U', false)]
    [InlineData("UKR0UT00", "UK", "RAL", 0, 'U', false)]
    [InlineData("NOC6RA3B.000", "NO", "CLB", 6, 'R', false)]
    [InlineData("AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000", "DEU", "CLB", 7, 'U', true)]
    [InlineData("AMLDEU0U_NKKK5026_QRT_TrialData", "DEU", "QRT", 0, 'U', true)]
    [InlineData("AMLDEU0U_NK_FMETEST.000", "DEU", null, 0, 'U', true)]
    [InlineData("/some/folder/AMLDEU0R_NKLK0540_LBO-TrialData.000", "DEU", "LBO", 0, 'R', true)]
    public void TryParse_DecodesAmlNames(string name, string producer, string? product, int band, char letter, bool aml30)
    {
        Assert.True(AmlCellName.TryParse(name, out var parsed));
        Assert.Equal(producer, parsed!.Producer);
        Assert.Equal(product, parsed.Product);
        Assert.Equal(band, parsed.ScaleBand);
        Assert.Equal(letter, parsed.ClassificationLetter);
        Assert.Equal(aml30, parsed.IsAml30Pattern);
    }

    [Theory]
    [InlineData("US5MA1BO.000")]
    [InlineData("GB40162A.000")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsOtherNames(string? name) => Assert.False(AmlCellName.TryParse(name, out _));

    [Theory]
    [InlineData('U', "UNCLASSIFIED")]
    [InlineData('r', "RESTRICTED")]
    [InlineData('C', null)]
    public void CellNameLetter_MapsToMarking(char letter, string? marking) =>
        Assert.Equal(marking, AmlProtectiveMarking.FromCellNameLetter(letter));

    [Fact]
    public void Rank_OrdersTheKnownMarkings_AndPutsOtherTextOnTop()
    {
        Assert.True(AmlProtectiveMarking.Rank("unclassified") < AmlProtectiveMarking.Rank("RESTRICTED"));
        Assert.True(AmlProtectiveMarking.Rank("RESTRICTED") < AmlProtectiveMarking.Rank("NATO  RESTRICTED"));
        Assert.True(AmlProtectiveMarking.Rank("NATO RESTRICTED") < AmlProtectiveMarking.Rank("UK EYES ONLY"));
    }

    [Fact]
    public void Highest_PicksTheHighestMarking()
    {
        var summary = AmlProtectiveMarking.Highest(["UNCLASSIFIED", null, "restricted", "UNCLASSIFIED"])!;

        Assert.Equal("RESTRICTED", summary.Highest);
        Assert.Equal(0, summary.OthersAtTop);
        Assert.Equal("RESTRICTED", summary.DisplayText);
        Assert.True(summary.IsAboveUnclassified);
    }

    [Fact]
    public void Highest_ShowsTheFirstPlusTheOthers_WhenDifferentMarkingsTieAtTheTop()
    {
        var summary = AmlProtectiveMarking.Highest(["UK EYES ONLY", "NATO RESTRICTED", "SECRET", "UK EYES ONLY"])!;

        Assert.Equal("UK EYES ONLY", summary.Highest);
        Assert.Equal("UK EYES ONLY +1", summary.DisplayText);
    }

    [Fact]
    public void Highest_IsNull_WhenThereAreNoMarkings() =>
        Assert.Null(AmlProtectiveMarking.Highest([null, " "]));

    [Theory]
    [InlineData(0, null, null, null)]
    [InlineData(6, 50_000, 125_000, 20_000)]
    [InlineData(9, 1_600, 1_500, null)]
    public void ScaleBands_FollowTheGuidanceTable(int band, int? compilation, int? minimum, int? maximum) =>
        Assert.Equal(new AmlScaleBandRange(compilation, minimum, maximum), AmlScaleBands.DisplayRange(band));
}
