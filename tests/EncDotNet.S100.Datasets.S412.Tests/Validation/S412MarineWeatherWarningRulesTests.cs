using System.Text;
using EncDotNet.S100.Datasets.S412.DataModel;
using EncDotNet.S100.Datasets.S412.Validation;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S412.Tests.Validation;

/// <summary>
/// The S-412 rule pack: the synthetic dataset is clean, each rule fires on an
/// edited copy of it, and the upstream sample's defects are reported.
/// </summary>
public class S412MarineWeatherWarningRulesTests
{
    // LF line endings whatever the checkout uses (Windows runners check out
    // CRLF), so edits that span lines apply everywhere.
    private static readonly string Synthetic = File.ReadAllText(S412DatasetReaderTests.SyntheticFile).ReplaceLineEndings("\n");

    private static ValidationReport Validate(string gml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(gml));
        return S412MarineWeatherWarningRules.Validate(S412MarineWeatherWarnings.From(S412Dataset.Open(stream), out _));
    }

    // The synthetic dataset with one edit; the edit must apply.
    private static ValidationReport ValidateEdited(string find, string replace)
    {
        Assert.Contains(find, Synthetic, StringComparison.Ordinal);
        return Validate(Synthetic.Replace(find, replace, StringComparison.Ordinal));
    }

    private static List<ValidationFinding> Findings(ValidationReport report, string ruleId) =>
        [.. report.Findings.Where(f => f.RuleId == ruleId)];

    [Fact]
    public void Synthetic_IsClean()
    {
        var report = Validate(Synthetic);

        Assert.Empty(report.Findings);
        Assert.Equal(14, S412MarineWeatherWarningRules.Default.Rules.Count);
    }

    [Fact]
    public void Sample_ReportsItsDefectsAsWarnings()
    {
        var report = S412MarineWeatherWarningRules.Validate(
            S412MarineWeatherWarnings.From(S412Dataset.Open(S412DatasetReaderTests.SampleFile), out _));

        Assert.All(report.Findings, f => Assert.Equal(ValidationSeverity.Warning, f.Severity));
        Assert.All(report.Findings, f => Assert.Equal("ID1", f.RelatedFeatureId));
        Assert.All(report.Findings, f => Assert.NotNull(f.Point));
        Assert.Equal(
            ["S412-R-1.1", "S412-R-5.1", "S412-R-5.1", "S412-R-5.1", "S412-R-5.1", "S412-R-5.3", "S412-R-6.2"],
            report.Findings.Select(f => f.RuleId));
    }

    [Fact]
    public void R1_1_MissingMandatoryAttribute()
    {
        var report = ValidateEdited("<S412:weatherWarningNumber>W-002</S412:weatherWarningNumber>", "");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-1.1", finding.RuleId);
        Assert.Equal("WAVE1", finding.RelatedFeatureId);
        Assert.Contains("weatherWarningNumber", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R1_1_MissingTypeSpecificAttribute()
    {
        var report = ValidateEdited("<S412:iceAccretionRate code=\"2\">severeIcing</S412:iceAccretionRate>", "");

        Assert.Contains("iceAccretionRate", Assert.Single(Findings(report, "S412-R-1.1")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R1_2_IncompleteFixedDateRange()
    {
        var report = ValidateEdited("<S412:timeOfDayEnd>12:00:00Z</S412:timeOfDayEnd>", "");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-1.2", finding.RuleId);
        Assert.Equal("VIS1", finding.RelatedFeatureId);
    }

    [Fact]
    public void R1_3_DataProviderWithoutContactAgency()
    {
        var report = ValidateEdited("<S412:agencyName>Example Marine Forecast Desk</S412:agencyName>", "");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-1.3", finding.RuleId);
        Assert.Contains("contactInformation.agencyName", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R1_4_InformationWithoutText()
    {
        var report = ValidateEdited("<S412:text>45 knots</S412:text>", "");

        Assert.Equal("S412-R-1.4", Assert.Single(report.Findings).RuleId);
    }

    [Fact]
    public void R2_1_UnresolvedDataProvider()
    {
        var report = ValidateEdited(
            "<S412:cancellationDate>2026-11-09T00:00:00Z</S412:cancellationDate>\n      <S412:dataProviderInformation xlink:href=\"#DP1\"/>",
            "<S412:cancellationDate>2026-11-09T00:00:00Z</S412:cancellationDate>\n      <S412:dataProviderInformation xlink:href=\"#NOPE\"/>");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-2.1", finding.RuleId);
        Assert.Equal("WAVE1", finding.RelatedFeatureId);
    }

    [Fact]
    public void R2_2_DuplicateIdentifier()
    {
        var report = ValidateEdited("gml:id=\"TS1\"", "gml:id=\"VIS1\"");

        Assert.Contains("'VIS1' is used 2 times", Assert.Single(Findings(report, "S412-R-2.2")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R3_1_OpenRing()
    {
        var report = ValidateEdited(
            "52.0 -28.0 53.0 -24.0 51.0 -22.0 50.0 -26.0 52.0 -28.0",
            "52.0 -28.0 53.0 -24.0 51.0 -22.0 50.0 -26.0 52.5 -28.0");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-3.1", finding.RuleId);
        Assert.Equal("TS1", finding.RelatedFeatureId);
    }

    [Fact]
    public void R3_2_PositionOutOfRange()
    {
        var report = ValidateEdited(
            "62.0 -26.0 62.0 -20.0 60.0 -20.0 60.0 -26.0 62.0 -26.0",
            "62.0 -26.0 62.0 -200.0 60.0 -20.0 60.0 -26.0 62.0 -26.0");

        Assert.Equal("ICE1", Assert.Single(Findings(report, "S412-R-3.2")).RelatedFeatureId);
    }

    [Fact]
    public void R5_1_TimeOfDayWithoutSeconds()
    {
        var report = ValidateEdited("<S412:timeOfDayStart>06:00:00Z</S412:timeOfDayStart>", "<S412:timeOfDayStart>0600</S412:timeOfDayStart>");

        Assert.Contains("hhmmss", Assert.Single(report.Findings).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R5_1_DateTimeWithoutTime()
    {
        var report = ValidateEdited("<S412:cancellationDate>2026-11-09T00:00:00Z</S412:cancellationDate>", "<S412:cancellationDate>2026-11-09</S412:cancellationDate>");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-5.1", finding.RuleId);
        Assert.Equal("WAVE1", finding.RelatedFeatureId);
    }

    [Fact]
    public void R5_2_RangeEndsBeforeItStarts()
    {
        var report = ValidateEdited("<S412:dateEnd><S100:date>2026-11-10</S100:date></S412:dateEnd>", "<S412:dateEnd><S100:date>2026-11-06</S100:date></S412:dateEnd>");

        Assert.Equal("ICE1", Assert.Single(Findings(report, "S412-R-5.2")).RelatedFeatureId);
    }

    [Fact]
    public void R5_3_CancelledBeforeItStarts()
    {
        var report = ValidateEdited("<S412:cancellationDate>2026-11-08T18:00:00Z</S412:cancellationDate>", "<S412:cancellationDate>2026-11-07T06:00:00Z</S412:cancellationDate>");

        Assert.Equal("WIND1", Assert.Single(Findings(report, "S412-R-5.3")).RelatedFeatureId);
    }

    [Fact]
    public void R5_4_NextUpdateBeforeIssue()
    {
        var report = ValidateEdited("<S412:nextUpdateDateTime>2026-11-07T12:00:00Z</S412:nextUpdateDateTime>", "<S412:nextUpdateDateTime>2026-11-07T05:00:00Z</S412:nextUpdateDateTime>");

        Assert.Equal("S412-R-5.4", Assert.Single(report.Findings).RuleId);
    }

    [Fact]
    public void R6_1_UnlistedValues()
    {
        var report = Validate(Synthetic
            .Replace("<S412:sourceOfIcing code=\"1\">", "<S412:sourceOfIcing code=\"7\">", StringComparison.Ordinal)
            .Replace("<S412:metareaName code=\"1\">", "<S412:metareaName code=\"15\">", StringComparison.Ordinal));

        Assert.Equal(["ICE1", "DP1"], Findings(report, "S412-R-6.1").Select(f => f.RelatedFeatureId));
    }

    [Fact]
    public void R6_2_BeaufortForceDoesNotMatchThreshold()
    {
        var report = ValidateEdited("<S412:beaufortForce code=\"8\">gale</S412:beaufortForce>", "<S412:beaufortForce code=\"10\">storm</S412:beaufortForce>");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("S412-R-6.2", finding.RuleId);
        Assert.Contains("gale force, force 8 or 9", finding.Message, StringComparison.Ordinal);
    }
}
